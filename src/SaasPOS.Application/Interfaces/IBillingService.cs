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
    /// Calculates the proportional cuota based on the plan price and contract date.
    /// Returns full price when contracted on Dia_Corte (day 3).
    /// Formula: (precioPlan / diasTotalesMes) × diasRestantes, rounded to 2 decimals.
    /// </summary>
    decimal CalcularCuotaProporcional(decimal precioPlan, DateTime fechaContratacion);
}
