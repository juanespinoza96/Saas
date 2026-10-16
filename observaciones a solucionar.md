# Observaciones a Solucionar

> Problemas reales detectados a partir del análisis de arquitectura del proyecto SaaS POS multi-tenant (.NET 9, Clean Architecture).
> Cada observación fue verificada contra el código real (solo lectura). Priorizadas por impacto.
> Documentos base: `.agents/tasks/arquitectura-saaspos-2026-10-01/informe-arquitectura.md` y `analisis-riesgos-observaciones.md`.

---

## Contexto de despliegue (decisión del equipo)

> **El proyecto NO se desplegará sobre AWS ni sobre ninguna infraestructura multi-nodo por ahora.** Al iniciar tendrá pocos clientes, por lo que el despliegue será de **instancia única** (un solo proceso de la API). Montar Redis, balanceadores o varios nodos sería sobre-ingeniería para la etapa actual.
>
> Esta decisión reescribe las prioridades: todo el riesgo que dependía de "múltiples nodos" (revocación que no viaja entre instancias, fuerza bruta repartida, IP blocking evadido) **no aplica hoy**. El único riesgo de la Observación #1 que sobrevive en instancia única es la **pérdida de estado al reiniciar el proceso** (cada deploy o reinicio), y es mucho más acotado de lo que parecía.
>
> **Regla a futuro:** el día que se decida escalar a más de una instancia, la Observación #1 vuelve a ser de severidad **Alta** y debe resolverse *antes* de poner el segundo nodo en producción. Mientras tanto, se trata con soluciones de bajo costo que usan solo lo que ya existe (PostgreSQL + memoria).

---

## Tabla resumen priorizada (replanteada para instancia única)

| Prioridad | Observación | Problema real HOY (instancia única) | Atributo de calidad | Severidad HOY | Severidad si se escala |
|-----------|-------------|--------------------------------------|---------------------|---------------|------------------------|
| 1 | `EnsureCreated()` sin migraciones | Deriva (drift) entre el SQL de producción y las entidades EF, sin validación automática. No depende del nº de nodos. | Corrección de datos / Disponibilidad / Mantenibilidad | **Media–Alta** | Media–Alta (igual) |
| 2 | Estado de seguridad en memoria | Solo persiste el riesgo de **perder JTI revocados / lockouts / IPs al reiniciar** el proceso. El problema multi-nodo no existe en instancia única. | Seguridad | **Baja–Media** | **Alta** (bloqueante antes de escalar) |
| 3 | Doble verificación de comercio suspendido | Consulta redundante a `Comercios` en 2 middlewares por request. | Rendimiento / Mantenibilidad | **Baja** | Baja |
| 4 | CORS `AllowAnyHeader`/`AllowAnyMethod` | Métodos y cabeceras abiertos (orígenes sí restringidos). | Seguridad (hardening) | **Baja** | Baja |

> **Cambio respecto a la versión anterior:** la Observación #1 (seguridad en memoria) baja de prioridad 1 a prioridad 2, porque en instancia única su peor escenario (multi-nodo) no ocurre. La Observación #2 (deriva de esquema) sube a prioridad 1, porque su riesgo es independiente del modo de despliegue: se materializa en cada release, con uno o con mil nodos.

---
## 1. `EnsureCreated()` sin migraciones EF Core — Severidad HOY: Media–Alta

**Por qué es ahora la prioridad 1:** es el único problema de la lista cuyo riesgo **no cambia** por desplegar en instancia única. Se materializa en cada evolución del esquema, independientemente de cuántos nodos haya.

**Problema real:** existen dos fuentes de verdad para el esquema (el SQL `BaseData_Completa.sql` en producción y la configuración Fluent API de EF), sin ningún mecanismo que garantice que no se desincronicen.

**Evidencia en código:**
- `src/SaasPOS.Api/Program.cs` — en Development ejecuta `db.Database.EnsureCreated();` con comentario "Remove or replace with migrations for production."
- No existe carpeta `Migrations/` en `SaasPOS.Infrastructure`. El mapeo vive solo en `AppDbContext.OnModelCreating`.
- Nota técnica clave: `EnsureCreated()` NO corrige derivas — si la BD ya existe, no añade columnas faltantes (es prácticamente un no-op). `EnsureCreated` y las migraciones son mutuamente excluyentes.

