using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Api.Controllers.Tenants;

[ApiController]
[Route("api/tenants/usuarios")]
public class UsuariosController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ISubscriptionGuard _subscriptionGuard;
    private readonly IJtiBlocklist _jtiBlocklist;
    private readonly IAuditService _auditService;
    private readonly IRoleValidator _roleValidator;
    private readonly IMemoryCache _memoryCache;
    private readonly ITimezoneService _timezoneService;

    public UsuariosController(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ISubscriptionGuard subscriptionGuard,
        IJtiBlocklist jtiBlocklist,
        IAuditService auditService,
        IRoleValidator roleValidator,
        IMemoryCache memoryCache,
        ITimezoneService timezoneService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _subscriptionGuard = subscriptionGuard;
        _jtiBlocklist = jtiBlocklist;
        _auditService = auditService;
        _roleValidator = roleValidator;
        _memoryCache = memoryCache;
        _timezoneService = timezoneService;
    }

    /// <summary>
    /// Retorna los roles disponibles para el plan del comercio autenticado.
    /// Solo accesible para usuarios con rol Dueño o Gerente.
    /// </summary>
    [HttpGet("/api/tenants/roles-disponibles")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetRolesDisponibles()
    {
        // Verificar que el usuario tiene rol Dueño o Gerente
        var rolUsuario = User.FindFirstValue("role");
        if (string.IsNullOrEmpty(rolUsuario) ||
            (!string.Equals(rolUsuario, "Dueño", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(rolUsuario, "Gerente", StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(403, new { error = "No tiene permisos para consultar los roles disponibles", code = "ACCESS_DENIED" });
        }

        var comercioId = GetComercioId();

        try
        {
            var roles = await _roleValidator.GetRolesDisponiblesAsync(comercioId);
            return Ok(roles);
        }
        catch
        {
            return StatusCode(403, new { error = "No se pudo verificar el plan del comercio", code = "PLAN_NOT_FOUND" });
        }
    }

    /// <summary>
    /// Retorna información sobre el límite de usuarios del plan actual.
    /// Útil para que el frontend deshabilite el botón "Nuevo Usuario" cuando se alcanza el límite.
    /// </summary>
    [HttpGet("limite")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetLimiteUsuarios()
    {
        var comercioId = GetComercioId();

        var comercio = await _dbContext.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == comercioId);

        if (comercio?.Plan is null)
        {
            return StatusCode(500, new { error = "No se pudo determinar el plan del comercio." });
        }

        // Solo contar usuarios activos — los desactivados liberan cupo
        var currentCount = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .CountAsync(u => u.ComercioId == comercioId && u.Activo);

        // Plan Empresarial no tiene límite
        var limiteUsuarios = comercio.Plan.Nombre == "Empresarial" ? (int?)null : comercio.Plan.LimiteUsuarios;
        var puedeAgregar = limiteUsuarios is null || currentCount < limiteUsuarios;

        return Ok(new
        {
            limiteUsuarios,
            usuariosActuales = currentCount,
            puedeAgregar
        });
    }

    /// <summary>
    /// Lists all users for the authenticated commerce.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> GetAll()
    {
        var comercioId = GetComercioId();

        var usuarios = await _dbContext.Usuarios
            .Where(u => u.ComercioId == comercioId)
            .Select(u => new UsuarioDto(u.Id, u.Nombre, u.Email, u.Rol, u.SucursalId, u.Activo))
            .ToListAsync();

        return Ok(usuarios);
    }

    /// <summary>
    /// Creates a new user for the authenticated commerce.
    /// Validates role permissions, plan user limits,
    /// and performs secondary validation before INSERT.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "CanManageUsers")]
    public async Task<IActionResult> Create([FromBody] CreateUsuarioRequest request)
    {
        var comercioId = GetComercioId();

        // 1. Validar que el rol es reconocido por el sistema (Req 3.5)
        if (!RolePlanMapping.EsRolValido(request.Rol))
        {
            return BadRequest(new
            {
                error = $"El rol '{request.Rol}' no es válido. Roles válidos: Dueño, Gerente, Supervisor, Cajero, Bodeguero",
                code = "INVALID_ROLE"
            });
        }

        // 2. Validar que el rol es permitido por el plan del comercio (Req 3.1, 3.2)
        var validationResult = await _roleValidator.ValidarRolParaComercioAsync(comercioId, request.Rol);
        if (!validationResult.EsValido)
        {
            return StatusCode(403, new
            {
                error = validationResult.MensajeError,
                code = "ROLE_NOT_ALLOWED",
                rolesDisponibles = validationResult.RolesDisponibles
            });
        }

        // 3. Validar unicidad del Dueño (Req 2.6)
        if (string.Equals(request.Rol, "Dueño", StringComparison.OrdinalIgnoreCase))
        {
            var ownerExists = await _dbContext.Usuarios
                .AnyAsync(u => u.ComercioId == comercioId && u.Rol == "Dueño" && u.Activo);

            if (ownerExists)
            {
                return StatusCode(403, new
                {
                    error = "El Comercio ya cuenta con un Dueño asignado",
                    code = "OWNER_ALREADY_EXISTS"
                });
            }

            // Dueño siempre tiene SucursalId NULL (Req 2.5)
            request = request with { SucursalId = null };
        }

        // 4. Validate user limit
        if (!await _subscriptionGuard.CanAddUserAsync(comercioId, request.SucursalId))
        {
            return StatusCode(403, new { error = "Se ha alcanzado el límite de usuarios del plan.", code = "USER_LIMIT_EXCEEDED" });
        }

        // 5. Validate email uniqueness (global unique index on Email)
        var emailExists = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == request.Email);

        if (emailExists)
        {
            return Conflict(new { error = "El email ya está registrado.", code = "EMAIL_ALREADY_EXISTS" });
        }

        // 6. Secondary validation: re-count active users before INSERT to prevent race conditions
        var currentCount = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .CountAsync(u => u.ComercioId == comercioId && u.Activo);

        var comercio = await _dbContext.Comercios
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == comercioId);

        if (comercio?.Plan is null)
        {
            return StatusCode(403, new { error = "No se puede determinar el plan del comercio.", code = "PLAN_NOT_FOUND" });
        }

        // For Empresarial plan, no limit check needed
        if (comercio.Plan.Nombre != "Empresarial")
        {
            if (comercio.Plan.Nombre == "Básico")
            {
                if (currentCount >= comercio.Plan.LimiteUsuarios)
                {
                    return StatusCode(403, new { error = "Se ha alcanzado el límite de usuarios del plan.", code = "USER_LIMIT_EXCEEDED" });
                }
            }
            else if (comercio.Plan.Nombre == "Intermedio")
            {
                // For Intermedio, limit is per sucursal
                if (request.SucursalId is not null)
                {
                    var sucursalCount = await _dbContext.Usuarios
                        .IgnoreQueryFilters()
                        .CountAsync(u => u.ComercioId == comercioId && u.SucursalId == request.SucursalId && u.Activo);

                    if (sucursalCount >= comercio.Plan.LimiteUsuarios)
                    {
                        return StatusCode(403, new { error = "Se ha alcanzado el límite de usuarios del plan.", code = "USER_LIMIT_EXCEEDED" });
                    }
                }
                else
                {
                    // Intermedio requires sucursalId — fail-safe deny (excepto Dueño que ya se manejó arriba)
                    if (!string.Equals(request.Rol, "Dueño", StringComparison.OrdinalIgnoreCase))
                    {
                        return StatusCode(403, new { error = "Se requiere una sucursal para este plan.", code = "SUCURSAL_REQUIRED" });
                    }
                }
            }
        }

        // 7. Hash password with BCrypt (work factor >= 12)
        var passwordHash = AuthService.HashPassword(request.Password);

        // 8. Create user entity
        var usuario = new Usuario
        {
            ComercioId = comercioId,
            SucursalId = request.SucursalId,
            Nombre = request.Nombre,
            Email = request.Email,
            PasswordHash = passwordHash,
            Rol = request.Rol,
            Activo = true
        };

        _dbContext.Usuarios.Add(usuario);
        await _dbContext.SaveChangesAsync();

        // Audit log for user creation
        var currentUserId = GetCurrentUserId();
        await _auditService.RegistrarAsync(
            comercioId,
            currentUserId,
            "Crear",
            "Usuarios",
            usuario.Id.ToString(),
            null,
            new { usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol, usuario.SucursalId, usuario.Activo });

        var dto = new UsuarioDto(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol, usuario.SucursalId, usuario.Activo);
        return CreatedAtAction(nameof(GetAll), null, dto);
    }

    /// <summary>
    /// Updates an existing user (name, email, role, sucursalId).
    /// Verifies user belongs to authenticated commerce.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageUsers")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUsuarioRequest request)
    {
        var comercioId = GetComercioId();

        // Verify user belongs to this commerce
        var usuario = await _dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id && u.ComercioId == comercioId);

        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado.", code = "USER_NOT_FOUND" });
        }

        // Si el rol está cambiando, aplicar validaciones de rol
        if (!string.Equals(usuario.Rol, request.Rol, StringComparison.OrdinalIgnoreCase))
        {
            // Validar que el rol destino es reconocido (Req 3.5)
            if (!RolePlanMapping.EsRolValido(request.Rol))
            {
                return BadRequest(new
                {
                    error = $"El rol '{request.Rol}' no es válido. Roles válidos: Dueño, Gerente, Supervisor, Cajero, Bodeguero",
                    code = "INVALID_ROLE"
                });
            }

            // Validar que si el usuario es el único Dueño, no se puede cambiar su rol (Req 2.4)
            if (string.Equals(usuario.Rol, "Dueño", StringComparison.OrdinalIgnoreCase))
            {
                var esUnicoDueno = await _roleValidator.EsUnicoDuenoAsync(comercioId, id);
                if (esUnicoDueno)
                {
                    return StatusCode(403, new
                    {
                        error = "No se puede cambiar el rol del único Dueño del Comercio",
                        code = "CANNOT_CHANGE_OWNER_ROLE"
                    });
                }
            }

            // Validar que el rol destino es permitido por el plan (Req 3.3, 3.4)
            var validationResult = await _roleValidator.ValidarRolParaComercioAsync(comercioId, request.Rol);
            if (!validationResult.EsValido)
            {
                return StatusCode(403, new
                {
                    error = validationResult.MensajeError,
                    code = "ROLE_NOT_ALLOWED",
                    rolesDisponibles = validationResult.RolesDisponibles
                });
            }
        }

        // Si el nuevo rol es Dueño, forzar SucursalId = null (Req 2.5)
        if (string.Equals(request.Rol, "Dueño", StringComparison.OrdinalIgnoreCase))
        {
            request = request with { SucursalId = null };
        }

        // If email changed, validate uniqueness
        if (!string.Equals(usuario.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailExists = await _dbContext.Usuarios
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Email == request.Email && u.Id != id);

            if (emailExists)
            {
                return Conflict(new { error = "El email ya está registrado.", code = "EMAIL_ALREADY_EXISTS" });
            }
        }

        // Capture previous values for audit
        var valoresAnteriores = new { usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol, usuario.SucursalId };

        usuario.Nombre = request.Nombre;
        usuario.Email = request.Email;
        usuario.Rol = request.Rol;
        usuario.SucursalId = request.SucursalId;

        await _dbContext.SaveChangesAsync();

        // Audit log for user update
        var currentUserId = GetCurrentUserId();
        await _auditService.RegistrarAsync(
            comercioId,
            currentUserId,
            "Actualizar",
            "Usuarios",
            usuario.Id.ToString(),
            valoresAnteriores,
            new { usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol, usuario.SucursalId });

        var dto = new UsuarioDto(usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol, usuario.SucursalId, usuario.Activo);
        return Ok(dto);
    }

    /// <summary>
    /// Deactivates a user, invalidates all their active sessions via JTI blocklist,
    /// and registers the action in LogsAuditoria.
    /// Protege al único Dueño contra desactivación (Req 2.3).
    /// </summary>
    [HttpPatch("{id:int}/desactivar")]
    [Authorize(Policy = "CanManageUsers")]
    public async Task<IActionResult> Desactivar(int id)
    {
        var comercioId = GetComercioId();

        // Verify user belongs to this commerce
        var usuario = await _dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id && u.ComercioId == comercioId);

        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado.", code = "USER_NOT_FOUND" });
        }

        // Proteger al único Dueño contra desactivación (Req 2.3)
        if (string.Equals(usuario.Rol, "Dueño", StringComparison.OrdinalIgnoreCase))
        {
            var esUnicoDueno = await _roleValidator.EsUnicoDuenoAsync(comercioId, id);
            if (esUnicoDueno)
            {
                return StatusCode(403, new
                {
                    error = "No se puede eliminar al único Dueño del Comercio",
                    code = "CANNOT_REMOVE_OWNER"
                });
            }
        }

        // Set Activo = false
        usuario.Activo = false;

        // Invalidate all active sessions for this user
        _jtiBlocklist.BlockAllForUser(id);

        // Invalidar cache de estado activo para que el middleware lo rechace inmediatamente
        _memoryCache.Remove($"user_active_{id}");

        // Register in LogsAuditoria via IAuditService
        var currentUserId = GetCurrentUserId();
        await _dbContext.SaveChangesAsync();

        await _auditService.RegistrarAsync(
            comercioId,
            currentUserId,
            "Desactivar",
            "Usuarios",
            id.ToString(),
            new { Activo = true },
            new { Activo = false });

        return Ok(new { message = "Usuario desactivado exitosamente." });
    }

    /// <summary>
    /// Reactiva un usuario previamente desactivado.
    /// Valida que no se exceda el límite de usuarios del plan al reactivar.
    /// </summary>
    [HttpPatch("{id:int}/activar")]
    [Authorize(Policy = "CanManageUsers")]
    public async Task<IActionResult> Activar(int id)
    {
        var comercioId = GetComercioId();

        // Verificar que el usuario pertenece a este comercio
        var usuario = await _dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id && u.ComercioId == comercioId);

        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado.", code = "USER_NOT_FOUND" });
        }

        if (usuario.Activo)
        {
            return BadRequest(new { error = "El usuario ya está activo.", code = "USER_ALREADY_ACTIVE" });
        }

        // Validar límite de usuarios del plan antes de reactivar
        if (!await _subscriptionGuard.CanAddUserAsync(comercioId, usuario.SucursalId))
        {
            return StatusCode(403, new { error = "No se puede activar: se alcanzó el límite de usuarios del plan.", code = "USER_LIMIT_EXCEEDED" });
        }

        // Validar que el rol del usuario sigue siendo permitido por el plan actual
        var validationResult = await _roleValidator.ValidarRolParaComercioAsync(comercioId, usuario.Rol);
        if (!validationResult.EsValido)
        {
            return StatusCode(403, new
            {
                error = $"No se puede activar: el rol '{usuario.Rol}' ya no es permitido por el plan actual.",
                code = "ROLE_NOT_ALLOWED",
                rolesDisponibles = validationResult.RolesDisponibles
            });
        }

        // Activar usuario
        usuario.Activo = true;
        await _dbContext.SaveChangesAsync();

        // Registrar en auditoría
        var currentUserId = GetCurrentUserId();
        await _auditService.RegistrarAsync(
            comercioId,
            currentUserId,
            "Activar",
            "Usuarios",
            id.ToString(),
            new { Activo = false },
            new { Activo = true });

        return Ok(new { message = "Usuario activado exitosamente." });
    }

    /// <summary>
    /// Actualiza la zona horaria preferida de un usuario.
    /// Solo accesible para roles Dueño y Gerente.
    /// </summary>
    [HttpPatch("{id:int}/zona-horaria")]
    [Authorize(Policy = "TenantAccess")]
    public async Task<IActionResult> ActualizarZonaHoraria(int id, [FromBody] UpdateZonaHorariaRequest request)
    {
        // Verificar que el usuario tiene rol Dueño o Gerente
        var rolUsuario = User.FindFirstValue("role");
        if (string.IsNullOrEmpty(rolUsuario) ||
            (!string.Equals(rolUsuario, "Dueño", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(rolUsuario, "Gerente", StringComparison.OrdinalIgnoreCase)))
        {
            return StatusCode(403, new { error = "No tiene permisos para actualizar la zona horaria", code = "ACCESS_DENIED" });
        }

        // Validar que la zona horaria sea IANA reconocida
        if (string.IsNullOrWhiteSpace(request.ZonaHoraria) || !_timezoneService.IsValidTimeZone(request.ZonaHoraria))
        {
            return BadRequest(new { error = $"La zona horaria '{request.ZonaHoraria}' no es válida. Debe ser un identificador IANA reconocido.", code = "INVALID_TIMEZONE" });
        }

        var comercioId = GetComercioId();

        // Verificar que el usuario pertenece a este comercio
        var usuario = await _dbContext.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id && u.ComercioId == comercioId);

        if (usuario is null)
        {
            return NotFound(new { error = "Usuario no encontrado.", code = "USER_NOT_FOUND" });
        }

        // Capturar valor anterior para auditoría
        var valorAnterior = usuario.ZonaHoraria;

        // Actualizar zona horaria
        usuario.ZonaHoraria = request.ZonaHoraria;
        await _dbContext.SaveChangesAsync();

        // Registrar en auditoría
        var currentUserId = GetCurrentUserId();
        await _auditService.RegistrarAsync(
            comercioId,
            currentUserId,
            "Actualizar",
            "Usuarios",
            usuario.Id.ToString(),
            new { ZonaHoraria = valorAnterior },
            new { ZonaHoraria = usuario.ZonaHoraria });

        return Ok(new { usuario.Id, usuario.Nombre, usuario.Email, usuario.Rol, usuario.SucursalId, usuario.Activo, usuario.ZonaHoraria });
    }

    private int GetComercioId() =>
        _tenantContext.ComercioId
        ?? throw new UnauthorizedAccessException("ComercioId not available");

    private int GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.Parse(sub!);
    }
}
