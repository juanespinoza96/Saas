using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Api.Extensions;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Helpers;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Tenants;

[ApiController]
[Route("api/tenants/ventas")]
public class VentasController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IStockService _stockService;
    private readonly IAuditService _auditService;
    private readonly IComprobantesConfigService _comprobantesConfigService;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly ITimezoneService _timezoneService;

    public VentasController(
        AppDbContext db,
        ITenantContext tenantContext,
        IStockService stockService,
        IAuditService auditService,
        IComprobantesConfigService comprobantesConfigService,
        ISecurityAuditService securityAuditService,
        ITimezoneService timezoneService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _stockService = stockService;
        _auditService = auditService;
        _comprobantesConfigService = comprobantesConfigService;
        _securityAuditService = securityAuditService;
        _timezoneService = timezoneService;
    }

    /// <summary>
    /// POST /api/tenants/ventas — Register a new sale (POS normal flow).
    /// Req 9.1: Exclude Insumo products from selection.
    /// Req 9.2: Create Venta + DetalleVentas records.
    /// Req 9.3: Generate Ticket Interno without SRI validation.
    /// Req 9.4: Respect MostrarBotonCliente for optional ClienteId.
    /// Req 9.5: Respect ImpresionAutomaticaTicket flag.
    /// Req 9.6: Total = sum(Cantidad × PrecioRealCobrado) with volume pricing.
    /// Req 9.7: Audit trail for the sale.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "CanSell")]
    public async Task<IActionResult> CrearVenta([FromBody] CrearVentaRequest request)
    {
        var comercioId = GetComercioId();
        var usuarioId = GetUsuarioId();

        // ── Validate request ─────────────────────────────────────────────────
        if (request.Lineas is null || request.Lineas.Count == 0)
            return BadRequest(new { error = "La venta debe contener al menos una línea.", code = "VENTA_EMPTY" });

        // Validate TipoComprobante — normalizar valores legacy y validar contra configuración del comercio
        var tipoComprobanteRaw = request.TipoComprobante ?? "Ticket Digital";
        var tipoComprobante = _comprobantesConfigService.NormalizarTipoComprobante(tipoComprobanteRaw);

        // Req 6.10: Validar que el tipo esté habilitado para el comercio
        var tipoHabilitado = await _comprobantesConfigService.EsTipoHabilitadoAsync(comercioId, tipoComprobante);
        if (!tipoHabilitado)
            return BadRequest(new { error = $"El tipo de comprobante '{tipoComprobante}' no está habilitado para este comercio.", code = "TIPO_COMPROBANTE_NO_HABILITADO" });

        // ── Validate MetodoPago (Req 22.1, 22.2) ────────────────────────────
        var metodoPago = request.MetodoPago ?? "Efectivo";
        var metodosValidos = new[] { "Efectivo", "TarjetaCredito", "TarjetaDebito", "Transferencia" };
        if (!metodosValidos.Contains(metodoPago))
            return BadRequest(new { error = "MetodoPago inválido. Valores permitidos: Efectivo, TarjetaCredito, TarjetaDebito, Transferencia.", code = "INVALID_METODO_PAGO" });

        // ── Validate CuotasMeses (Req 22.5, 22.6) ───────────────────────────
        var cuotasMeses = request.CuotasMeses;
        var cuotasPermitidas = new[] { 0, 3, 6, 9, 12, 18 };
        if (!cuotasPermitidas.Contains(cuotasMeses))
            return BadRequest(new { error = "CuotasMeses inválido. Valores permitidos: 0, 3, 6, 9, 12, 18.", code = "INVALID_CUOTAS" });

        // Req 22.5: Deferred payments only allowed with TarjetaCredito
        if (cuotasMeses > 0 && metodoPago != "TarjetaCredito")
            return BadRequest(new { error = "Los pagos diferidos (cuotas) solo están disponibles con Tarjeta de Crédito.", code = "CUOTAS_ONLY_CREDIT_CARD" });

        // ── Validate ReferenciaTransaccion (Req 22.8) ────────────────────────
        // ReferenciaTransaccion is optional but only relevant for card/transfer payments
        if (!string.IsNullOrWhiteSpace(request.ReferenciaTransaccion)
            && metodoPago == "Efectivo")
            return BadRequest(new { error = "ReferenciaTransaccion no aplica para pagos en efectivo.", code = "REFERENCIA_NOT_APPLICABLE" });

        // ── Validate sucursal belongs to the tenant ──────────────────────────
        var sucursalExists = await _db.Sucursales
            .AnyAsync(s => s.Id == request.SucursalId && s.ComercioId == comercioId);

        if (!sucursalExists)
            return BadRequest(new { error = "La sucursal no pertenece al comercio.", code = "INVALID_SUCURSAL" });

        // ── Load ConfiguracionesSucursal (fuente de verdad para toggles de sucursal) ──
        var configSucursal = await _db.ConfiguracionesSucursal
            .FirstOrDefaultAsync(cs => cs.SucursalId == request.SucursalId);

        // Req 9.4: Validate ClienteId only if MostrarBotonCliente is true and ClienteId is provided
        if (request.ClienteId.HasValue)
        {
            if (configSucursal?.MostrarBotonCliente != true)
                return BadRequest(new { error = "La configuración del comercio no permite asociar clientes a ventas.", code = "CLIENTE_NOT_ALLOWED" });

            var clienteExists = await _db.Clientes
                .AnyAsync(c => c.Id == request.ClienteId.Value && c.ComercioId == comercioId);

            if (!clienteExists)
                return BadRequest(new { error = "El cliente no existe o no pertenece al comercio.", code = "INVALID_CLIENTE" });
        }

        // ── Load and validate products ───────────────────────────────────────
        var productoIds = request.Lineas.Select(l => l.ProductoId).Distinct().ToList();

        var productos = await _db.Productos
            .Where(p => productoIds.Contains(p.Id) && p.ComercioId == comercioId)
            .Include(p => p.PreciosVolumen)
            .ToListAsync();

        // Check all products exist and belong to tenant
        var productosDict = productos.ToDictionary(p => p.Id);
        foreach (var productoId in productoIds)
        {
            if (!productosDict.ContainsKey(productoId))
                return BadRequest(new { error = $"Producto con Id {productoId} no encontrado.", code = "PRODUCTO_NOT_FOUND" });
        }

        // Req 9.1: Exclude Insumo products
        var insumos = productos.Where(p => p.TipoArticulo == "Insumo").ToList();
        if (insumos.Count > 0)
            return BadRequest(new { error = $"Los productos de tipo Insumo no pueden ser vendidos directamente: {string.Join(", ", insumos.Select(i => i.Nombre))}.", code = "INSUMO_NOT_SELLABLE" });

        // ── Validate PrecioRealCobrado: positivity and precision (Req 2.4, 2.5) ──
        foreach (var linea in request.Lineas)
        {
            if (linea.PrecioRealCobrado.HasValue)
            {
                if (linea.PrecioRealCobrado.Value <= 0)
                    return BadRequest(new { error = "El precio negociado debe ser mayor a cero.", code = "PRECIO_MUST_BE_POSITIVE" });

                if (linea.PrecioRealCobrado.Value != Math.Round(linea.PrecioRealCobrado.Value, 2))
                    return BadRequest(new { error = "El precio negociado no debe tener más de 2 decimales.", code = "PRECIO_INVALID_PRECISION" });
            }
        }

        // ── Validate toggle/bar escolar para precios negociados (Req 3.1–3.5) ──
        var tieneLineasNegociadas = request.Lineas.Any(l => l.PrecioRealCobrado.HasValue);

        if (tieneLineasNegociadas)
        {
            // EsBarEscolar has priority over toggle
            if (configSucursal?.EsBarEscolar == true)
                return BadRequest(new { error = "La sucursal no permite precios negociados. Active el toggle PermitePrecioNegociado en la configuración.", code = "PRECIO_NEGOCIADO_NOT_ALLOWED" });

            if (configSucursal?.PermitePrecioNegociado != true)
                return BadRequest(new { error = "La sucursal no permite precios negociados. Active el toggle PermitePrecioNegociado en la configuración.", code = "PRECIO_NEGOCIADO_NOT_ALLOWED" });
        }

        // ── Validate precio mínimo (Req 4.1–4.6) ────────────────────────────
        foreach (var linea in request.Lineas)
        {
            if (linea.PrecioRealCobrado.HasValue)
            {
                var producto = productosDict[linea.ProductoId];
                if (producto.PrecioMinimo > 0 && linea.PrecioRealCobrado.Value < producto.PrecioMinimo)
                    return BadRequest(new { error = $"El precio {linea.PrecioRealCobrado.Value} para '{producto.Nombre}' es inferior al mínimo permitido ({producto.PrecioMinimo}).", code = "PRECIO_BELOW_MINIMUM" });
            }
        }

        // ── Calculate prices and build detail lines ──────────────────────────
        var detalles = new List<DetalleVenta>();

        foreach (var linea in request.Lineas)
        {
            if (linea.Cantidad <= 0)
                return BadRequest(new { error = "La cantidad debe ser mayor a 0.", code = "INVALID_CANTIDAD" });

            var producto = productosDict[linea.ProductoId];

            // Req 9.6: Apply volume pricing using PricingHelper
            var reglasVolumen = producto.PreciosVolumen
                .Select(pv => (pv.CantidadMinima, pv.PrecioEspecial))
                .ToList();

            var precioCalculado = PricingHelper.DeterminarPrecioEfectivo(
                producto.PrecioLista,
                linea.Cantidad,
                reglasVolumen);

            // Req 2.2, 2.3: Use PrecioRealCobrado if provided, otherwise use calculated price
            var precioEfectivo = linea.PrecioRealCobrado ?? precioCalculado;

            detalles.Add(new DetalleVenta
            {
                ProductoId = linea.ProductoId,
                Cantidad = linea.Cantidad,
                PrecioRealCobrado = precioEfectivo
            });
        }

        // Req 9.6: Total = sum(Cantidad × PrecioRealCobrado)
        var total = detalles.Sum(d => d.Cantidad * d.PrecioRealCobrado);

        // ── Decrement stock for each line item ───────────────────────────────
        foreach (var detalle in detalles)
        {
            var producto = productosDict[detalle.ProductoId];

            if (!producto.ManejaStock)
                continue;

            StockResult stockResult;
            if (producto.TipoArticulo == "Venta Directa")
            {
                stockResult = await _stockService.DecrementarStockVentaDirectaAsync(
                    detalle.ProductoId, request.SucursalId, detalle.Cantidad, comercioId);
            }
            else // Ensamblado
            {
                stockResult = await _stockService.DecrementarStockEnsambladoAsync(
                    detalle.ProductoId, request.SucursalId, detalle.Cantidad, comercioId);
            }

            if (!stockResult.Success)
                return Conflict(new { error = $"Error de stock para producto '{producto.Nombre}': {stockResult.Error}", code = "STOCK_ERROR" });
        }

        // ── Create Venta record ──────────────────────────────────────────────
        // Req 22.6: Calculate ValorCuota when CuotasMeses > 0
        decimal? valorCuota = cuotasMeses > 0
            ? Math.Round(total / cuotasMeses, 2)
            : null;

        var venta = new Venta
        {
            ComercioId = comercioId,
            SucursalId = request.SucursalId,
            UsuarioId = usuarioId,
            ClienteId = request.ClienteId,
            Total = total,
            TipoComprobante = tipoComprobante,
            MetodoPago = metodoPago,
            CuotasMeses = cuotasMeses,
            ValorCuota = valorCuota,
            ReferenciaTransaccion = string.IsNullOrWhiteSpace(request.ReferenciaTransaccion) ? null : request.ReferenciaTransaccion,
            FechaVenta = DateTime.UtcNow,
            Detalles = detalles
        };

        _db.Ventas.Add(venta);
        await _db.SaveChangesAsync();

        // ── Req 9.7 + Req 6.1–6.3: Audit trail with negotiated price info ───
        // Determine if any line used a negotiated price from the user
        var usoPrecioNegociado = request.Lineas.Any(l => l.PrecioRealCobrado.HasValue);

        // Build audit detail lines with comparative pricing
        var auditDetalles = request.Lineas.Select((linea, index) =>
        {
            var producto = productosDict[linea.ProductoId];
            var reglasVolumen = producto.PreciosVolumen
                .Select(pv => (pv.CantidadMinima, pv.PrecioEspecial))
                .ToList();
            var precioCalculado = PricingHelper.DeterminarPrecioEfectivo(
                producto.PrecioLista,
                linea.Cantidad,
                reglasVolumen);

            return new
            {
                detalles[index].ProductoId,
                detalles[index].Cantidad,
                PrecioRealCobrado = detalles[index].PrecioRealCobrado,
                PrecioCalculado = precioCalculado
            };
        }).ToList();

        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "Crear",
            "Ventas",
            venta.Id.ToString(),
            null,
            new
            {
                venta.Id,
                venta.ComercioId,
                venta.SucursalId,
                venta.UsuarioId,
                venta.ClienteId,
                venta.Total,
                venta.TipoComprobante,
                venta.MetodoPago,
                venta.CuotasMeses,
                venta.ValorCuota,
                venta.ReferenciaTransaccion,
                venta.FechaVenta,
                usoPrecioNegociado,
                Detalles = auditDetalles
            });

        // ── Build response ───────────────────────────────────────────────────
        var response = new VentaResponse(
            Id: venta.Id,
            ComercioId: venta.ComercioId,
            SucursalId: venta.SucursalId,
            UsuarioId: venta.UsuarioId,
            ClienteId: venta.ClienteId,
            Total: venta.Total,
            TipoComprobante: venta.TipoComprobante,
            MetodoPago: venta.MetodoPago,
            CuotasMeses: venta.CuotasMeses,
            ValorCuota: venta.ValorCuota,
            ReferenciaTransaccion: venta.ReferenciaTransaccion,
            FechaVenta: venta.FechaVenta,
            ImpresionAutomatica: configSucursal?.ImpresionAutomaticaTicket ?? false,
            MostrarBotonCliente: configSucursal?.MostrarBotonCliente ?? false,
            Detalles: detalles.Select(d => new DetalleVentaDto(
                Id: d.Id,
                ProductoId: d.ProductoId,
                ProductoNombre: productosDict[d.ProductoId].Nombre,
                Cantidad: d.Cantidad,
                PrecioRealCobrado: d.PrecioRealCobrado,
                Subtotal: d.Cantidad * d.PrecioRealCobrado
            )).ToList());

        return CreatedAtAction(nameof(GetById), new { id = venta.Id }, response);
    }

    /// <summary>
    /// POST /api/tenants/ventas/bar-escolar — Register a quick sale in Bar Escolar mode.
    /// Req 10.1: Activated when ConfiguracionesSucursal.EsBarEscolar = TRUE.
    /// Req 10.2: Quantity = 1, PrecioLista, no intermediate confirmation screens.
    /// Req 10.3: No ClienteId, TipoComprobante = "Ticket Interno".
    /// Req 10.4: BOM-based stock decrement for Ensamblado products.
    /// Req 10.5: Direct stock decrement for Venta Directa products.
    /// </summary>
    [HttpPost("bar-escolar")]
    [Authorize(Policy = "CanSell")]
    public async Task<IActionResult> CrearVentaBarEscolar([FromBody] CrearVentaBarEscolarRequest request)
    {
        var comercioId = GetComercioId();
        var usuarioId = GetUsuarioId();

        // ── Validate EsBarEscolar is enabled (leer de ConfiguracionesSucursal) ──
        var configSucursal = await _db.ConfiguracionesSucursal
            .FirstOrDefaultAsync(cs => cs.SucursalId == request.SucursalId);

        if (configSucursal?.EsBarEscolar != true)
            return StatusCode(403, new { error = "El modo Bar Escolar no está habilitado para este comercio.", code = "BAR_ESCOLAR_DISABLED" });

        // ── Validate sucursal belongs to the tenant ──────────────────────────
        var sucursalExists = await _db.Sucursales
            .AnyAsync(s => s.Id == request.SucursalId && s.ComercioId == comercioId);

        if (!sucursalExists)
            return BadRequest(new { error = "La sucursal no pertenece al comercio.", code = "INVALID_SUCURSAL" });

        // ── Validate product exists, belongs to tenant, and is not Insumo ────
        var producto = await _db.Productos
            .FirstOrDefaultAsync(p => p.Id == request.ProductoId && p.ComercioId == comercioId);

        if (producto is null)
            return BadRequest(new { error = $"Producto con Id {request.ProductoId} no encontrado.", code = "PRODUCTO_NOT_FOUND" });

        if (producto.TipoArticulo == "Insumo")
            return BadRequest(new { error = $"Los productos de tipo Insumo no pueden ser vendidos en modo Bar Escolar.", code = "INSUMO_NOT_SELLABLE" });

        // ── Decrement stock ──────────────────────────────────────────────────
        if (producto.ManejaStock)
        {
            StockResult stockResult;
            if (producto.TipoArticulo == "Venta Directa")
            {
                stockResult = await _stockService.DecrementarStockVentaDirectaAsync(
                    producto.Id, request.SucursalId, 1, comercioId);
            }
            else // Ensamblado
            {
                stockResult = await _stockService.DecrementarStockEnsambladoAsync(
                    producto.Id, request.SucursalId, 1, comercioId);
            }

            if (!stockResult.Success)
                return Conflict(new { error = $"Error de stock para producto '{producto.Nombre}': {stockResult.Error}", code = "STOCK_ERROR" });
        }

        // ── Create Venta record (Req 10.2, 10.3, 22.2) ────────────────────
        var total = 1m * producto.PrecioLista;

        var detalle = new DetalleVenta
        {
            ProductoId = producto.Id,
            Cantidad = 1,
            PrecioRealCobrado = producto.PrecioLista
        };

        var venta = new Venta
        {
            ComercioId = comercioId,
            SucursalId = request.SucursalId,
            UsuarioId = usuarioId,
            ClienteId = null,
            Total = total,
            TipoComprobante = "Ticket Interno",
            MetodoPago = "Efectivo",
            CuotasMeses = 0,
            ValorCuota = null,
            ReferenciaTransaccion = null,
            FechaVenta = DateTime.UtcNow,
            Detalles = new List<DetalleVenta> { detalle }
        };

        _db.Ventas.Add(venta);
        await _db.SaveChangesAsync();

        // ── Audit trail ──────────────────────────────────────────────────────
        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "Crear",
            "Ventas",
            venta.Id.ToString(),
            null,
            new
            {
                venta.Id,
                venta.ComercioId,
                venta.SucursalId,
                venta.UsuarioId,
                venta.ClienteId,
                venta.Total,
                venta.TipoComprobante,
                venta.MetodoPago,
                venta.CuotasMeses,
                venta.ValorCuota,
                venta.ReferenciaTransaccion,
                venta.FechaVenta,
                Modo = "BarEscolar",
                Detalles = new[] { new { detalle.ProductoId, detalle.Cantidad, detalle.PrecioRealCobrado } }
            });

        // ── Build response ───────────────────────────────────────────────────
        var barResponse = new VentaResponse(
            Id: venta.Id,
            ComercioId: venta.ComercioId,
            SucursalId: venta.SucursalId,
            UsuarioId: venta.UsuarioId,
            ClienteId: venta.ClienteId,
            Total: venta.Total,
            TipoComprobante: venta.TipoComprobante,
            MetodoPago: venta.MetodoPago,
            CuotasMeses: venta.CuotasMeses,
            ValorCuota: venta.ValorCuota,
            ReferenciaTransaccion: venta.ReferenciaTransaccion,
            FechaVenta: venta.FechaVenta,
            ImpresionAutomatica: configSucursal?.ImpresionAutomaticaTicket ?? false,
            MostrarBotonCliente: false,
            Detalles: new List<DetalleVentaDto>
            {
                new DetalleVentaDto(
                    Id: detalle.Id,
                    ProductoId: detalle.ProductoId,
                    ProductoNombre: producto.Nombre,
                    Cantidad: detalle.Cantidad,
                    PrecioRealCobrado: detalle.PrecioRealCobrado,
                    Subtotal: detalle.Cantidad * detalle.PrecioRealCobrado)
            });

        return CreatedAtAction(nameof(GetById), new { id = venta.Id }, barResponse);
    }

    /// <summary>
    /// GET /api/tenants/ventas — List sales history for the tenant.
    /// Req 4.6: Retorna 403 si el Cajero no tiene MostrarVentasAlCajero habilitado.
    /// Req 4.7: Valida el toggle consultando ConfiguracionesSucursal de la sucursal del Cajero.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetAll([FromQuery] int? sucursalId, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta)
    {
        var comercioId = GetComercioId();

        // ── Req 4.6, 4.7: Validar visibilidad de ventas para Cajeros ─────────
        var rol = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
        if (rol == "Cajero")
        {
            var cajeroSucursalId = _tenantContext.SucursalId;
            if (cajeroSucursalId.HasValue)
            {
                var configSucursal = await _db.ConfiguracionesSucursal
                    .FirstOrDefaultAsync(cs => cs.SucursalId == cajeroSucursalId.Value);

                if (configSucursal == null || !configSucursal.MostrarVentasAlCajero)
                {
                    // Req 3.4: Registrar violación de acceso en auditoría de seguridad
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    _ = _securityAuditService.LogAccessViolationAsync(
                        comercioId, GetUsuarioId(), ipAddress,
                        HttpContext.Request.Path.ToString(),
                        "Cajero intentó acceder a ventas con MostrarVentasAlCajero deshabilitado");

                    return StatusCode(403, new { code = "VENTAS_VISIBILITY_RESTRICTED", error = "No tiene autorización para ver las ventas" });
                }
            }
            else
            {
                // Si el Cajero no tiene sucursal asignada, denegar acceso por seguridad
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                _ = _securityAuditService.LogAccessViolationAsync(
                    comercioId, GetUsuarioId(), ipAddress,
                    HttpContext.Request.Path.ToString(),
                    "Cajero sin sucursal asignada intentó acceder a ventas");

                return StatusCode(403, new { code = "VENTAS_VISIBILITY_RESTRICTED", error = "No tiene autorización para ver las ventas" });
            }
        }

        var query = _db.Ventas
            .Where(v => v.ComercioId == comercioId);

        if (sucursalId.HasValue)
            query = query.Where(v => v.SucursalId == sucursalId.Value);

        // Normalizar fechas de filtro a UTC usando la zona horaria del usuario
        var desdeUtc = HttpContext.NormalizarFiltroFechaAUtc(desde, _timezoneService);
        var hastaUtc = HttpContext.NormalizarFiltroFechaHastaAUtc(hasta, _timezoneService);

        if (desdeUtc.HasValue)
            query = query.Where(v => v.FechaVenta >= desdeUtc.Value);

        if (hastaUtc.HasValue)
            query = query.Where(v => v.FechaVenta <= hastaUtc.Value);

        var ventas = await query
            .OrderByDescending(v => v.FechaVenta)
            .Select(v => new VentaListDto(
                v.Id,
                v.SucursalId,
                v.UsuarioId,
                v.ClienteId,
                v.Total,
                v.TipoComprobante,
                v.EstadoSRI,
                v.FechaVenta))
            .ToListAsync();

        return Ok(ventas);
    }

    /// <summary>
    /// GET /api/tenants/ventas/{id} — Get sale detail with line items.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetById(int id)
    {
        var comercioId = GetComercioId();

        var venta = await _db.Ventas
            .Where(v => v.Id == id && v.ComercioId == comercioId)
            .Include(v => v.Detalles)
                .ThenInclude(d => d.Producto)
            .FirstOrDefaultAsync();

        if (venta is null)
            return NotFound(new { error = "Venta no encontrada.", code = "VENTA_NOT_FOUND" });

        var configSucursal = await _db.ConfiguracionesSucursal
            .FirstOrDefaultAsync(cs => cs.SucursalId == venta.SucursalId);

        var response = new VentaResponse(
            Id: venta.Id,
            ComercioId: venta.ComercioId,
            SucursalId: venta.SucursalId,
            UsuarioId: venta.UsuarioId,
            ClienteId: venta.ClienteId,
            Total: venta.Total,
            TipoComprobante: venta.TipoComprobante,
            MetodoPago: venta.MetodoPago,
            CuotasMeses: venta.CuotasMeses,
            ValorCuota: venta.ValorCuota,
            ReferenciaTransaccion: venta.ReferenciaTransaccion,
            FechaVenta: venta.FechaVenta,
            ImpresionAutomatica: configSucursal?.ImpresionAutomaticaTicket ?? false,
            MostrarBotonCliente: configSucursal?.MostrarBotonCliente ?? false,
            Detalles: venta.Detalles.Select(d => new DetalleVentaDto(
                Id: d.Id,
                ProductoId: d.ProductoId,
                ProductoNombre: d.Producto.Nombre,
                Cantidad: d.Cantidad,
                PrecioRealCobrado: d.PrecioRealCobrado,
                Subtotal: d.Cantidad * d.PrecioRealCobrado
            )).ToList());

        return Ok(response);
    }

    private int GetComercioId() =>
        _tenantContext.ComercioId
            ?? throw new UnauthorizedAccessException("ComercioId not available in tenant context.");

    private int GetUsuarioId() =>
        int.Parse(User.FindFirstValue("sub")!);
}
