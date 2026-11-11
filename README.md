# Saas

## ⚠️ Interruptor temporal de rate limit para E2E (pendiente de retirar)

> **Estado: inactivo por defecto; solo se activa con las variables de entorno indicadas. Retirar cuando termine la estabilización de la suite E2E.**

Para ejecutar la suite E2E de Playwright (`src/SaasPOS.Web`) se añadió en
`src/SaasPOS.Api/Program.cs` un interruptor que permite **omitir el bloqueo por IP y el
rate limiter** mientras corren las pruebas.

**Por qué.** La suite E2E lanza cientos de peticiones desde una sola IP (`127.0.0.1`) en
pocos minutos. Eso dispara el bloqueo por IP (`IpBlockingService`: 500 req / 5 min →
`403 IP_BLOCKED`) y el rate limiter, y los tests fallan con errores falsos (p. ej. no
aparece el `401 Credenciales inválidas` esperado porque la petición ni siquiera llega al
controlador).

**Cómo se usa (solo Development).** Arrancar la API con estas variables de entorno:

```powershell
$env:Security__DisableIpBlocking = "true"
$env:Security__DisableRateLimiter = "true"
dotnet run --project src/SaasPOS.Api
```

Por defecto ambos valen `false`, así que **sin esas variables el comportamiento es el
normal**. El interruptor solo surte efecto en `Development`; en producción se ignora.
No afecta al bloqueo de cuenta (`AccountLockoutService`) ni al límite de 3 intentos del
`AuthController`.

**Cómo restaurar (OBLIGATORIO al terminar).** Reiniciar la API **sin** esas variables de
entorno. Para retirar el interruptor del código por completo, volver a dejar en
`Program.cs` únicamente:

```csharp
app.UseMiddleware<IpBlockingMiddleware>();
app.UseRateLimiter();
```


## Mejoras pospuestas

Este apartado registra mejoras detectadas que se decidió **no aplicar todavía**, con su motivo, su causa y la solución prevista, para retomarlas en el momento adecuado.

### Future flags de React Router v7 (SaasPOS.Web)

**Qué se observa.** Al ejecutar `npm run test` en `src/SaasPOS.Web` aparecen dos avisos de React Router (v6) en `LoginPage`, `CambiarPasswordPage`, la bandeja de recuperación de `UsuariosPage` y `RequirePasswordChangeGuard`:

- `v7_startTransition`: en v7 las actualizaciones de estado del router se envolverán en `React.startTransition`.
- `v7_relativeSplatPath`: en v7 cambiará la resolución de rutas relativas dentro de rutas con splat (`*`).

**Causa.** El router se monta sin los *future flags*, de modo que React Router avisa de que su comportamiento cambiará al migrar a v7. No es un defecto: la aplicación y los tests funcionan correctamente con la v6 actual.

**Por qué se pospone.** Activar los flags **cambia el comportamiento de navegación en producción**, no solo la salida de los tests. Requiere probar con calma los flujos de rutas con splat y rutas relativas (por ejemplo la redirección a `/cambiar-password`), y no aporta valor funcional inmediato. El riesgo de no hacerlo es subir a v7 sin haber migrado y que la navegación cambie sin aviso.

**Solución prevista.**

1. Activar los flags en la configuración del router (y en los `MemoryRouter`/`BrowserRouter` usados en los tests), por ejemplo:

   ```tsx
   <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
   ```

   Con `createBrowserRouter`/`RouterProvider` el equivalente es pasar `future` en las opciones del router.
2. Revisar y ajustar los enlaces y rutas relativas dentro de rutas con splat.
3. Ejecutar la suite completa (`npm run test`) y una prueba manual de navegación (login, cambio de contraseña obligatorio, rutas protegidas).
4. Verificar si `src/SaasPOS.Admin` usa React Router con la misma configuración y aplicar el mismo cambio allí.