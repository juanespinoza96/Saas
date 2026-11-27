using SaasPOS.Application.DTOs;
using SaasPOS.Domain.Entities;

namespace SaasPOS.Application.Interfaces;

/// <summary>
/// Manages subscription billing: plan contracting, daily cut processing, and payment registration.
/// </summary>
public interface IBillingService
{
    /// <summary>
    /// Contracts a plan for a comercio, creating a Suscripcion with proportional or full cuota.
    /// </summary>
    Task<Suscripcion> ContratarPlanAsync(int comercioId, int planId, DateTime fechaContratacion);

    /// <summary>
    /// Processes daily billing cuts (state transitions, notifications, suspensions).
    /// </summary>
    Task ProcesarCortesDiariosAsync(DateTime fechaActual);

    /// <summary>
    /// Registers a payment for a comercio's active subscription.
    /// </summary>
    Task<bool> RegistrarPagoAsync(int comercioId, PagoDto pago);

    /// <summary>
    /// Calcula la cuota proporcional a partir de la mensualidad y la fecha de contratación.
    /// Devuelve la mensualidad completa si la fecha es el día de corte (día 3).
    /// Fórmula: (mensualidad / díasTotalesMes) × díasRestantes, redondeada a 2 decimales.
    /// </summary>
    decimal CalcularCuotaProporcional(decimal mensualidad, DateTime fechaContratacion);
}
