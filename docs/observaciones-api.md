# Observaciones sobre la API — Hallazgos de verificación con Postman

Este documento recoge hallazgos sobre el comportamiento de la API detectados durante la
verificación de las colecciones Postman (Auth, Ventas, Seguridad). No describe cambios
aplicados al código: salvo que se indique lo contrario, la lógica de la API **no fue
modificada**. Son notas para que el equipo decida si actuar sobre ellas.

---

## 1. Login de usuario inactivo: 401 genérico (comportamiento intencional)

### Resumen

Cuando un usuario con `Activo = false` intenta iniciar sesión, la API responde **401
"Credenciales inválidas"**, el mismo error genérico que para un email inexistente o una
contraseña incorrecta. **No** responde 403 con un código `USER_INACTIVE`.

### Por qué es intencional (no es un bug)

En `src/SaasPOS.Infrastructure/Services/AuthService.cs`, el método `LoginAsync` incluye:

```csharp
// Usuario desactivado: retornar error genérico para evitar enumeración de cuentas (Req 23.x)
if (!user.Activo)
    return AuthResult.Fail(GenericError);
```

`AuthResult.Fail(GenericError)` se invoca **sin** segundo argumento, por lo que el
`ErrorCode` queda en `null`. En consecuencia, en `AuthController.Login` la rama
`if (result.ErrorCode == "USER_INACTIVE")` nunca se cumple para este caso y la respuesta
cae a la rama genérica de credenciales inválidas (401).

Esto es una **medida anti-enumeración de cuentas (Req 23.x)**: devolver un 403
`USER_INACTIVE` distinguible permitiría a un atacante saber que una cuenta existe pero
está desactivada. Mantener el 401 genérico hace que los tres casos (email inexistente,
contraseña incorrecta, cuenta inactiva) sean indistinguibles desde fuera.

### Observación de mantenibilidad (código muerto)

`AuthController.Login` conserva una rama explícita:

```csharp
if (result.ErrorCode == "USER_INACTIVE")
{
    LogFailedLoginAsync(request.Email, ipAddress, "Usuario inactivo");
    return StatusCode(StatusCodes.Status403Forbidden, new { message = result.ErrorMessage, code = "USER_INACTIVE" });
}
```

Esta rama es **inalcanzable** en el flujo normal de login, porque `AuthService.LoginAsync`
nunca emite el `ErrorCode` `"USER_INACTIVE"` (usa el error genérico a propósito). El
comportamiento resultante es correcto y seguro; la rama es código muerto que podría
confundir a quien lea el controlador en el futuro.

**Decisión actual:** se deja como está. No se eliminó para no modificar la lógica de la
API. Si en el futuro se decide limpiar, la opción recomendada es **eliminar la rama
`USER_INACTIVE` del controlador** (y no al revés: hacer que el servicio emita el código
reintroduciría el canal de enumeración que el Req 23.x busca evitar).

### Cómo se verifica

La colección **Seguridad - SaasPOS**, request **"S2.1 Login usuario inactivo → 401
genérico (anti-enumeración)"**, confirma este comportamiento: valida que la respuesta sea
401, que el cuerpo **no** incluya `code` ni la cadena `USER_INACTIVE`/`inactiv`, y que el
mensaje sea el genérico "Credenciales inválidas".

---

## 2. Violación de FK en `LogsAuditoria` al registrar eventos de seguridad (CORREGIDO)

### Resumen

Al registrar ciertos eventos de auditoría de seguridad, la base de datos lanzaba una
**violación de llave foránea** (`PostgresException 23503` sobre
`FK_LogsAuditoria_Comercios_ComercioId`). El `INSERT` en `LogsAuditoria` usaba
`ComercioId = 0`, pero no existe ningún comercio con `Id = 0`, por lo que la FK fallaba.

### Mensaje de error observado en la terminal

```
fail: Microsoft.EntityFrameworkCore.Database.Command[20102]
      Failed executing DbCommand ... INSERT INTO "LogsAuditoria" (...) VALUES (...) RETURNING "Id";
fail: Microsoft.EntityFrameworkCore.Update[10000]
      Npgsql.PostgresException (0x80004005): 23503: inserción o actualización en la tabla
      «LogsAuditoria» viola la llave foránea «FK_LogsAuditoria_Comercios_ComercioId»
      ...
      ConstraintName: FK_LogsAuditoria_Comercios_ComercioId
```

