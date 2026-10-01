using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SaasPOS.Application.DTOs;
using SaasPOS.Application.DTOs.Results;
using SaasPOS.Application.Helpers;
using SaasPOS.Application.Interfaces;
using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Infrastructure.Services;

public class AuthService : IAuthService
{
    private const string GenericError = "Credenciales inválidas";
    private const int BcryptWorkFactor = 12;

    // Ventana de vigencia de una solicitud/contraseña temporal de recuperación: 24 horas (Req 6.1)
    private const int RecoveryExpirationHours = 24;

    private readonly AppDbContext _dbContext;
    private readonly JwtSettings _jwtSettings;
    private readonly IJtiBlocklist _jtiBlocklist;
    // Cifrado AES-256 de la contraseña temporal para su visualización posterior (Req 4.5)
    private readonly IPasswordEncryptionService _passwordEncryption;
    // Auditoría de las acciones de recuperación (aprobación/rechazo) (Req 15.1)
    private readonly IAuditService _auditService;

    public AuthService(
        AppDbContext dbContext,
        IOptions<JwtSettings> jwtSettings,
        IJtiBlocklist jtiBlocklist,
        IPasswordEncryptionService passwordEncryption,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _jwtSettings = jwtSettings.Value;
        _jtiBlocklist = jtiBlocklist;
        _passwordEncryption = passwordEncryption;
        _auditService = auditService;
    }

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        // Bypass tenant query filter for login — user could belong to any comercio
        var user = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        // Same generic message for: email not found, wrong password
        if (user is null)
            return AuthResult.Fail(GenericError);

        // Usuario desactivado: retornar error genérico para evitar enumeración de cuentas (Req 23.x)
        if (!user.Activo)
            return AuthResult.Fail(GenericError);

        // Rechazo con error genérico (401) para credenciales inválidas (email inexistente / contraseña incorrecta).
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return AuthResult.Fail(GenericError);

        // Req 6.4: un login con una contraseña temporal expirada NO debe conceder acceso. El PasswordHash
        // no se rota al expirar (la limpieza solo borra el cifrado de visualización), por lo que el hash de
        // la temporal podría seguir coincidiendo tras 24h. Por eso, cuando el usuario está obligado a cambiar
        // la contraseña (DebeCambiarPassword) por una recuperación, se aplica validación perezosa sobre la
        // solicitud Aprobada asociada: si su temporal ya venció (FechaExpiracion <= UtcNow), se rechaza el
        // login con el mismo error genérico (401) que cualquier credencial inválida.
        if (user.DebeCambiarPassword && await TieneTemporalExpiradaAsync(user.Id))
            return AuthResult.Fail(GenericError);

