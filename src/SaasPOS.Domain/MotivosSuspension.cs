namespace SaasPOS.Domain;

/// <summary>
/// Motivos por los que un comercio puede estar suspendido.
/// Se guardan en <c>Comercio.MotivoSuspension</c> y determinan si un pago reactiva el comercio.
/// </summary>
public static class MotivosSuspension
{
    /// <summary>Suspensión automática por mora en la mensualidad. Un pago de mensualidad la revierte.</summary>
    public const string Mora = "Mora";

    /// <summary>Suspensión manual decidida por el SuperAdmin. Un pago no reactiva el comercio.</summary>
    public const string Administrativa = "Administrativa";

    /// <summary>Suspensión por vencimiento del periodo de prueba sin conversión a plan de pago.</summary>
    public const string TrialExpirado = "TrialExpirado";
}