### Causa raíz

`LogsAuditoria.ComercioId` era `NOT NULL` con FK a `Comercios.Id`. Cuatro rutas de
auditoría insertaban con `ComercioId = 0` (valor centinela sin fila correspondiente):

- `SecurityAuditService.LogAuthenticationFailedAsync` — acción `AutenticacionFallida`.
- `SecurityAuditService.LogAccessViolationAsync` — acción `ViolacionAcceso` (cuando
  `comercioId` llegaba null, usaba `?? 0`).
- `SecurityAuditService.LogInputValidationErrorAsync` — acción `ErrorValidacionEntrada`.
- `TrialService` — acción `Trial_Rechazado` (intento de trial sin comercio asociado).

Estos eventos ocurren **a nivel de sistema**, antes o fuera de un contexto de comercio
(p. ej. un login fallido: el usuario aún no está autenticado), por lo que no deberían
exigir un `ComercioId` válido.

### Impacto

- Los eventos de seguridad más sensibles (login fallido, violación de acceso, error de
  validación de entrada) **no se persistían**: el `INSERT` fallaba y la auditoría se
  perdía silenciosamente (incumpliendo Req 3.4 / 23.10).
- El error estaba envuelto en `try/catch` fire-and-forget, por lo que **no rompía la
  respuesta** al usuario, pero generaba ruido de errores en los logs.

### Corrección aplicada (Opción A — esquema)

- `LogAuditoria.ComercioId` pasa a `int?` (nullable).
- Relación en `AppDbContext`: `IsRequired(false)` + `OnDelete(DeleteBehavior.SetNull)`
  (mismo patrón que ya tenía `UsuarioId`).
- Las cuatro rutas de auditoría ahora asignan `null` (o el `comercioId` real cuando
  existe) en lugar de `0`.
- `LogAuditoriaDto.ComercioId` pasa a `int?` para reflejar el nuevo modelo.
- Migración EF: `LogAuditoriaComercioIdNullable` (altera la columna a nullable y la FK a
  `SET NULL`).

No se levantó una spec: es un bugfix acotado (una entidad, su configuración EF, una
migración y cuatro asignaciones de servicio), con causa raíz y solución claras.

### Escenario de reproducción (para probar antes/después de la corrección)

El error se dispara con cualquier evento de seguridad sin comercio. Con las colecciones
Postman existentes se reproduce así:

1. **Login fallido** (ruta más directa): ejecutar el request
   **"Login Failed - Rate Limit Info"** de la colección **Auth - SaasPOS** (envía
   credenciales inválidas a `POST /api/tenants/auth/login`). Internamente llama a
   `LogAuthenticationFailedAsync`.
2. **Login de usuario inactivo**: request **"S2.1"** de la colección
   **Seguridad - SaasPOS** (`inactivo@demo.com`). También dispara
   `LogAuthenticationFailedAsync`.
3. **Violación de acceso de Cajero**: request **"1.5 V2 - GET Ventas con Cajero"** de la
   colección **Ventas - SaasPOS** (Cajero sin visibilidad). Dispara
   `LogAccessViolationAsync`.

**Antes de la corrección:** la petición HTTP respondía normal (401/403), pero en la
terminal del API aparecía el error `23503` sobre `FK_LogsAuditoria_Comercios_ComercioId`,
y **no** se creaba la fila de auditoría.

**Verificación después de la corrección:**

1. Reiniciar la API (para aplicar el nuevo esquema; en Development `EnsureCreated`/migración).
2. Ejecutar cualquiera de los tres requests anteriores.
3. **Criterio de éxito:**
   - La terminal del API **ya no muestra** el error `23503` ni el stack de
     `DbUpdateException`.
   - La fila de auditoría **sí se registra** con `ComercioId = NULL`. Verificable con:
     ```sql
     SELECT "Id", "ComercioId", "UsuarioId", "Accion", "FechaHora"
     FROM "LogsAuditoria"
     WHERE "Accion" IN ('AutenticacionFallida','ViolacionAcceso','ErrorValidacionEntrada')
     ORDER BY "Id" DESC
     LIMIT 10;
     ```
     Deben aparecer filas nuevas con `ComercioId` en `NULL` y sin que el `INSERT` falle.
