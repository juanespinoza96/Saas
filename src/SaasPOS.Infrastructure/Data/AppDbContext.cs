using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;

namespace SaasPOS.Infrastructure.Data;

public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // DbSets
    public DbSet<Plan> Planes => Set<Plan>();
    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<ConfiguracionComercio> ConfiguracionesComercio => Set<ConfiguracionComercio>();
    public DbSet<Suscripcion> Suscripciones => Set<Suscripcion>();
    public DbSet<PagoComercio> PagosComercio => Set<PagoComercio>();
    public DbSet<Sucursal> Sucursales => Set<Sucursal>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<AtributoCategoria> AtributosCategoria => Set<AtributoCategoria>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<RecetaProducto> RecetasProducto => Set<RecetaProducto>();
    public DbSet<PrecioVolumen> PreciosVolumen => Set<PrecioVolumen>();
    public DbSet<StockSucursal> StockSucursal => Set<StockSucursal>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Venta> Ventas => Set<Venta>();
    public DbSet<DetalleVenta> DetalleVentas => Set<DetalleVenta>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<ColaCorreo> ColaCorreos => Set<ColaCorreo>();
    public DbSet<LogAuditoria> LogsAuditoria => Set<LogAuditoria>();
    public DbSet<ConfiguracionSucursal> ConfiguracionesSucursal => Set<ConfiguracionSucursal>();
    public DbSet<ConfiguracionComprobante> ConfiguracionesComprobante => Set<ConfiguracionComprobante>();
    public DbSet<SolicitudRecuperacion> SolicitudesRecuperacion => Set<SolicitudRecuperacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Plans ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Plan>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Nombre).HasMaxLength(100).IsRequired();
            e.Property(p => p.Precio).HasColumnType("numeric(10,2)");
        });

        // ── Comercios ────────────────────────────────────────────────────────
        modelBuilder.Entity<Comercio>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Ruc).HasMaxLength(20).IsRequired();
            e.HasIndex(c => c.Ruc).IsUnique().HasDatabaseName("ix_comercios_ruc");
            e.Property(c => c.RazonSocial).HasMaxLength(200).IsRequired();
            e.Property(c => c.RutaFirmaElectronica).HasMaxLength(500);
            e.Property(c => c.ClaveFirmaEncriptada).HasMaxLength(500);
            e.Property(c => c.Estado).HasMaxLength(20).IsRequired().HasDefaultValue("Activo");
            e.Property(c => c.ZonaHorariaDefecto).HasMaxLength(50);
            e.Ignore(c => c.Activo); // Computed property, not a DB column
            e.HasOne(c => c.Plan)
                .WithMany(p => p.Comercios)
                .HasForeignKey(c => c.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── ConfiguracionesComercio ──────────────────────     ────────────────────
        modelBuilder.Entity<ConfiguracionComercio>(e =>
        {
            e.HasKey(c => c.ComercioId);
            e.HasOne(c => c.Comercio)
                .WithOne(co => co.Configuracion)
                .HasForeignKey<ConfiguracionComercio>(c => c.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Suscripciones ────────────────────────────────────────────────────
        modelBuilder.Entity<Suscripcion>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.MontoCuota).HasColumnType("numeric(10,2)");
            e.Property(s => s.Estado).HasMaxLength(50).IsRequired();
            e.Property(s => s.DiasExtendidos).HasDefaultValue(0);
            e.HasOne(s => s.Comercio)
                .WithMany(c => c.Suscripciones)
                .HasForeignKey(s => s.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Plan)
                .WithMany(p => p.Suscripciones)
                .HasForeignKey(s => s.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => s.Estado).HasDatabaseName("idx_suscripciones_estado");
            e.HasIndex(s => s.FechaProximoCorte).HasDatabaseName("idx_suscripciones_corte");

            // Índice parcial para búsqueda eficiente de trials por estado y fecha de corte
            e.HasIndex(s => new { s.Estado, s.FechaProximoCorte })
                .HasDatabaseName("idx_suscripciones_trial")
                .HasFilter("\"Estado\" IN ('Trial', 'Trial_Expirado')");

            // Multi-tenant query filter
            e.HasQueryFilter(s => _tenantContext.IsSuperAdmin || s.ComercioId == _tenantContext.ComercioId);
        });

        // ── PagosComercio ────────────────────────────────────────────────────
        modelBuilder.Entity<PagoComercio>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.MontoPagado).HasColumnType("numeric(10,2)");
            e.Property(p => p.MetodoPago).HasMaxLength(100);
            e.Property(p => p.Referencia).HasMaxLength(200);
            e.HasOne(p => p.Comercio)
                .WithMany(c => c.Pagos)
                .HasForeignKey(p => p.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Suscripcion)
                .WithMany(s => s.Pagos)
                .HasForeignKey(p => p.SuscripcionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.UsuarioRegistrador)
                .WithMany(u => u.PagosRegistrados)
                .HasForeignKey(p => p.RegistradoPor)
                .OnDelete(DeleteBehavior.Restrict);

            // Multi-tenant query filter
            e.HasQueryFilter(p => _tenantContext.IsSuperAdmin || p.ComercioId == _tenantContext.ComercioId);
        });

        // ── Sucursales ───────────────────────────────────────────────────────
        modelBuilder.Entity<Sucursal>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Nombre).HasMaxLength(200).IsRequired();
            e.Property(s => s.Telefono).HasMaxLength(20);
            e.Property(s => s.SerieFacturacion).HasMaxLength(10);
            e.HasOne(s => s.Comercio)
                .WithMany(c => c.Sucursales)
                .HasForeignKey(s => s.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);

            // Multi-tenant query filter
            e.HasQueryFilter(s => _tenantContext.IsSuperAdmin || s.ComercioId == _tenantContext.ComercioId);
        });

        // ── ConfiguracionesSucursal ──────────────────────────────────────────
        modelBuilder.Entity<ConfiguracionSucursal>(entity =>
        {
            entity.ToTable("ConfiguracionesSucursal");
            entity.HasKey(e => e.SucursalId);
            entity.Property(e => e.EsBarEscolar).HasDefaultValue(false);
            entity.Property(e => e.MostrarBotonCliente).HasDefaultValue(false);
            entity.Property(e => e.PermiteVentaEnNegativo).HasDefaultValue(false);
            entity.Property(e => e.ImpresionAutomaticaTicket).HasDefaultValue(true);
            entity.Property(e => e.PermitePrecioNegociado).HasDefaultValue(false);
            entity.Property(e => e.MostrarVentasAlCajero).HasDefaultValue(false);

            entity.HasOne(e => e.Sucursal)
                  .WithOne(s => s.Configuracion)
                  .HasForeignKey<ConfiguracionSucursal>(e => e.SucursalId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ConfiguracionesComprobante ───────────────────────────────────────
        modelBuilder.Entity<ConfiguracionComprobante>(entity =>
        {
            entity.ToTable("ConfiguracionesComprobante");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TipoComprobante).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Habilitado).HasDefaultValue(true);
            entity.Property(e => e.FechaCreacion).HasDefaultValueSql("NOW()");

            entity.HasIndex(e => new { e.ComercioId, e.TipoComprobante })
                  .IsUnique()
                  .HasDatabaseName("ix_configuraciones_comprobante_comercio_tipo");

            entity.HasOne(e => e.Comercio)
                  .WithMany(c => c.ConfiguracionesComprobante)
                  .HasForeignKey(e => e.ComercioId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Usuarios ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Nombre).HasMaxLength(200).IsRequired();
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ix_usuarios_email");
            e.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(u => u.Rol).HasMaxLength(50).IsRequired();
            e.Property(u => u.ZonaHoraria).HasMaxLength(64);
            // Bandera de cambio obligatorio de contraseña (Requirement 16.1): valor por defecto false
            e.Property(u => u.DebeCambiarPassword).HasDefaultValue(false);
            e.HasOne(u => u.Comercio)
                .WithMany(c => c.Usuarios)
                .HasForeignKey(u => u.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(u => u.Sucursal)
                .WithMany(s => s.Usuarios)
                .HasForeignKey(u => u.SucursalId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(u => u.ComercioId).HasDatabaseName("idx_usuarios_comercio");

            // Multi-tenant query filter
            e.HasQueryFilter(u => _tenantContext.IsSuperAdmin || u.ComercioId == _tenantContext.ComercioId);
        });

        // ── Categorias ───────────────────────────────────────────────────────
        modelBuilder.Entity<Categoria>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Nombre).HasMaxLength(200).IsRequired();
            e.HasOne(c => c.Comercio)
                .WithMany(co => co.Categorias)
                .HasForeignKey(c => c.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);

            // Multi-tenant query filter
            e.HasQueryFilter(c => _tenantContext.IsSuperAdmin || c.ComercioId == _tenantContext.ComercioId);
        });

        // ── AtributosCategoria ───────────────────────────────────────────────
        modelBuilder.Entity<AtributoCategoria>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.NombreAtributo).HasMaxLength(200).IsRequired();
            e.Property(a => a.TipoDato).HasMaxLength(50).IsRequired();
            e.HasOne(a => a.Categoria)
                .WithMany(c => c.Atributos)
                .HasForeignKey(a => a.CategoriaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Productos ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Producto>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Nombre).HasMaxLength(300).IsRequired();
            e.Property(p => p.TipoArticulo).HasMaxLength(50).IsRequired();
            e.Property(p => p.ValoresDinamicos).HasColumnType("jsonb");
            e.Property(p => p.UnidadMedida).HasMaxLength(50);
            e.Property(p => p.CostoProduccion).HasColumnType("numeric(10,2)");
            e.Property(p => p.PrecioLista).HasColumnType("numeric(10,2)");
            e.Property(p => p.PrecioMinimo).HasColumnType("numeric(10,2)");
            e.HasOne(p => p.Comercio)
                .WithMany(c => c.Productos)
                .HasForeignKey(p => p.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Categoria)
                .WithMany(c => c.Productos)
                .HasForeignKey(p => p.CategoriaId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(p => p.ComercioId).HasDatabaseName("idx_productos_comercio");
            e.HasIndex(p => p.ValoresDinamicos)
                .HasMethod("GIN")
                .HasDatabaseName("idx_productos_jsonb");

            // Multi-tenant query filter
            e.HasQueryFilter(p => _tenantContext.IsSuperAdmin || p.ComercioId == _tenantContext.ComercioId);
        });

        // ── RecetasProducto ──────────────────────────────────────────────────
        modelBuilder.Entity<RecetaProducto>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.CantidadRequerida).HasColumnType("numeric(10,4)");
            e.HasOne(r => r.ProductoFinal)
                .WithMany(p => p.RecetasComoFinal)
                .HasForeignKey(r => r.ProductoFinalId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Ingrediente)
                .WithMany(p => p.RecetasComoIngrediente)
                .HasForeignKey(r => r.IngredienteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PreciosVolumen ────────────────────────────────────────────────────
        modelBuilder.Entity<PrecioVolumen>(e =>
        {
            e.HasKey(pv => pv.Id);
            e.Property(pv => pv.CantidadMinima).HasColumnType("numeric(10,4)");
            e.Property(pv => pv.PrecioEspecial).HasColumnType("numeric(10,2)");
            e.HasOne(pv => pv.Producto)
                .WithMany(p => p.PreciosVolumen)
                .HasForeignKey(pv => pv.ProductoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── StockSucursal ─────────────────────────────────────────────────────
        modelBuilder.Entity<StockSucursal>(e =>
        {
            e.HasKey(ss => ss.Id);
            e.Property(ss => ss.CantidadFisica).HasColumnType("numeric(10,4)");
            e.Property(ss => ss.StockMinimo).HasColumnType("numeric(10,4)").HasDefaultValue(0m);
            e.HasIndex(ss => new { ss.ProductoId, ss.SucursalId })
                .IsUnique()
                .HasDatabaseName("ix_stock_sucursal_producto");
            e.HasOne(ss => ss.Producto)
                .WithMany(p => p.Stocks)
                .HasForeignKey(ss => ss.ProductoId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ss => ss.Sucursal)
                .WithMany(s => s.Stocks)
                .HasForeignKey(ss => ss.SucursalId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Clientes ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Cliente>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Identificacion).HasMaxLength(20).IsRequired();
            e.Property(c => c.Nombre).HasMaxLength(300).IsRequired();
            e.Property(c => c.Correo).HasMaxLength(256);
            e.Property(c => c.Telefono).HasMaxLength(20);
            e.HasIndex(c => new { c.ComercioId, c.Identificacion })
                .IsUnique()
                .HasDatabaseName("ix_clientes_comercio_identificacion");
            e.HasOne(c => c.Comercio)
                .WithMany(co => co.Clientes)
                .HasForeignKey(c => c.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);

            // Multi-tenant query filter
            e.HasQueryFilter(c => _tenantContext.IsSuperAdmin || c.ComercioId == _tenantContext.ComercioId);
        });

        // ── Ventas ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Venta>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Total).HasColumnType("numeric(12,2)");
            e.Property(v => v.TipoComprobante).HasMaxLength(50).IsRequired();
            e.Property(v => v.EstadoSRI).HasMaxLength(50);
            e.Property(v => v.MetodoPago).HasMaxLength(50).IsRequired().HasDefaultValue("Efectivo");
            e.Property(v => v.CuotasMeses).HasDefaultValue(0);
            e.Property(v => v.ValorCuota).HasColumnType("numeric(12,2)");
            e.Property(v => v.ReferenciaTransaccion).HasMaxLength(200);
            e.HasOne(v => v.Comercio)
                .WithMany(c => c.Ventas)
                .HasForeignKey(v => v.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.Sucursal)
                .WithMany(s => s.Ventas)
                .HasForeignKey(v => v.SucursalId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Usuario)
                .WithMany(u => u.Ventas)
                .HasForeignKey(v => v.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Cliente)
                .WithMany(c => c.Ventas)
                .HasForeignKey(v => v.ClienteId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(v => v.ComercioId).HasDatabaseName("idx_ventas_comercio");

            // Multi-tenant query filter
            e.HasQueryFilter(v => _tenantContext.IsSuperAdmin || v.ComercioId == _tenantContext.ComercioId);
        });

        // ── DetalleVentas ─────────────────────────────────────────────────────
        modelBuilder.Entity<DetalleVenta>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Cantidad).HasColumnType("numeric(10,4)");
            e.Property(d => d.PrecioRealCobrado).HasColumnType("numeric(10,2)");
            e.HasOne(d => d.Venta)
                .WithMany(v => v.Detalles)
                .HasForeignKey(d => d.VentaId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Producto)
                .WithMany(p => p.DetalleVentas)
                .HasForeignKey(d => d.ProductoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Notificaciones ────────────────────────────────────────────────────
        modelBuilder.Entity<Notificacion>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Titulo).HasMaxLength(300).IsRequired();
            e.Property(n => n.TipoNotificacion).HasMaxLength(100).IsRequired();
            e.HasOne(n => n.Comercio)
                .WithMany(c => c.Notificaciones)
                .HasForeignKey(n => n.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.Sucursal)
                .WithMany(s => s.Notificaciones)
                .HasForeignKey(n => n.SucursalId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(n => n.Producto)
                .WithMany(p => p.Notificaciones)
                .HasForeignKey(n => n.ProductoId)
                .OnDelete(DeleteBehavior.SetNull);

            // Relación opcional con Suscripcion para verificación de duplicados en trials (ON DELETE SET NULL)
            e.HasOne(n => n.Suscripcion)
                .WithMany()
                .HasForeignKey(n => n.SuscripcionId)
                .OnDelete(DeleteBehavior.SetNull);

            // Índice compuesto para búsqueda rápida de notificaciones por suscripción y tipo
            e.HasIndex(n => new { n.SuscripcionId, n.TipoNotificacion })
                .HasDatabaseName("idx_notificaciones_suscripcion_tipo")
                .HasFilter("\"SuscripcionId\" IS NOT NULL");

            // Multi-tenant query filter
            e.HasQueryFilter(n => _tenantContext.IsSuperAdmin || n.ComercioId == _tenantContext.ComercioId);
        });

        // ── ColaCorreos ───────────────────────────────────────────────────────
        modelBuilder.Entity<ColaCorreo>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Destinatario).HasMaxLength(256).IsRequired();
            e.Property(c => c.Asunto).HasMaxLength(500).IsRequired();
            e.Property(c => c.Estado).HasMaxLength(50).IsRequired();
            // Partial index: only pending emails
            e.HasIndex(c => c.Estado)
                .HasFilter("\"Estado\" = 'Pendiente'")
                .HasDatabaseName("idx_cola_correos_estado");
            e.HasOne(c => c.Comercio)
                .WithMany()
                .HasForeignKey(c => c.ComercioId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── LogsAuditoria ─────────────────────────────────────────────────────
        modelBuilder.Entity<LogAuditoria>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Accion).HasMaxLength(200).IsRequired();
            e.Property(l => l.TablaAfectada).HasMaxLength(100).IsRequired();
            e.Property(l => l.RegistroId).HasMaxLength(100).IsRequired();
            e.Property(l => l.ValoresAnteriores).HasColumnType("jsonb");
            e.Property(l => l.ValoresNuevos).HasColumnType("jsonb");
            e.HasOne(l => l.Comercio)
                .WithMany(c => c.LogsAuditoria)
                .HasForeignKey(l => l.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);
            // ON DELETE SET NULL for UsuarioId
            e.HasOne(l => l.Usuario)
                .WithMany(u => u.LogsAuditoria)
                .HasForeignKey(l => l.UsuarioId)
                .OnDelete(DeleteBehavior.SetNull);

            // SuperAdmin-only: no tenant query filter here (accessed cross-tenant)
        });

        // ── SolicitudesRecuperacion ───────────────────────────────────────────
        modelBuilder.Entity<SolicitudRecuperacion>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Estado).HasMaxLength(20).IsRequired().HasDefaultValue("Pendiente");
            // Contraseña temporal cifrada AES-256 en Base64(IV + ciphertext) (Requirement 16.2): holgura de 1024 caracteres
            e.Property(s => s.PasswordTemporalCifrada).HasMaxLength(1024);
            // Motivo de rechazo opcional (Requirement 16.3)
            e.Property(s => s.MotivoRechazo).HasMaxLength(500);
            // FechaExpiracion (DateTime?) se mapea a timestamp nullable por convención; no requiere configuración explícita.
            e.HasOne(s => s.Usuario)
                .WithMany()
                .HasForeignKey(s => s.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Comercio)
                .WithMany()
                .HasForeignKey(s => s.ComercioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Aprobador)
                .WithMany()
                .HasForeignKey(s => s.AprobadoPor)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => s.ComercioId).HasDatabaseName("idx_solicitudes_recuperacion_comercio");
            e.HasIndex(s => s.Estado)
                .HasFilter("\"Estado\" = 'Pendiente'")
                .HasDatabaseName("idx_solicitudes_recuperacion_estado");
        });
    }
}
