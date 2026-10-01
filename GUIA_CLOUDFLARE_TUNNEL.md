# Guía: Exponer la aplicación a Internet con Cloudflare Tunnel

Esta guía explica cómo levantar el **Backend (API .NET 9)** y el **Frontend POS (React + Vite)**
y exponerlos a Internet mediante túneles de **Cloudflare** (`cloudflared`), para poder acceder
a la aplicación desde otro dispositivo/red.

> Puertos locales del proyecto:
> - **Backend API**: `http://localhost:5291`
> - **Frontend POS**: `http://localhost:3000`
> - **Frontend Admin** (opcional): `http://localhost:3001`

---

## 0. Requisitos previos (una sola vez)

1. Tener instalado el **.NET 9 SDK** y **Node.js 18+**.
2. Instalar **cloudflared** (cliente de túneles de Cloudflare):

   **Windows (con winget):**
   ```cmd
   winget install --id Cloudflare.cloudflared
   ```

   **Windows (con Chocolatey):**
   ```cmd
   choco install cloudflared
   ```

   Verifica la instalación:
   ```cmd
   cloudflared --version
   ```

> Con túneles **rápidos/efímeros** (`trycloudflare.com`) NO necesitas cuenta de Cloudflare
> ni configurar un dominio. Cada vez que levantes el túnel obtendrás una URL nueva.

---

## 1. Levantar el Backend (API .NET 9)

Abre una terminal en la raíz del proyecto y ejecuta:

```cmd
dotnet run --project src/SaasPOS.Api
```

Deja esta terminal abierta. El API quedará escuchando en `http://localhost:5291`.

> Consejo: para verificar que funciona, abre `http://localhost:5291/swagger` en el navegador.

---

## 2. Crear el túnel del Backend

Abre una **segunda terminal** y ejecuta:

```cmd
cloudflared tunnel --url http://localhost:5291
```

Cloudflare mostrará una URL pública parecida a:

```
https://algo-aleatorio-xxxx.trycloudflare.com
```

**Copia esa URL.** Es la URL pública de tu API. Déjala esta terminal abierta.

---

## 3. Apuntar el Frontend al túnel del Backend

El frontend POS lee la URL del API desde la variable `VITE_API_BASE_URL`.

1. Edita el archivo `src/SaasPOS.Web/.env`:

   ```dotenv
   VITE_API_BASE_URL=https://algo-aleatorio-xxxx.trycloudflare.com
   ```

   Reemplaza el valor por la **URL del túnel del Backend** que copiaste en el paso 2.

> IMPORTANTE: Vite lee las variables `.env` al **iniciar**. Si cambias el `.env`,
> debes **reiniciar** el servidor del frontend (paso 4) para que tome el nuevo valor.

---

## 4. Levantar el Frontend POS

Abre una **tercera terminal** en la carpeta del frontend POS:

```cmd
cd src/SaasPOS.Web
npm install   REM solo la primera vez o si cambiaron dependencias
npm run dev
```

El frontend quedará en `http://localhost:3000`. Deja esta terminal abierta.

> El `vite.config.ts` ya tiene `allowedHosts: true`, por lo que aceptará peticiones
> desde el dominio del túnel sin configuración adicional.

---

## 5. Crear el túnel del Frontend

Abre una **cuarta terminal** y ejecuta:

```cmd
cloudflared tunnel --url http://localhost:3000
```

Copia la URL pública que aparezca (otra `https://...trycloudflare.com`).
**Esta es la URL que compartes/abres desde el otro dispositivo** para usar el POS.

---

## 6. Ajustar CORS del Backend (paso clave)

El Backend solo acepta peticiones desde los orígenes configurados en CORS. Como el
frontend ahora se sirve desde un dominio de Cloudflare, debes autorizarlo.

Edita `src/SaasPOS.Api/appsettings.Development.json` y coloca la **URL del túnel del Frontend**
(la del paso 5) en `PosOrigin`:

```json
"Cors": {
  "PosOrigin": "https://url-del-frontend-xxxx.trycloudflare.com",
  "AdminOrigin": "http://localhost:3001"
}
```

Luego **reinicia el Backend** (Ctrl+C en la terminal del paso 1 y vuelve a ejecutar
`dotnet run --project src/SaasPOS.Api`) para que tome el nuevo origen.

> Nota: el origen NO debe llevar `/` al final.

---

## Resumen del orden de arranque

| Terminal | Comando | Deja abierta |
|----------|---------|--------------|
| 1 | `dotnet run --project src/SaasPOS.Api` | Sí |
| 2 | `cloudflared tunnel --url http://localhost:5291` → copiar URL API | Sí |
| —  | Editar `src/SaasPOS.Web/.env` con la URL del API | — |
| —  | Editar `appsettings.Development.json` → `Cors:PosOrigin` con la URL del Front | — |
| 3 | `cd src/SaasPOS.Web` → `npm run dev` | Sí |
| 4 | `cloudflared tunnel --url http://localhost:3000` → copiar URL Front | Sí |
| 1 | Reiniciar el Backend para aplicar el CORS | Sí |

Flujo de dependencias:

```
Backend (5291) ──> Túnel API ──> .env del Front (VITE_API_BASE_URL)
Frontend (3000) ─> Túnel Front ─> Cors:PosOrigin del Backend
```

Abre la **URL del túnel del Frontend** desde cualquier dispositivo con Internet y listo.

---

## Notas importantes

- **URLs efímeras**: con `trycloudflare.com` las URLs cambian cada vez que reinicias
  `cloudflared`. Si eso pasa, repite los pasos 3 y 6 con las nuevas URLs y reinicia
  Front y Backend según corresponda.
- **URL fija (opcional)**: si necesitas una URL estable, crea una cuenta en Cloudflare,
  ejecuta `cloudflared tunnel login`, `cloudflared tunnel create <nombre>` y configura
  un dominio propio con un archivo de configuración de túnel con nombre. Eso evita que
  la URL cambie en cada reinicio.
- **Exponer el Admin (opcional)**: repite el mismo patrón: levanta `SaasPOS.Admin`
  (`http://localhost:3001`), crea un túnel a ese puerto y pon esa URL en `Cors:AdminOrigin`.
- **Seguridad**: mientras el túnel esté activo, tu API/Front están accesibles desde
  Internet. Cierra las terminales de `cloudflared` (Ctrl+C) cuando termines para
  cerrar el acceso público.
