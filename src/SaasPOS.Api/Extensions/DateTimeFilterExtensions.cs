using SaasPOS.Application.Interfaces;

namespace SaasPOS.Api.Extensions;

/// <summary>
/// Extensiones para normalizar fechas de filtro recibidas del cliente.
/// Cuando el cliente envia una fecha sin zona horaria (Kind == Unspecified),
/// se interpreta como hora local del usuario y se convierte a UTC antes de consultar la DB.
/// Esto corrige el problema de reportes personalizados donde el input type="date"
/// envia "2026-08-25" sin indicador de zona.
/// </summary>
public static class DateTimeFilterExtensions
{
    private const string ResolvedTimezoneKey = "ResolvedTimezone";

    /// <summary>
    /// Normaliza un DateTime de filtro "desde" a UTC.
    /// Si Kind == Utc, se retorna sin cambios. Si Kind == Unspecified,
    /// se interpreta como hora local del usuario y se convierte a UTC.
    /// </summary>
    public static DateTime? NormalizarFiltroFechaAUtc(
        this HttpContext context,
        DateTime? dateTime,
        ITimezoneService timezoneService)
    {
        if (!dateTime.HasValue)
            return null;

        var dt = dateTime.Value;

        if (dt.Kind == DateTimeKind.Utc)
            return dt;

        var timezone = GetResolvedTimezone(context);

        if (string.Equals(timezone, "UTC", StringComparison.OrdinalIgnoreCase))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        return timezoneService.ConvertToUtc(dt, timezone);
    }

    /// <summary>
    /// Normaliza un DateTime de filtro "hasta" a UTC, incluyendo el final del dia.
    /// Si la fecha solo tiene componente de fecha (hora = 00:00:00), se ajusta
    /// al final del dia (23:59:59.9999999) antes de convertir a UTC.
    /// </summary>
    public static DateTime? NormalizarFiltroFechaHastaAUtc(
        this HttpContext context,
        DateTime? dateTime,
        ITimezoneService timezoneService)
    {
        if (!dateTime.HasValue)
            return null;

        var dt = dateTime.Value;

        if (dt.Kind == DateTimeKind.Utc)
            return dt;

        // Si solo viene fecha sin hora, ajustar al final del dia
        if (dt.TimeOfDay == TimeSpan.Zero)
            dt = dt.Date.AddDays(1).AddTicks(-1);

        var timezone = GetResolvedTimezone(context);

        if (string.Equals(timezone, "UTC", StringComparison.OrdinalIgnoreCase))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        return timezoneService.ConvertToUtc(dt, timezone);
    }

    private static string GetResolvedTimezone(HttpContext context)
    {
        if (context.Items.TryGetValue(ResolvedTimezoneKey, out var value) && value is string tz)
            return tz;

        return "UTC";
    }
}