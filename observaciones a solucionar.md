# Observaciones a Solucionar

> Problemas reales detectados a partir del análisis de arquitectura del proyecto SaaS POS multi-tenant (.NET 9, Clean Architecture).
> Cada observación fue verificada contra el código real (solo lectura). Priorizadas por impacto.
> Documentos base: `.agents/tasks/arquitectura-saaspos-2026-10-01/informe-arquitectura.md` y `analisis-riesgos-observaciones.md`.

---

## Tabla resumen priorizada

| Prioridad | Observación | Problema real | Atributo de calidad | Severidad | ¿Problema HOY? |
|-----------|-------------|---------------|---------------------|-----------|----------------|
| 1 | Estado de seguridad en memoria | JTI blocklist, lockout e IP blocking como singletons en `IMemoryCache`, no compartidos ni persistentes | Seguridad | **Alta** (si multi-instancia) | Sí, con ≥2 nodos o ante reinicio |
| 2 | `EnsureCreated()` sin migraciones | Deriva (drift) entre el SQL de producción y las entidades EF, sin validación automática | Corrección de datos / Disponibilidad / Mantenibilidad | **Media–Alta** | Riesgo latente desde ya |
| 3 | Doble verificación de comercio suspendido | Consulta redundante a `Comercios` en 2 middlewares por request | Rendimiento / Mantenibilidad | **Baja–Media** | Consulta duplicada: sí. Divergencia: no |
| 4 | CORS `AllowAnyHeader`/`AllowAnyMethod` | Métodos y cabeceras abiertos (orígenes sí restringidos) | Seguridad (hardening) | **Baja** | Mayormente teórico |

---

## 1. Estado de seguridad en memoria (singletons sobre `IMemoryCache`) — Severidad: Alta

**Problema real:** toda la revocación de sesiones (JTI blocklist) y la protección anti-fuerza-bruta (lockout, bloqueo de IP) vive en la memoria de un solo proceso. En un despliegue multi-instancia cada nodo tiene su propio estado, y cualquier reinicio de proceso borra todo ese estado.

**Evidencia en código:**
- `src/SaasPOS.Infrastructure/Services/JtiBlocklist.cs` — `IMemoryCache` con prefijos `jti:blocked:`, `jti:user:`, `jti:comercio:`. Singleton.
- `src/SaasPOS.Infrastructure/Services/AccountLockoutService.cs` — `IMemoryCache`, 10 intentos / 30 min → bloqueo 30 min. Singleton.
- `src/SaasPOS.Infrastructure/Services/IpBlockingService.cs` — `IMemoryCache`, ventana 5 min, bloqueo 30 min tras 500 req. Singleton.
- `ILoginAttemptTracker` — Singleton.
- Mitigación parcial existente: `JtiValidationMiddleware` verifica `Usuario.Activo` en BD con caché de 30s. Solo cubre la desactivación de usuario persistida; NO cubre revocación de token individual, lockout ni IP blocking.

**Escenarios de fallo concretos:**
1. **Token revocado que sigue vivo:** se revoca un JTI en el nodo A; el balanceador enruta al atacante al nodo B, que no conoce la revocación → el token se acepta hasta que expire.
2. **Fuerza bruta repartida:** con N nodos, el umbral efectivo de bloqueo pasa de 10 a ~10×N intentos.
3. **IP blocking evadido:** una IP bloqueada en un nodo sigue entrando por los demás.
4. **Reinicio de proceso:** un deploy/reinicio de pod vacía `IMemoryCache` → todos los JTI revocados, lockouts e IP bloqueadas desaparecen. Aplica incluso en instancia única.

**Impacto:** revocación de sesión no confiable, protección anti-fuerza-bruta debilitada (Req 23.8), bypass de IP blocking. El sistema "parece" funcionar pero la protección es ilusoria en multi-instancia.

**Recomendación:** mover el estado a un store distribuido (Redis vía `IDistributedCache`) para JTI blocklist, lockout, IP blocking y tracker de intentos. Alternativamente, persistir revocaciones JTI en una tabla con caché corta (como ya se hace con `Usuario.Activo`). Mientras tanto, documentar que el despliegue debe ser de instancia única o con sticky sessions (los sticky sessions NO mitigan el reinicio ni una revocación emitida en otro nodo).

---

## 2. `EnsureCreated()` sin migraciones EF Core — Severidad: Media–Alta

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

