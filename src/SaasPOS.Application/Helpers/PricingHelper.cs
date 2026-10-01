namespace SaasPOS.Application.Helpers;

/// <summary>
/// Encapsulates the volume pricing selection logic for determining effective price
/// based on a product's PrecioLista, the sale quantity, and applicable volume rules.
/// </summary>
public static class PricingHelper
{
    /// <summary>
    /// Determines the effective price for a product given a quantity and available volume pricing rules.
    /// Returns PrecioLista if no applicable rule found, otherwise the PrecioEspecial of the 
    /// highest CantidadMinima rule that the quantity satisfies.
    /// </summary>
    /// <param name="precioLista">The product's list price (default price when no volume rule applies).</param>
    /// <param name="cantidad">The sale quantity.</param>
    /// <param name="reglasVolumen">Collection of volume pricing rules as (CantidadMinima, PrecioEspecial) tuples.</param>
    /// <returns>The effective price to charge.</returns>
    public static decimal DeterminarPrecioEfectivo(
        decimal precioLista,
        decimal cantidad,
        IEnumerable<(decimal CantidadMinima, decimal PrecioEspecial)> reglasVolumen)
    {
        var reglaAplicable = reglasVolumen
            .Where(r => cantidad >= r.CantidadMinima)
            .OrderByDescending(r => r.CantidadMinima)
            .FirstOrDefault();

        return reglaAplicable == default ? precioLista : reglaAplicable.PrecioEspecial;
    }
}
