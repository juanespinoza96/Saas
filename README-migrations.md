# Migraciones EF Core — SaaS POS

Guía operativa para gestionar el esquema de base de datos mediante Entity Framework Core Migrations.

---

## Prerequisitos

| Herramienta | Versión mínima | Instalación |
|-------------|---------------|-------------|
| .NET SDK | 9.0 | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| dotnet-ef | 9.0+ | `dotnet tool install --global dotnet-ef` |
| PostgreSQL | 15+ | Instancia local o remota |

### Connection String

El connection string se configura en `src/SaasPOS.Api/appsettings.json` (o `appsettings.Development.json`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=saaspos;Username=postgres;Password=tu_password"
  }
}
```

> **Nota:** En producción, usar variables de entorno o un gestor de secretos en lugar de almacenar credenciales en archivos de configuración.

---

## Comandos Principales

Todos los comandos se ejecutan desde la **raíz de la solución** (`SaasPOS.slnx`).

### Listar migraciones existentes

```bash
dotnet ef migrations list \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api
```

### Agregar una nueva migración

```bash
dotnet ef migrations add <NombreDescriptivo> \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api
```

Ejemplo:
```bash
dotnet ef migrations add AddCampoDescuento \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api
```

### Aplicar migraciones pendientes

```bash
dotnet ef database update \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api
```

### Revertir a una migración específica (rollback)

```bash
dotnet ef database update <NombreMigracionDestino> \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api
```

Para revertir **todas** las migraciones (esquema limpio):

```bash
dotnet ef database update 0 \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api
```

### Eliminar la última migración (no aplicada)

```bash
dotnet ef migrations remove \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api
```

> **Importante:** Solo funciona si la migración NO ha sido aplicada a la base de datos.

### Generar script SQL para producción

```bash
dotnet ef migrations script \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api \
  --output migrations-initial.sql \
  --idempotent
```

El flag `--idempotent` genera un script seguro para ejecutar múltiples veces (verifica en `__EFMigrationsHistory` antes de aplicar cada migración).

Para generar un script desde una migración específica hasta otra:

```bash
dotnet ef migrations script MigracionOrigen MigracionDestino \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api \
  --output migration-delta.sql \
  --idempotent
```

---

## Reglas Operativas

### 1. Nunca modificar migraciones ya aplicadas

Una vez que una migración ha sido aplicada en **cualquier entorno** (desarrollo compartido, staging o producción), **NO se debe modificar**. Si se necesita corregir algo, crear una nueva migración con los cambios necesarios.

### 2. Nombrar migraciones descriptivamente

Usar nombres que describan el cambio realizado:
- ✅ `AddPaymentFields`
- ✅ `CreateIndexOnVentasFecha`
- ✅ `AddPermitePrecioNegociado`
- ❌ `Update1`
- ❌ `Fix`
- ❌ `Cambios`

### 3. Probar antes de desplegar

Antes de hacer merge a la rama principal:
1. Aplicar la migración en una base de datos limpia
2. Verificar que el rollback funciona sin errores
3. Verificar que los datos existentes no se pierden (si aplica)

### 4. Una migración por cambio lógico

Cada migración debe representar un cambio atómico y coherente. No mezclar cambios no relacionados en una sola migración.

### 5. No usar `EnsureCreated()` fuera de tests

`EnsureCreated()` está reservado exclusivamente para tests de integración con Testcontainers. En desarrollo y producción, siempre usar migraciones.

### 6. Versionar las migraciones en Git

La carpeta `src/SaasPOS.Infrastructure/Data/Migrations/` debe estar versionada en el repositorio. Nunca agregar esta carpeta al `.gitignore`.

---

## Flujo de Trabajo para Producción

### Paso 1: Generar el script SQL

```bash
dotnet ef migrations script \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api \
  --output migration-produccion.sql \
  --idempotent