**Recomendación:** adoptar migraciones EF Core (`dotnet ef migrations add`) como fuente única de verdad; o, si se mantiene el SQL autoritativo, añadir en CI una verificación esquema↔modelo levantando `BaseData_Completa.sql` en un contenedor Testcontainers.PostgreSql (ya está en el stack de tests) y consultando cada DbSet para detectar columnas faltantes.

---

## 3. Doble verificación de comercio suspendido en dos middlewares — Severidad: Baja–Media

**Problema real:** dos middlewares consecutivos consultan la tabla `Comercios` para verificar si el comercio está suspendido, duplicando una consulta por cada request a `/api/tenants/**`. La divergencia por diferencia de query filters que temía el informe original **NO puede ocurrir**.

**Evidencia en código:**
- `src/SaasPOS.Api/Middleware/RouteAuthorizationMiddleware.cs` (`IsComercioSuspendidoAsync`) — consulta `Comercios` con `IgnoreQueryFilters()` y cachea el resultado en `context.Items["__ComercioSuspendido"]`.
- `src/SaasPOS.Api/Middleware/TenantContextMiddleware.cs` — vuelve a consultar `Comercios` (sin leer la caché anterior) → segunda consulta a la BD.
- Clave: en `AppDbContext.OnModelCreating`, la entidad `Comercio` **no tiene `HasQueryFilter`**. Por tanto, la consulta con `IgnoreQueryFilters()` y la consulta sin él producen el mismo SQL y el mismo resultado. **La divergencia es imposible** con el modelo actual.

**Escenario de fallo concreto:** no hay fallo de corrección. El "fallo" es de rendimiento: 2 SELECT a `Comercios` por el mismo `comercioId` en lugar de 1 (la caché de `context.Items` no se reutiliza).

**Impacto:** rendimiento (consulta extra barata, PK lookup de una columna) y mantenibilidad (lógica duplicada en dos lugares con mensajes de error distintos: "COMMERCE_SUSPENDED" vs `{ mensaje: ... }`).

**Nota de honestidad:** el informe de arquitectura original sobreestimó este riesgo al advertir una posible divergencia por query filters. Verificado el código, esa divergencia no es posible.

**Recomendación:** consolidar la verificación de suspensión en un solo punto. Opción simple: que `TenantContextMiddleware` reutilice la caché `context.Items["__ComercioSuspendido"]` que ya deja `RouteAuthorizationMiddleware`, eliminando la segunda consulta y unificando el mensaje de error. Opción más limpia: dejar la verificación en un solo middleware.

---

## 4. CORS con `AllowAnyHeader` / `AllowAnyMethod` — Severidad: Baja (mayormente teórico)

**Problema real:** la política CORS `SaaS` abre todos los métodos y cabeceras, pero restringe los orígenes a dos valores de configuración. Como CORS es un control basado en **origen**, abrir métodos/headers tiene impacto real muy limitado.

**Evidencia en código:**
- `src/SaasPOS.Api/Program.cs` — política `SaaS`: `WithOrigins(posOrigin, adminOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()`.
- Nota positiva: los orígenes SÍ están limitados (dos, configurables). NO se incurre en el antipatrón peligroso `AllowAnyOrigin() + AllowCredentials()` (que ASP.NET Core prohíbe en runtime).

**Escenario de fallo concreto:** un sitio malicioso `https://evil.com` no recibe cabeceras CORS que le permitan leer la respuesta, porque solo los dos orígenes de confianza están autorizados. No hay robo de datos cross-origin viable solo por tener métodos/headers abiertos. El riesgo residual se limita a defensa en profundidad (p. ej. si un origen de confianza sufriera XSS, o una futura mala configuración del origen).

**Impacto:** seguridad solo marginal (minimización de superficie / hardening). No compromete corrección ni disponibilidad.

**Recomendación (opcional, baja prioridad):** restringir a los métodos usados (`GET, POST, PUT, PATCH, DELETE, OPTIONS`) y a las cabeceras necesarias (`Authorization, Content-Type` y las personalizadas del cliente). Es hardening, no una corrección urgente.

---

## Conclusión

De las 4 observaciones, solo **2 merecen atención real a corto/mediano plazo**:
- **#1 (seguridad en memoria):** bloqueante antes de escalar a multi-instancia.
- **#2 (deriva de esquema):** riesgo latente que se materializa en cada release sin control.

Las observaciones **#3 y #4 son de bajo impacto**, y la #3 estaba sobredimensionada en el informe original.
