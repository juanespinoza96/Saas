namespace SaasPOS.Domain;

/// <summary>
/// Tipos de pago registrados en <c>PagoComercio.Tipo</c>.
/// </summary>
public static class TiposPago
{
    /// <summary>Pago recurrente de la mensualidad del plan.</summary>
    public const string Mensualidad = "Mensualidad";

    /// <summary>Pago único de la licencia del plan (máximo uno por comercio).</summary>
    public const string Licencia = "Licencia";
}