```

### Paso 2: Revisión por el DBA

El DBA revisa el script generado para verificar:
- No hay operaciones destructivas inesperadas (DROP TABLE, DROP COLUMN)
- Los índices y constraints son apropiados
- No hay bloqueos largos en tablas con muchos registros
- El script es idempotente (puede ejecutarse múltiples veces sin error)

### Paso 3: Aplicar en staging

Ejecutar el script SQL en el entorno de staging primero y validar que la aplicación funciona correctamente con el nuevo esquema.

### Paso 4: Aplicar en producción

Ejecutar el script SQL en producción durante una ventana de mantenimiento (si incluye cambios destructivos) o directamente si son cambios aditivos.

### Paso 5: Verificar

Confirmar que la tabla `__EFMigrationsHistory` registra las migraciones aplicadas:

```sql
SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId";
```

---

## Procedimiento de Rollback

### Rollback en desarrollo

```bash
# Revertir a la migración anterior
dotnet ef database update <MigraciónAnterior> \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api

# Eliminar la migración fallida del código
dotnet ef migrations remove \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api
```

### Rollback en producción

1. Generar el script de rollback:
```bash
dotnet ef migrations script MigracionActual MigracionAnterior \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api \
  --output rollback.sql
```

2. Revisar el script con el DBA
3. Aplicar en staging para validar
4. Aplicar en producción

> **⚠️ Precaución:** Un rollback puede causar pérdida de datos si la migración agregó columnas que ya contienen datos. Siempre hacer backup antes de revertir en producción.

---

## Migraciones Existentes

| Migración | Descripción |
|-----------|-------------|
| `20260620051231_InitialCreate` | Esquema inicial completo (todas las tablas, índices, FKs) |
| `20260623121700_AddPaymentFields` | Campos de métodos de pago en Ventas |
| `20260625100000_AddPermitePrecioNegociado` | Campo para precio negociado en configuración |
| `20260717180830_AddTimezoneColumns` | Columnas de zona horaria |
| `20260804204508_AddTrialFields` | Campos para período de prueba en Comercios |

---

## Estructura del Proyecto de Migraciones

```
src/SaasPOS.Infrastructure/
├── Data/
│   ├── AppDbContext.cs                          # DbContext principal
│   ├── DesignTimeDbContextFactory.cs            # Factory para design-time (migraciones)
│   ├── Configurations/                          # EntityTypeConfiguration<T> por entidad
│   └── Migrations/
│       ├── 20260620051231_InitialCreate.cs      # Migración inicial
│       ├── 20260620051231_InitialCreate.Designer.cs
│       ├── ...                                  # Migraciones subsiguientes
│       └── AppDbContextModelSnapshot.cs         # Estado actual del modelo
```

---

## Resolución de Problemas

### Error: "No migrations were found"

Verificar que el flag `--project` apunta al proyecto correcto:
```bash
--project src/SaasPOS.Infrastructure
```

### Error: "Unable to create a 'DbContext'"

Verificar que:
1. El `DesignTimeDbContextFactory` existe en `SaasPOS.Infrastructure/Data/`
2. El `appsettings.json` en `SaasPOS.Api` tiene el connection string configurado
3. El flag `--startup-project` apunta a `src/SaasPOS.Api`

### Error: "The migration has already been applied"

Si necesitas regenerar una migración ya aplicada, primero revierte:
```bash
dotnet ef database update <MigraciónAnterior> \
  --project src/SaasPOS.Infrastructure \
  --startup-project src/SaasPOS.Api
```

Luego elimina y recrea la migración.

### Conflicto de migraciones en Git

Si dos desarrolladores crean migraciones simultáneamente:
1. El segundo developer revierte su migración local
2. Hace pull de la rama principal
3. Regenera su migración sobre el nuevo snapshot

---

## Scripts de Utilidad

- `migrations-initial.sql` — Script SQL idempotente con todas las migraciones, listo para uso del DBA en producción.
