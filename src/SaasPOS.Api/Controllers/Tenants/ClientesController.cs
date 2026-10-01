using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Api.Controllers.Tenants;

[ApiController]
[Route("api/tenants/clientes")]
public class ClientesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public ClientesController(AppDbContext db, ITenantContext tenantContext, IAuditService auditService)
    {
        _db = db;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    /// <summary>
    /// GET /api/tenants/clientes — List/search clients for the tenant (paged).
    /// Req 12.1: Client management CRUD.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? busqueda,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var comercioId = GetComercioId();

        var query = _db.Clientes
            .Where(c => c.ComercioId == comercioId);

        if (!string.IsNullOrWhiteSpace(busqueda))
        {
            var term = busqueda.Trim().ToLower();
            query = query.Where(c =>
                c.Identificacion.ToLower().Contains(term) ||
                c.Nombre.ToLower().Contains(term));
        }

        var total = await query.CountAsync();

        var clientes = await query
            .OrderBy(c => c.Nombre)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ClienteResponse(
                c.Id,
                c.ComercioId,
                c.Identificacion,
                c.Nombre,
                c.Correo,
                c.Direccion,
                c.Telefono,
                c.EsConsumidorFinal))
            .ToListAsync();

        return Ok(new ClienteListResponse(clientes, total, page, pageSize));
    }

    /// <summary>
    /// GET /api/tenants/clientes/{identificacion} — Search client by Identificacion.
    /// Req 12.4: When not found, return empty result (frontend shows create form).
    /// </summary>
    [HttpGet("{identificacion}")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetByIdentificacion(string identificacion)
    {
        var comercioId = GetComercioId();

        var cliente = await _db.Clientes
            .Where(c => c.ComercioId == comercioId && c.Identificacion == identificacion)
            .Select(c => new ClienteResponse(
                c.Id,
                c.ComercioId,
                c.Identificacion,
                c.Nombre,
                c.Correo,
                c.Direccion,
                c.Telefono,
                c.EsConsumidorFinal))
            .FirstOrDefaultAsync();

        if (cliente is null)
            return Ok(new { found = false, cliente = (ClienteResponse?)null });

        return Ok(new { found = true, cliente });
    }

    /// <summary>
    /// POST /api/tenants/clientes — Create a new client.
    /// Req 12.1: CRUD operations.
    /// Req 12.2: Uniqueness (ComercioId, Identificacion) enforced.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "CanSell")]
    public async Task<IActionResult> Create([FromBody] CreateClienteRequest request)
    {
        var comercioId = GetComercioId();

        // ── Validate required fields ─────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.Identificacion))
            return BadRequest(new { error = "La identificación es requerida.", code = "IDENTIFICACION_REQUIRED" });

        if (string.IsNullOrWhiteSpace(request.Nombre))
            return BadRequest(new { error = "El nombre es requerido.", code = "NOMBRE_REQUIRED" });

        // If NOT consumidor final, Correo/Direccion/Telefono are required
        if (!request.EsConsumidorFinal)
        {
            if (string.IsNullOrWhiteSpace(request.Correo))
                return BadRequest(new { error = "El correo es requerido para clientes que no son consumidor final.", code = "CORREO_REQUIRED" });

            if (string.IsNullOrWhiteSpace(request.Direccion))
                return BadRequest(new { error = "La dirección es requerida para clientes que no son consumidor final.", code = "DIRECCION_REQUIRED" });

            if (string.IsNullOrWhiteSpace(request.Telefono))
                return BadRequest(new { error = "El teléfono es requerido para clientes que no son consumidor final.", code = "TELEFONO_REQUIRED" });
        }

        // ── Check uniqueness (ComercioId, Identificacion) ────────────────────
        var exists = await _db.Clientes
            .AnyAsync(c => c.ComercioId == comercioId && c.Identificacion == request.Identificacion);

        if (exists)
            return Conflict(new { error = "Ya existe un cliente con esta identificación en el comercio.", code = "CLIENTE_DUPLICADO" });

        // ── Create entity ────────────────────────────────────────────────────
        var cliente = new Cliente
        {
            ComercioId = comercioId,
            Identificacion = request.Identificacion.Trim(),
            Nombre = request.Nombre.Trim().ToUpper(),
            Correo = request.Correo?.Trim().ToUpper(),
            Direccion = request.Direccion?.Trim(),
            Telefono = request.Telefono?.Trim(),
            EsConsumidorFinal = request.EsConsumidorFinal
        };

        _db.Clientes.Add(cliente);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Conflict(new { error = "Ya existe un cliente con esta identificación en el comercio.", code = "CLIENTE_DUPLICADO" });
        }

        // Req 15.1: Audit log for client creation
        var usuarioId = GetUsuarioId();
        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "Crear",
            "Clientes",
            cliente.Id.ToString(),
            null,
            new { cliente.Id, cliente.Identificacion, cliente.Nombre, cliente.Correo, cliente.Direccion, cliente.Telefono, cliente.EsConsumidorFinal });

        var response = new ClienteResponse(
            cliente.Id,
            cliente.ComercioId,
            cliente.Identificacion,
            cliente.Nombre,
            cliente.Correo,
            cliente.Direccion,
            cliente.Telefono,
            cliente.EsConsumidorFinal);

        return CreatedAtAction(nameof(GetByIdentificacion), new { identificacion = cliente.Identificacion }, response);
    }

    /// <summary>
    /// PUT /api/tenants/clientes/{id} — Update an existing client.
    /// Req 12.1: CRUD operations.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanSell")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateClienteRequest request)
    {
        var comercioId = GetComercioId();

        var cliente = await _db.Clientes
            .FirstOrDefaultAsync(c => c.Id == id && c.ComercioId == comercioId);

        if (cliente is null)
            return NotFound(new { error = "Cliente no encontrado.", code = "CLIENTE_NOT_FOUND" });

        // ── Validate required fields ─────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.Nombre))
            return BadRequest(new { error = "El nombre es requerido.", code = "NOMBRE_REQUIRED" });

        // If NOT consumidor final, Correo/Direccion/Telefono are required
        if (!request.EsConsumidorFinal)
        {
            if (string.IsNullOrWhiteSpace(request.Correo))
                return BadRequest(new { error = "El correo es requerido para clientes que no son consumidor final.", code = "CORREO_REQUIRED" });

            if (string.IsNullOrWhiteSpace(request.Direccion))
                return BadRequest(new { error = "La dirección es requerida para clientes que no son consumidor final.", code = "DIRECCION_REQUIRED" });

            if (string.IsNullOrWhiteSpace(request.Telefono))
                return BadRequest(new { error = "El teléfono es requerido para clientes que no son consumidor final.", code = "TELEFONO_REQUIRED" });
        }

        // ── Update fields ────────────────────────────────────────────────────
        // Capture previous values for audit
        var valoresAnteriores = new { cliente.Id, cliente.Nombre, cliente.Correo, cliente.Direccion, cliente.Telefono, cliente.EsConsumidorFinal };

        cliente.Nombre = request.Nombre.Trim().ToUpper();
        cliente.Correo = request.Correo?.Trim().ToUpper();
        cliente.Direccion = request.Direccion?.Trim();
        cliente.Telefono = request.Telefono?.Trim();
        cliente.EsConsumidorFinal = request.EsConsumidorFinal;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Conflict(new { error = "Ya existe un cliente con esta identificación en el comercio.", code = "CLIENTE_DUPLICADO" });
        }

        // Req 15.1: Audit log for client update
        var usuarioId = GetUsuarioId();
        await _auditService.RegistrarAsync(
            comercioId,
            usuarioId,
            "Actualizar",
            "Clientes",
            cliente.Id.ToString(),
            valoresAnteriores,
            new { cliente.Id, cliente.Nombre, cliente.Correo, cliente.Direccion, cliente.Telefono, cliente.EsConsumidorFinal });

        var response = new ClienteResponse(
            cliente.Id,
            cliente.ComercioId,
            cliente.Identificacion,
            cliente.Nombre,
            cliente.Correo,
            cliente.Direccion,
            cliente.Telefono,
            cliente.EsConsumidorFinal);

        return Ok(response);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int GetComercioId() =>
        _tenantContext.ComercioId
            ?? throw new UnauthorizedAccessException("ComercioId not available in tenant context.");

    private int GetUsuarioId() =>
        int.Parse(User.FindFirstValue("sub")!);

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // PostgreSQL unique violation error code: 23505
        var inner = ex.InnerException;
        return inner != null && inner.Message.Contains("23505");
    }
}