        // Se propaga la Bandera_Cambio para que el JWT incluya el claim must_change_password cuando aplique (Req 8.1, 8.2, 8.3).
        var token = GenerateJwt(user.Id, user.ComercioId, user.Rol, user.SucursalId, user.DebeCambiarPassword);
        return AuthResult.Ok(token);
    }

    public Task<bool> InvalidateSessionAsync(int userId)
    {
        _jtiBlocklist.BlockAllForUser(userId);
        return Task.FromResult(true);
    }

    public Task<bool> InvalidateAllSessionsForComercioAsync(int comercioId)
    {
        _jtiBlocklist.BlockAllForComercio(comercioId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Solicitud silenciosa de recuperación de contraseña (Requirements 1, 2).
    /// Siempre devuelve true, sin revelar la existencia del correo:
    /// - Si el correo no corresponde a ningún usuario, no crea nada (Req 1.3).
    /// - Si el usuario existe y no tiene una Solicitud_Activa, crea una
    ///   SolicitudRecuperacion Pendiente con UsuarioId, ComercioId y FechaSolicitud (Req 1.2, 1.5).
    /// - Nunca modifica el PasswordHash del usuario (Req 1.4).
    /// El bloqueo de duplicados con validación perezosa de 24h se refina en la tarea 9.2.
    /// </summary>
    public async Task<bool> RequestPasswordRecoveryAsync(string email)
    {
        // Se ignora el filtro de tenant: el usuario puede pertenecer a cualquier comercio (mismo patrón que LoginAsync)
        var user = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        // Correo inexistente: no crear nada y no revelar la ausencia del usuario (Req 1.3)
        if (user is null)
            return true;

        // Si el usuario ya tiene una Solicitud_Activa (Pendiente no vencida a 24h), no se duplica la solicitud (Req 2.1, 2.2).
        // Si la previa venció o se resolvió, se permite crear una nueva (Req 2.3).
        if (await HasActiveRecoveryRequestAsync(user.Id))
            return true;

        // Crear la solicitud silenciosa en estado Pendiente (Req 1.2, 1.5).
        // No se toca el PasswordHash del usuario en ningún momento (Req 1.4).
        var solicitud = new SolicitudRecuperacion
        {
            UsuarioId = user.Id,
            ComercioId = user.ComercioId,
            Estado = "Pendiente",
            FechaSolicitud = DateTime.UtcNow,
        };

        _dbContext.SolicitudesRecuperacion.Add(solicitud);
        await _dbContext.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Determina si un usuario tiene una Solicitud_Activa que impida crear una nueva (Requirements 2.1, 2.2, 2.3, 6.1).
    /// Se aplica validación perezosa (Req 7.3, 7.4): una solicitud solo se considera activa si está
    /// en estado Pendiente y NO ha vencido su plazo de 24 horas desde la FechaSolicitud.
    /// Una solicitud Pendiente con más de 24h se trata como Expirada (Req 6.1) y por tanto NO bloquea
    /// la creación de una nueva; las solicitudes ya resueltas (Aprobada/Rechazada) tampoco bloquean (Req 2.3).
    /// El cálculo del corte se hace dentro de la consulta LINQ para que se traduzca a SQL.
    /// </summary>
    private Task<bool> HasActiveRecoveryRequestAsync(int usuarioId)
    {
        // Corte de vigencia: una Pendiente se considera activa solo si FechaSolicitud es posterior a este instante
        var cutoff = DateTime.UtcNow.AddHours(-RecoveryExpirationHours);

        return _dbContext.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .AnyAsync(s => s.UsuarioId == usuarioId
                && s.Estado == "Pendiente"
                && s.FechaSolicitud > cutoff);
    }

    /// <summary>
    /// Determina, mediante validación perezosa, si el usuario tiene una contraseña temporal de
    /// recuperación ya expirada (Req 6.4, 7.3, 7.4). Se considera expirada cuando existe una solicitud
    /// Aprobada cuya FechaExpiracion es anterior o igual al instante actual. En ese caso el login con la
    /// temporal no debe conceder acceso, aunque su PasswordHash aún coincida (el hash no se rota al expirar).
    /// Se ignora el filtro de tenant porque el usuario puede pertenecer a cualquier comercio (igual que el login).
    /// </summary>
    private Task<bool> TieneTemporalExpiradaAsync(int usuarioId)
    {
        var ahora = DateTime.UtcNow;

        return _dbContext.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .AnyAsync(s => s.UsuarioId == usuarioId
                && s.Estado == "Aprobada"
                && s.FechaExpiracion != null
                && s.FechaExpiracion <= ahora);
    }

    /// <summary>
    /// Aprueba una solicitud de recuperación de contraseña (Requirements 3, 4, 15).
    ///
    /// Flujo (diseño, sección (b) "Aprobación + temporal + invalidación de sesiones"):
    /// 10.1 Autorización y contexto:
    ///   - Carga la solicitud Pendiente + solicitante y el aprobador (ignorando el filtro de tenant,
    ///     porque pueden pertenecer a distintos comercios en el caso cross-tenant SuperAdmin → Dueño).
    ///   - Validación perezosa: si la Pendiente venció (24h desde FechaSolicitud) se trata como
    ///     Expirada y se devuelve Conflict (Req 6.1, 7.3, 7.4).
    ///   - Aplica RoleHierarchy.CanApprove, prohíbe la auto-aprobación (approver != solicitante),
    ///     exige mismo ComercioId en el contexto POS (Dueño/Gerente) y solo permite que el SuperAdmin
    ///     apruebe a un Dueño cross-tenant (Req 3.1–3.5, 3.7, 3.8). Caso no autorizado → Forbidden.
    /// 10.2 Generación y aplicación de la temporal:
    ///   - Genera una temporal conforme de 12 caracteres, aplica el hash BCrypt (WF ≥ 12) al solicitante,
    ///     activa DebeCambiarPassword, cifra la temporal (AES-256) y marca la solicitud como Aprobada
    ///     con AprobadoPor, FechaResolucion y FechaExpiracion = UtcNow + 24h (Req 4.3–4.6).
    /// 10.3 Invalidación de sesiones y auditoría:
    ///   - Invalida todas las sesiones activas del solicitante vía el JTI blocklist (Req 4.7),
    ///     audita la aprobación (aprobador, solicitante, timestamp) (Req 15.1) y devuelve la temporal
    ///     en texto plano para que el aprobador la comunique en persona.
    /// </summary>
    public async Task<ApprovalResult> ApprovePasswordRecoveryAsync(int requestId, int approverUserId)
    {
        // ── 10.1 Cargar solicitud + solicitante y validar autorización/contexto ──

        // La entidad SolicitudRecuperacion no tiene filtro de tenant; el solicitante sí lo tiene,
        // por eso se ignoran los filtros para poder resolverlo aunque pertenezca a otro comercio
        // (caso SuperAdmin aprobando a un Dueño cross-tenant).
        var solicitud = await _dbContext.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .Include(s => s.Usuario)
            .FirstOrDefaultAsync(s => s.Id == requestId);

        // Solicitud inexistente → 404 (Req 3.4 sin modificar nada).
        if (solicitud is null || solicitud.Usuario is null)
            return ApprovalResult.Fail(RecoveryErrorCode.NotFound, "La solicitud de recuperación no existe.");

        // El aprobador debe existir; se ignora el filtro de tenant por el mismo motivo cross-tenant.
        var aprobador = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == approverUserId);

        if (aprobador is null)
            return ApprovalResult.Fail(RecoveryErrorCode.Forbidden, "El aprobador no es válido.");

        // Solo se puede aprobar una solicitud que siga Pendiente; si ya fue Aprobada o Rechazada
        // se considera resuelta → 409 (Req 3.4 sin modificar la solicitud).
        if (!string.Equals(solicitud.Estado, "Pendiente", StringComparison.Ordinal))
            return ApprovalResult.Fail(RecoveryErrorCode.Conflict, "La solicitud ya fue resuelta.");

        // Validación perezosa de expiración: una Pendiente con más de 24h se trata como Expirada (Req 6.1, 7.3, 7.4) → 409.
        var cutoff = DateTime.UtcNow.AddHours(-RecoveryExpirationHours);
        if (solicitud.FechaSolicitud <= cutoff)
            return ApprovalResult.Fail(RecoveryErrorCode.Conflict, "La solicitud ha expirado.");

        var solicitante = solicitud.Usuario;

        // Prohibición de auto-aprobación: nadie puede aprobar su propia solicitud (Req 3.5) → 403.
        if (approverUserId == solicitante.Id)
            return ApprovalResult.Fail(RecoveryErrorCode.Forbidden, "No puede aprobar su propia solicitud.");

        // Jerarquía estricta de roles: el aprobador debe tener un rango superior al del solicitante (Req 3.1, 3.2, 3.4).
        if (!RoleHierarchy.CanApprove(aprobador.Rol, solicitante.Rol))
            return ApprovalResult.Fail(RecoveryErrorCode.Forbidden, "No tiene la jerarquía necesaria para aprobar esta solicitud.");

        // Restricciones de contexto/tenant que dependen de datos (Req 3.3, 3.7, 3.8):
        // - SuperAdmin: solo puede aprobar solicitudes de usuarios con rol Dueño (cross-tenant permitido).
        // - Resto de aprobadores (Dueño/Gerente en el POS): deben pertenecer al mismo ComercioId del solicitante.
        if (string.Equals(aprobador.Rol, "SuperAdmin", StringComparison.Ordinal))
        {
            if (!string.Equals(solicitante.Rol, "Dueño", StringComparison.Ordinal))
                return ApprovalResult.Fail(RecoveryErrorCode.Forbidden, "El SuperAdmin solo puede aprobar solicitudes de Dueños.");
        }
        else if (aprobador.ComercioId != solicitante.ComercioId)
        {
            return ApprovalResult.Fail(RecoveryErrorCode.Forbidden, "No puede aprobar solicitudes de otro comercio.");
        }

        // ── 10.2 Generar y aplicar la contraseña temporal ──

        // Temporal de 12 caracteres que siempre cumple la Politica_Password estricta (Req 4.1, 4.2).
        var tempPassword = PasswordPolicy.GenerateCompliantTemporary(12);

        // Aplicar el hash BCrypt (WF ≥ 12) al solicitante y activar la bandera de cambio obligatorio (Req 4.3, 4.4).
        solicitante.PasswordHash = HashPassword(tempPassword);
        solicitante.DebeCambiarPassword = true;

        // Cifrar la temporal con AES-256 para poder reconsultarla dentro de las 24h (Req 4.5).
        solicitud.PasswordTemporalCifrada = _passwordEncryption.Encrypt(tempPassword);

        // Transicionar la solicitud a Aprobada con sus marcas de resolución y expiración (Req 4.6).
        var ahora = DateTime.UtcNow;
        var fechaExpiracion = ahora.AddHours(RecoveryExpirationHours);
        solicitud.Estado = "Aprobada";
        solicitud.AprobadoPor = approverUserId;
        solicitud.FechaResolucion = ahora;
        solicitud.FechaExpiracion = fechaExpiracion;

        await _dbContext.SaveChangesAsync();

        // ── 10.3 Invalidar sesiones y auditar ──

        // Invalidar de inmediato todas las sesiones activas del solicitante vía el JTI blocklist (Req 4.7).
        await InvalidateSessionAsync(solicitante.Id);

        // Registrar el evento de auditoría de la aprobación con aprobador, solicitante y timestamp (Req 15.1).
        // Se usa el ComercioId del solicitante para asociar el log al tenant correcto.
        await _auditService.RegistrarAsync(
            comercioId: solicitante.ComercioId,
            usuarioId: approverUserId,
            accion: "AprobarRecuperacionPassword",
            tablaAfectada: "SolicitudesRecuperacion",
            registroId: solicitud.Id.ToString(),
            valoresAnteriores: null,
            valoresNuevos: new
            {
                solicitud.Id,
                SolicitanteId = solicitante.Id,
                AprobadorId = approverUserId,
                FechaResolucion = ahora,
                FechaExpiracion = fechaExpiracion,
            });

        // Devolver la temporal en texto plano (solo al aprobador autorizado) junto con su expiración.
        return ApprovalResult.Ok(tempPassword, fechaExpiracion);
    }

    /// <summary>
    /// Rechaza una solicitud de recuperación con un motivo obligatorio (Requirement 15).
    ///
    /// Flujo (diseño, sección (e) "Rechazo con motivo"):
    ///   - Si el MotivoRechazo está vacío o es solo espacios → MissingReason (400) sin modificar
    ///     la solicitud (Req 15.2).
    ///   - Valida que la solicitud exista y siga Pendiente (una ya resuelta → 409/404).
    ///   - Aplica la misma matriz de autorización que la aprobación: jerarquía estricta
    ///     (RoleHierarchy.CanApprove), sin auto-rechazo, mismo ComercioId en el POS y SuperAdmin
    ///     únicamente sobre Dueños cross-tenant (Req 3.x). Caso no autorizado → Forbidden (403).
    ///   - Si el motivo está presente y el aprobador está autorizado → transición a Rechazada con
    ///     MotivoRechazo, AprobadoPor y FechaResolucion (Req 15.3, 15.5), y auditoría del rechazo
    ///     con aprobador, solicitante, motivo y timestamp (Req 15.4).
    /// </summary>
    public async Task<RejectResult> RejectPasswordRecoveryAsync(int requestId, int approverUserId, string motivoRechazo)
    {
        // El motivo de rechazo es obligatorio: vacío o solo espacios → 400 sin tocar la solicitud (Req 15.2).
        if (string.IsNullOrWhiteSpace(motivoRechazo))
            return RejectResult.Fail(RecoveryErrorCode.MissingReason, "El motivo de rechazo es obligatorio.");

        // La entidad SolicitudRecuperacion no tiene filtro de tenant; el solicitante sí, por eso se ignoran
        // los filtros para resolverlo aunque pertenezca a otro comercio (caso SuperAdmin → Dueño cross-tenant).
        var solicitud = await _dbContext.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .Include(s => s.Usuario)
            .FirstOrDefaultAsync(s => s.Id == requestId);

        // Solicitud inexistente → 404 sin modificar nada.
        if (solicitud is null || solicitud.Usuario is null)
            return RejectResult.Fail(RecoveryErrorCode.NotFound, "La solicitud de recuperación no existe.");

        // El aprobador debe existir; se ignora el filtro de tenant por el mismo motivo cross-tenant.
        var aprobador = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == approverUserId);

        if (aprobador is null)
            return RejectResult.Fail(RecoveryErrorCode.Forbidden, "El aprobador no es válido.");

        // Solo se puede rechazar una solicitud que siga Pendiente; una ya Aprobada o Rechazada
        // se considera resuelta → 409 sin modificar la solicitud.
        if (!string.Equals(solicitud.Estado, "Pendiente", StringComparison.Ordinal))
            return RejectResult.Fail(RecoveryErrorCode.Conflict, "La solicitud ya fue resuelta.");

        var solicitante = solicitud.Usuario;

        // La autorización de rechazo replica la de aprobación (misma matriz jerárquica y de contexto).
        // Un aprobador que no puede aprobar tampoco puede rechazar (Req 3.x) → 403.
        if (!IsAuthorizedApprover(aprobador, solicitante))
            return RejectResult.Fail(RecoveryErrorCode.Forbidden, "No tiene autorización para rechazar esta solicitud.");

        // Transición a Rechazada con el motivo y las marcas de resolución (Req 15.3, 15.5).
        var ahora = DateTime.UtcNow;
        solicitud.Estado = "Rechazada";
        solicitud.MotivoRechazo = motivoRechazo;
        solicitud.AprobadoPor = approverUserId;
        solicitud.FechaResolucion = ahora;

        await _dbContext.SaveChangesAsync();

        // Registrar el evento de auditoría del rechazo con aprobador, solicitante, motivo y timestamp (Req 15.4).
        // Se usa el ComercioId del solicitante para asociar el log al tenant correcto.
        await _auditService.RegistrarAsync(
            comercioId: solicitante.ComercioId,
            usuarioId: approverUserId,
            accion: "RechazarRecuperacionPassword",
            tablaAfectada: "SolicitudesRecuperacion",
            registroId: solicitud.Id.ToString(),
            valoresAnteriores: null,
            valoresNuevos: new
            {
                solicitud.Id,
                SolicitanteId = solicitante.Id,
                AprobadorId = approverUserId,
                MotivoRechazo = motivoRechazo,
                FechaResolucion = ahora,
            });

        return RejectResult.Ok();
    }

    /// <summary>
    /// Determina si un aprobador está autorizado para resolver (aprobar o rechazar) la solicitud de
    /// un solicitante, aplicando la matriz de autorización completa del diseño (Req 3.1–3.8):
    ///   - No se permite la auto-resolución (aprobador != solicitante).
    ///   - Jerarquía estricta de roles (RoleHierarchy.CanApprove).
    ///   - SuperAdmin: solo puede resolver solicitudes de usuarios con rol Dueño (cross-tenant permitido).
    ///   - Resto de aprobadores (Dueño/Gerente en el POS): mismo ComercioId que el solicitante.
    /// Centraliza la regla para reutilizarla en la aprobación, el rechazo y la reconsulta de la temporal.
    /// </summary>
    private static bool IsAuthorizedApprover(Usuario aprobador, Usuario solicitante)
    {
        // Prohibición de auto-resolución: nadie resuelve su propia solicitud (Req 3.5).
        if (aprobador.Id == solicitante.Id)
            return false;

        // Jerarquía estricta: el aprobador debe tener un rango superior al del solicitante (Req 3.1, 3.2, 3.4).
        if (!RoleHierarchy.CanApprove(aprobador.Rol, solicitante.Rol))
            return false;

        // Restricciones de contexto/tenant que dependen de datos (Req 3.3, 3.7, 3.8).
        if (string.Equals(aprobador.Rol, "SuperAdmin", StringComparison.Ordinal))
        {
            // El SuperAdmin solo puede resolver solicitudes de Dueños (cross-tenant).
            return string.Equals(solicitante.Rol, "Dueño", StringComparison.Ordinal);
        }

        // Aprobadores del contexto POS: deben pertenecer al mismo comercio del solicitante.
        return aprobador.ComercioId == solicitante.ComercioId;
    }

    /// <summary>
    /// Lista las solicitudes que el aprobador puede gestionar en su bandeja (Requirements 3.6, 3.7, 3.8, 5.3).
    ///
    /// Reglas (diseño, sección "GetPendingRecoveriesAsync"):
    ///   - Solo se incluyen solicitudes de usuarios de rol estrictamente inferior (RoleHierarchy.CanApprove).
    ///   - Contexto POS (Dueño/Gerente): únicamente solicitudes del mismo ComercioId del aprobador (Req 3.7).
    ///   - Contexto SuperAdmin: únicamente solicitudes de usuarios con rol Dueño de cualquier comercio,
    ///     enriquecidas con ComercioId/ComercioNombre para el modal Admin (Req 3.8, 13.2).
    ///   - Estados visibles: Pendiente no vencida (&lt; 24h desde FechaSolicitud) o Aprobada con la
    ///     contraseña temporal aún vigente (FechaExpiracion &gt; UtcNow).
    ///   - Se calcula MinutosRestantes y TienePasswordTemporalVigente con validación perezosa en memoria (Req 7.3, 7.4).
    /// </summary>
    public async Task<IReadOnlyList<PendingRecoveryDto>> GetPendingRecoveriesAsync(int approverUserId)
    {
        // Resolver el aprobador ignorando el filtro de tenant (puede ser SuperAdmin sin comercio propio relevante).
        var aprobador = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == approverUserId);

        // Aprobador inexistente: no hay nada que mostrar.
        if (aprobador is null)
            return Array.Empty<PendingRecoveryDto>();

        var ahora = DateTime.UtcNow;
        var cutoff = ahora.AddHours(-RecoveryExpirationHours);
        var esSuperAdmin = string.Equals(aprobador.Rol, "SuperAdmin", StringComparison.Ordinal);

        // Consulta base: solicitudes en estados relevantes (Pendiente no vencida o Aprobada con temporal vigente).
        // Se incluye el solicitante (y su comercio en el caso Admin) para poder proyectar los datos de la tarjeta.
        var query = _dbContext.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .Include(s => s.Usuario)
                .ThenInclude(u => u.Comercio)
            .Where(s =>
                (s.Estado == "Pendiente" && s.FechaSolicitud > cutoff)
                || (s.Estado == "Aprobada" && s.FechaExpiracion != null && s.FechaExpiracion > ahora));

        if (esSuperAdmin)
        {
            // SuperAdmin: solo solicitudes de Dueños, cross-tenant (Req 3.8).
            query = query.Where(s => s.Usuario.Rol == "Dueño");
        }
        else
        {
            // Contexto POS: solo solicitudes del mismo comercio del aprobador (Req 3.7).
            query = query.Where(s => s.ComercioId == aprobador.ComercioId);
        }

        var solicitudes = await query.ToListAsync();

        var resultado = new List<PendingRecoveryDto>();
        foreach (var solicitud in solicitudes)
        {
            var solicitante = solicitud.Usuario;

            // Filtro de jerarquía estricta: solo roles estrictamente inferiores al del aprobador (Req 3.6).
            // Para el SuperAdmin ya se restringió a Dueños; CanApprove(SuperAdmin, Dueño) también se cumple.
            if (!RoleHierarchy.CanApprove(aprobador.Rol, solicitante.Rol))
                continue;

            // Validación perezosa: una temporal vigente exige estado Aprobada, cifrado presente y no vencida (Req 7.3, 7.4).
            var tempVigente = string.Equals(solicitud.Estado, "Aprobada", StringComparison.Ordinal)
                && solicitud.PasswordTemporalCifrada != null
                && solicitud.FechaExpiracion.HasValue
                && solicitud.FechaExpiracion.Value > ahora;

            // Minutos restantes según el estado:
            // - Aprobada: hasta la expiración de la temporal (FechaExpiracion).
            // - Pendiente: hasta cumplir 24h desde la FechaSolicitud.
            int minutosRestantes;
            if (string.Equals(solicitud.Estado, "Aprobada", StringComparison.Ordinal) && solicitud.FechaExpiracion.HasValue)
            {
                minutosRestantes = CalcularMinutosRestantes(solicitud.FechaExpiracion.Value, ahora);
            }
            else
            {
                minutosRestantes = CalcularMinutosRestantes(solicitud.FechaSolicitud.AddHours(RecoveryExpirationHours), ahora);
            }

            var dto = esSuperAdmin
                ? new PendingRecoveryAdminDto
                {
                    ComercioId = solicitante.ComercioId,
                    // La entidad Comercio usa RazonSocial como nombre comercial mostrado.
                    ComercioNombre = solicitante.Comercio?.RazonSocial ?? string.Empty,
                }
                : new PendingRecoveryDto();

            dto.RequestId = solicitud.Id;
            dto.UsuarioId = solicitante.Id;
            dto.Nombre = solicitante.Nombre;
            dto.Email = solicitante.Email;
            dto.Rol = solicitante.Rol;
            dto.Estado = solicitud.Estado;
            dto.FechaSolicitud = solicitud.FechaSolicitud;
            dto.FechaExpiracion = solicitud.FechaExpiracion;
            dto.MinutosRestantes = minutosRestantes;
            dto.TienePasswordTemporalVigente = tempVigente;

            resultado.Add(dto);
        }

        return resultado;
    }

    /// <summary>
    /// Calcula los minutos restantes hasta un instante de expiración, redondeando hacia arriba y
    /// nunca por debajo de cero. Si el instante de expiración ya pasó, devuelve 0 (elemento vencido).
    /// </summary>
    private static int CalcularMinutosRestantes(DateTime fechaExpiracion, DateTime ahora)
    {
        if (fechaExpiracion <= ahora)
            return 0;

        // Redondeo hacia arriba para no mostrar 0 minutos cuando aún queda una fracción de minuto.
        var minutos = (int)Math.Ceiling((fechaExpiracion - ahora).TotalMinutes);
        return minutos < 0 ? 0 : minutos;
    }

    /// <summary>
    /// Reconsulta ilimitada de la contraseña temporal vigente de una solicitud Aprobada
    /// (Requirements 5.1, 5.2, 5.4, 6.2, 6.3, 7.3, 7.4).
    ///
    /// Flujo (diseño, sección "GetTempPasswordAsync"):
    ///   - Valida que la solicitud exista y esté Aprobada.
    ///   - Valida la autorización con la misma matriz que la aprobación/rechazo (jerarquía + comercio;
    ///     SuperAdmin solo sobre Dueños). No autorizado → Forbidden (403) (Req 5.3).
    ///   - Validación perezosa de expiración: si la temporal está vencida (FechaExpiracion &lt; UtcNow)
    ///     o su cifrado ya fue borrado, no se entrega descifrada → Gone (410); además, si estaba vencida
    ///     pero aún tenía cifrado, la validación perezosa borra el valor (PasswordTemporalCifrada = null)
    ///     para cerrar la ventana entre barridos del servicio de limpieza (Req 5.4, 6.2, 6.3, 7.3, 7.4).
    ///   - Si sigue vigente, descifra la temporal con IPasswordEncryptionService y la entrega, un número
    ///     ilimitado de veces dentro de las 24h, sin alterar el estado de la solicitud (Req 5.1, 5.2).
    /// </summary>
    public async Task<TempPasswordResult> GetTempPasswordAsync(int requestId, int approverUserId)
    {
        // La entidad SolicitudRecuperacion no tiene filtro de tenant; el solicitante sí, por eso se ignoran
        // los filtros para resolverlo aunque pertenezca a otro comercio (caso SuperAdmin → Dueño cross-tenant).
        var solicitud = await _dbContext.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .Include(s => s.Usuario)
            .FirstOrDefaultAsync(s => s.Id == requestId);

        // Solicitud inexistente → 404.
        if (solicitud is null || solicitud.Usuario is null)
            return TempPasswordResult.Fail(RecoveryErrorCode.NotFound, "La solicitud de recuperación no existe.");

        // El aprobador debe existir; se ignora el filtro de tenant por el mismo motivo cross-tenant.
        var aprobador = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == approverUserId);

        if (aprobador is null)
            return TempPasswordResult.Fail(RecoveryErrorCode.Forbidden, "El aprobador no es válido.");

        var solicitante = solicitud.Usuario;

        // Autorización: misma matriz jerárquica y de contexto que la aprobación/rechazo (Req 5.3) → 403.
        if (!IsAuthorizedApprover(aprobador, solicitante))
            return TempPasswordResult.Fail(RecoveryErrorCode.Forbidden, "No tiene autorización para consultar esta contraseña temporal.");

        // Solo una solicitud Aprobada puede tener una temporal que reconsultar.
        if (!string.Equals(solicitud.Estado, "Aprobada", StringComparison.Ordinal))
            return TempPasswordResult.Fail(RecoveryErrorCode.Gone, "La solicitud no tiene una contraseña temporal vigente.");

        var ahora = DateTime.UtcNow;

        // Validación perezosa de expiración: si venció y aún quedaba cifrado, se borra ahora mismo
        // para cerrar la ventana entre barridos del servicio de limpieza (Req 6.3, 7.3, 7.4).
        var expirada = !solicitud.FechaExpiracion.HasValue || solicitud.FechaExpiracion.Value <= ahora;
        if (expirada)
        {
            if (solicitud.PasswordTemporalCifrada != null)
            {
                solicitud.PasswordTemporalCifrada = null;
                await _dbContext.SaveChangesAsync();
            }

            // Una temporal expirada nunca se entrega descifrada → 410 (Req 5.4, 6.2).
            return TempPasswordResult.Fail(RecoveryErrorCode.Gone, "La contraseña temporal ha expirado.");
        }

        // Cifrado ya borrado (por limpieza previa) → no hay nada que entregar → 410.
        if (solicitud.PasswordTemporalCifrada == null)
            return TempPasswordResult.Fail(RecoveryErrorCode.Gone, "La contraseña temporal ya no está disponible.");

        // Descifrar y entregar la temporal vigente. Decrypt puede lanzar ante datos corruptos; en ese caso
        // se trata como no disponible (410) sin exponer detalles internos (ver Error Handling del diseño).
        try
        {
            var tempPassword = _passwordEncryption.Decrypt(solicitud.PasswordTemporalCifrada);
            return TempPasswordResult.Ok(tempPassword, solicitud.FechaExpiracion!.Value);
        }
        catch
        {
            return TempPasswordResult.Fail(RecoveryErrorCode.Gone, "La contraseña temporal no está disponible.");
        }
    }

    /// <summary>
    /// Cambio de contraseña obligatorio tras usar una temporal (Requirements 10.1, 10.2, 10.3, 10.4, 9.3).
    ///
    /// Flujo (diseño, sección (d) "Cambio de contraseña obligatorio"):
    ///   - Valida la nueva contraseña con PasswordPolicy (única fuente de verdad de la política).
    ///     Si es inválida → PolicyError (mapea a 400) con un mensaje descriptivo, sin modificar nada (Req 11, 9.3).
    ///   - Si es válida y el usuario existe:
    ///       · Aplica el hash BCrypt (WF ≥ 12) a la nueva contraseña sobre el PasswordHash del usuario (Req 10.1, 10.4).
    ///       · Desactiva la Bandera_Cambio (DebeCambiarPassword = false) (Req 10.2).
    ///       · Borra el valor cifrado de la contraseña temporal de la solicitud Aprobada asociada
    ///         (PasswordTemporalCifrada = null) para que ya no pueda reconsultarse (Req 10.3).
    ///   - El PasswordHash del usuario solo se modifica en este instante (Req 10.4).
    /// </summary>
    public async Task<ChangePasswordResult> ChangePasswordAsync(int userId, string newPassword)
    {
        // Validación de la Politica_Password estricta como única fuente de verdad (Req 11).
        // Si la contraseña incumple la política → PolicyError (400) con el primer mensaje descriptivo (Req 9.3).
        var validacion = PasswordPolicy.Validate(newPassword);
        if (!validacion.IsValid)
            return ChangePasswordResult.Fail(RecoveryErrorCode.PolicyError, validacion.FirstError);

        // Resolver el usuario ignorando el filtro de tenant: el cambio obligatorio se ejecuta con el
        // userId del claim y el usuario puede pertenecer a cualquier comercio (mismo patrón que LoginAsync).
        var usuario = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);

        // Usuario inexistente → 404 sin modificar nada.
        if (usuario is null)
            return ChangePasswordResult.Fail(RecoveryErrorCode.NotFound, "El usuario no existe.");

        // Aplicar el hash BCrypt (WF ≥ 12) a la nueva contraseña definitiva (Req 10.1, 10.4).
        usuario.PasswordHash = HashPassword(newPassword);

        // Desactivar la Bandera_Cambio: el usuario ya estableció su contraseña propia (Req 10.2).
        usuario.DebeCambiarPassword = false;

        // Borrar el cifrado de la contraseña temporal de la solicitud asociada (Req 10.3):
        // es la solicitud Aprobada del usuario cuya temporal cifrada aún no ha sido borrada.
        var solicitudAsociada = await _dbContext.SolicitudesRecuperacion
            .IgnoreQueryFilters()
            .Where(s => s.UsuarioId == userId
                && s.Estado == "Aprobada"
                && s.PasswordTemporalCifrada != null)
            .OrderByDescending(s => s.FechaResolucion)
            .FirstOrDefaultAsync();

        if (solicitudAsociada != null)
            solicitudAsociada.PasswordTemporalCifrada = null;

        await _dbContext.SaveChangesAsync();

        return ChangePasswordResult.Ok();
    }

    /// <summary>
    /// Cambio de contraseña voluntario desde Configuración (Requirement 17).
    /// Stub temporal: la lógica real llega en la tarea 12.
    /// </summary>
    public async Task<ChangePasswordResult> ChangePasswordVoluntaryAsync(int userId, string currentPassword, string newPassword)
    {
        // Resolver el usuario ignorando el filtro de tenant: el cambio voluntario se ejecuta con el
        // userId del claim y el usuario puede pertenecer a cualquier comercio (mismo patrón que ChangePasswordAsync).
        var usuario = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);

        // Usuario inexistente → 404 sin modificar nada.
        if (usuario is null)
            return ChangePasswordResult.Fail(RecoveryErrorCode.NotFound, "El usuario no existe.");

        // Verificar la contraseña actual (Req 17.2, 17.3). Si es incorrecta → InvalidCredentials (401)
        // SIN modificar el PasswordHash, para no permitir cambios sin conocer la contraseña vigente.
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, usuario.PasswordHash))
            return ChangePasswordResult.Fail(RecoveryErrorCode.InvalidCredentials, "La contraseña actual es incorrecta.");

        // Validar la nueva contraseña contra la Politica_Password estricta (Req 17.4, 17.5).
        // Si incumple la política → PolicyError (400) con el primer mensaje descriptivo, sin tocar el hash.
        var validacion = PasswordPolicy.Validate(newPassword);
        if (!validacion.IsValid)
            return ChangePasswordResult.Fail(RecoveryErrorCode.PolicyError, validacion.FirstError);

        // Aplicar el hash BCrypt (WF ≥ 12) a la nueva contraseña (Req 17.6).
        // NO se toca DebeCambiarPassword ni la contraseña temporal / SolicitudRecuperacion (Req 17.7):
        // el cambio voluntario es independiente del flujo de recuperación.
        usuario.PasswordHash = HashPassword(newPassword);

        await _dbContext.SaveChangesAsync();

        return ChangePasswordResult.Ok();
    }

    private string GenerateJwt(int userId, int comercioId, string role, int? sucursalId, bool mustChangePassword = false)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("comercio_id", comercioId.ToString()),
            new("role", role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        if (sucursalId.HasValue)
            claims.Add(new Claim("sucursal_id", sucursalId.Value.ToString()));

        // Claim_Cambio (must_change_password): se añade únicamente cuando la Bandera_Cambio está activa (Req 8.1).
        // Si la bandera está inactiva, el JWT se emite sin el claim (Req 8.2), evitando exponer un valor falso.
        if (mustChangePassword)
            claims.Add(new Claim("must_change_password", "true", ClaimValueTypes.Boolean));

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Utility to hash a password with BCrypt (work factor ≥ 12).
    /// Used when creating/updating users.
    /// </summary>
    public static string HashPassword(string plainPassword)
    {
        return BCrypt.Net.BCrypt.HashPassword(plainPassword, BcryptWorkFactor);
    }
}