**Escenario de fallo concreto:**
1. Se añade una propiedad a una entidad (p. ej. `Producto.CodigoBarras`) y su `e.Property(...)`.
2. En la máquina de dev, con la BD recreada, funciona.
3. En producción el SQL no incluyó la columna nueva.
4. La primera consulta que proyecte esa columna genera `Npgsql.PostgresException` con `SqlState 42703` ("column does not exist") → el `GlobalExceptionMiddleware` lo devuelve como **500**; el endpoint queda roto.
5. **Variante peor (tipo distinto):** si el SQL define `numeric(10,2)` y la entidad espera otra escala/tipo, hay conversión o truncamiento **silencioso** de datos financieros, sin excepción.

**Impacto:** corrección de datos (truncamiento silencioso de montos en el peor caso), disponibilidad (endpoints caídos con 500), mantenibilidad (dos fuentes de verdad sincronizadas a mano, sin red de seguridad en CI).

**Recomendación (para esta etapa, de menor a mayor esfuerzo):**
- **Opción B — recomendada ahora (mantiene el SQL autoritativo):** añadir un **test automatizado de validación esquema↔modelo** que use `Testcontainers.PostgreSql` (ya está en el stack de tests): levantar `BaseData_Completa.sql` en un contenedor y, para cada `DbSet`, ejecutar una consulta mínima que falle si falta una columna o difiere el tipo. Esto convierte una deriva silenciosa en un **test rojo** antes de desplegar. No cambia la forma de trabajar actual (el SQL sigue siendo la fuente de verdad) y aprovecha infraestructura existente.
- **Opción A — a futuro (fuente única EF):** adoptar migraciones EF Core (`dotnet ef migrations add`) con una migración *baseline* que represente el esquema actual. Más idiomático y la red de seguridad más fuerte, pero obliga a reconciliar el SQL autoritativo existente. Dejar para cuando el esquema se estabilice o el equipo crezca.

---

## 2. Estado de seguridad en memoria (singletons sobre `IMemoryCache`) — Severidad HOY: Baja–Media

**Replanteo por instancia única:** de los cuatro escenarios de fallo originales, **tres desaparecen** porque requieren ≥2 nodos:
- ~~Token revocado que sigue vivo en otro nodo~~ → no hay otro nodo.
- ~~Fuerza bruta repartida entre nodos~~ → un solo contador, el umbral de 10 intentos se respeta.
- ~~IP blocking evadido por otros nodos~~ → una sola lista de IPs, el bloqueo se respeta.

**El único escenario que sobrevive: reinicio de proceso.** Un deploy o reinicio de la app vacía `IMemoryCache`, perdiendo JTI revocados, lockouts e IPs bloqueadas. Su gravedad real, medida contra el código:

- **JWT dura 60 min** (`JwtSettings.ExpirationMinutes = 60`, verificado). Un JTI revocado solo necesita sobrevivir, como máximo, 60 minutos. Tras un reinicio, un token revocado podría volver a ser válido durante lo que le reste de esos 60 min — y solo si el reinicio ocurre dentro de esa ventana. Es una ventana pequeña, no indefinida.
- La **mitigación existente ya cubre el caso más grave:** `JtiValidationMiddleware` verifica `Usuario.Activo` contra la BD (con caché de 30 s). Si un usuario se desactiva (empleado despedido, cuenta comprometida con baja), eso **sí es persistente** y sobrevive al reinicio. Lo único que no sobrevive es la revocación de un *token individual* por `jti` sin desactivar al usuario.
- Lockout e IP blocking perdidos tras un reinicio solo le dan "borrón y cuenta nueva" a un atacante **en el preciso momento del reinicio**, que es un evento raro y controlado (lo haces tú al desplegar).

**Conclusión del replanteo:** en instancia única esto es **Baja–Media**, no Alta. No es bloqueante para salir a producción con pocos clientes.

**Recomendación escalonada (sin Redis, usando solo PostgreSQL + memoria):**

1. **Ahora (barato, cierra el hueco más real):** persistir **solo la revocación de JTI** en una tabla PostgreSQL (p. ej. `JtiRevocados` con `Jti`, `Expiracion`, `FechaRevocacion`), y que `JtiBlocklist` consulte esa tabla con una caché corta encima (igual patrón que ya usa `Usuario.Activo`). Así una revocación de token sobrevive al reinicio. Un barrido periódico (o un índice con filtro por `Expiracion`) mantiene la tabla limpia. Lockout e IP blocking pueden quedarse en memoria: perderlos en un reinicio es tolerable en instancia única.
2. **Documentar la restricción de despliegue:** dejar escrito de forma explícita (README de despliegue o steering) que **mientras el estado de seguridad siga en memoria, el despliegue debe ser de instancia ÚNICA.** Un segundo nodo rompería silenciosamente las garantías de seguridad.
3. **A futuro, antes de escalar a multi-instancia:** introducir un store distribuido (Redis vía `IDistributedCache`) para lockout, IP blocking y tracker de intentos, detrás de una abstracción que permita caer a `IMemoryCache` en desarrollo local. Esta es la pieza que convierte la Observación #2 de nuevo en Alta, pero solo cuando llegue el momento de escalar.

**Nota:** la decisión de no usar AWS/Redis ahora es razonable para la etapa; la clave es que la restricción de "instancia única" quede **documentada y respetada**, porque es una condición de seguridad, no solo de rendimiento.

---

## 3. Doble verificación de comercio suspendido en dos middlewares — Severidad: Baja

**Problema real:** dos middlewares consecutivos consultan la tabla `Comercios` para verificar si el comercio está suspendido, duplicando una consulta por cada request a `/api/tenants/**`. La divergencia por diferencia de query filters que temía el informe original **NO puede ocurrir** (la entidad `Comercio` no tiene `HasQueryFilter`).

**Evidencia en código:**
- `src/SaasPOS.Api/Middleware/RouteAuthorizationMiddleware.cs` (`IsComercioSuspendidoAsync`) — consulta `Comercios` con `IgnoreQueryFilters()` y cachea el resultado en `context.Items["__ComercioSuspendido"]`.
- `src/SaasPOS.Api/Middleware/TenantContextMiddleware.cs` — vuelve a consultar `Comercios` (sin leer la caché anterior) → segunda consulta a la BD.
- Clave: `Comercio` **no tiene `HasQueryFilter`**, por lo que ambas consultas producen el mismo SQL y el mismo resultado. La divergencia es imposible con el modelo actual.

**Impacto:** rendimiento (una consulta extra barata, PK lookup de una columna) y mantenibilidad (lógica duplicada con mensajes de error distintos: "COMMERCE_SUSPENDED" vs `{ mensaje: ... }`).

**Recomendación:** que `TenantContextMiddleware` reutilice la caché `context.Items["__ComercioSuspendido"]` que ya deja `RouteAuthorizationMiddleware`, eliminando la segunda consulta y unificando el mensaje de error. Quick win de bajo riesgo.

---

## 4. CORS con `AllowAnyHeader` / `AllowAnyMethod` — Severidad: Baja (mayormente teórico)

**Problema real:** la política CORS `SaaS` abre todos los métodos y cabeceras, pero restringe los orígenes a dos valores de configuración. Como CORS es un control basado en **origen**, abrir métodos/headers tiene impacto real muy limitado.

**Evidencia en código:**
- `src/SaasPOS.Api/Program.cs` — política `SaaS`: `WithOrigins(posOrigin, adminOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()`.
- Nota positiva: los orígenes SÍ están limitados. NO se incurre en el antipatrón peligroso `AllowAnyOrigin() + AllowCredentials()`.

**Impacto:** seguridad solo marginal (minimización de superficie / hardening). No compromete corrección ni disponibilidad.

**Recomendación (opcional, baja prioridad):** restringir a los métodos usados (`GET, POST, PUT, PATCH, DELETE, OPTIONS`) y a las cabeceras necesarias (`Authorization, Content-Type` y las personalizadas del cliente). Es hardening, no una corrección urgente.

---

## Conclusión (replanteada para instancia única)

Con el despliegue de **instancia única** confirmado y sin AWS:

- **#1 (deriva de esquema) es ahora la de mayor prioridad real.** Su riesgo no depende del nº de nodos y se materializa en cada release. Acción: test de validación esquema↔modelo con Testcontainers (bajo esfuerzo, usa el stack existente).
- **#2 (seguridad en memoria) baja a Baja–Media.** El problema grave era multi-nodo y hoy no aplica. Solo queda la pérdida de estado al reiniciar, acotada por el lifetime de 60 min del token y mitigada en gran parte por la verificación de `Usuario.Activo` en BD. Acción mínima: persistir la revocación de JTI en PostgreSQL y **documentar que el despliegue debe ser de instancia única**. Redis queda explícitamente aplazado hasta que se decida escalar.
- **#3 y #4 siguen siendo quick wins de bajo impacto** (consolidar la consulta de suspensión; endurecer CORS).

**Orden de trabajo sugerido:** #3 y #4 (quick wins rápidos) → #1 (red de seguridad en CI) → #2 (persistir JTI + documentar restricción de instancia única). La migración a Redis/multi-instancia se difiere hasta que el crecimiento de clientes lo justifique.
