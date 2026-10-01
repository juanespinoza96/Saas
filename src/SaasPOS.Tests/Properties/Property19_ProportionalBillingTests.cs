using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Property-based tests for proportional billing formula correctness.
/// **Validates: Requirements 19.2**
///
/// Property 19: Proportional billing formula correctness.
/// For any valid plan price > 0 and any contract date:
/// 1. When contracted on day 3 → cuota == precioPlan (full charge)
/// 2. When contracted on any other day → cuota == Math.Round((precioPlan / daysInMonth) * daysUntilNextDay3, 2)
/// 3. Cuota is always > 0 and &lt;= precioPlan
/// </summary>
public class Property19_ProportionalBillingTests
{
    private readonly BillingService _service;

    public Property19_ProportionalBillingTests()
    {
        // CalcularCuotaProporcional is a pure method that doesn't use any dependencies,
        // so we provide mocked/dummy dependencies to satisfy the constructor.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PropBilling_{Guid.NewGuid()}")
            .Options;
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(t => t.ComercioId).Returns(1);
        var db = new AppDbContext(options, tenantContext.Object);

        var auditService = new Mock<IAuditService>();
        var notificationService = new Mock<INotificationService>();
        var emailService = new Mock<IEmailService>();
        var jtiBlocklist = new Mock<IJtiBlocklist>();
        var logger = new Mock<ILogger<BillingService>>();

        _service = new BillingService(
            db,
            auditService.Object,
            notificationService.Object,
            emailService.Object,
            jtiBlocklist.Object,
            logger.Object);
    }

    /// <summary>
    /// Generates a positive decimal plan price in a reasonable range (0.01 to 9999.99).
    /// </summary>
    private static Gen<decimal> GenPositivePrice() =>
        Gen.Choose(1, 999999)
           .Select(i => (decimal)i / 100m);

    /// <summary>
    /// Generates a valid year between 2020 and 2030.
    /// </summary>
    private static Gen<int> GenYear() =>
        Gen.Choose(2020, 2030);

    /// <summary>
    /// Generates a valid month (1 to 12).
    /// </summary>
    private static Gen<int> GenMonth() =>
        Gen.Choose(1, 12);

    /// <summary>
    /// Generates a valid day for a given year/month, excluding day 3.
    /// </summary>
    private static Gen<int> GenDayNotThree(int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        // Generate days 1-daysInMonth excluding 3
        return Gen.Choose(1, daysInMonth - 1)
                  .Select(d => d >= 3 ? d + 1 : d);
    }

    /// <summary>
    /// For any precioPlan > 0 and any year/month, contracting on day 3 always returns full price.
    /// **Validates: Requirements 19.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Day3_AlwaysReturnsFullPrice()
    {
        var gen =
            from precio in GenPositivePrice()
            from year in GenYear()
            from month in GenMonth()
            select (precio, year, month);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioPlan, year, month) = tuple;
            var fecha = new DateTime(year, month, 3);

            var result = _service.CalcularCuotaProporcional(precioPlan, fecha);

            return (result == precioPlan)
                .Label($"Expected full price {precioPlan} on day 3, got {result} (date={fecha:yyyy-MM-dd})");
        });
    }

    /// <summary>
    /// For any date NOT on day 3, the result equals the manual formula:
    /// Math.Round((precioPlan / daysInMonth) * daysUntilNextDay3, 2)
    /// **Validates: Requirements 19.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NonDay3_CalculatesCorrectProportional()
    {
        var gen =
            from precio in GenPositivePrice()
            from year in GenYear()
            from month in GenMonth()
            from day in GenDayNotThree(year, month)
            select (precio, year, month, day);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioPlan, year, month, day) = tuple;
            var fecha = new DateTime(year, month, day);

            // Manually compute expected result using the formula from Req 19.2
            DateTime proximoDia3;
            if (fecha.Day < 3)
            {
                proximoDia3 = new DateTime(fecha.Year, fecha.Month, 3);
            }
            else
            {
                var nextMonth = fecha.AddMonths(1);
                proximoDia3 = new DateTime(nextMonth.Year, nextMonth.Month, 3);
            }

            var diasTotalesMes = DateTime.DaysInMonth(fecha.Year, fecha.Month);
            var diasRestantes = (proximoDia3 - fecha).Days;
            var expected = Math.Round((precioPlan / diasTotalesMes) * diasRestantes, 2);

            var result = _service.CalcularCuotaProporcional(precioPlan, fecha);

            return (result == expected)
                .Label($"Expected {expected}, got {result} (price={precioPlan}, date={fecha:yyyy-MM-dd}, daysInMonth={diasTotalesMes}, daysRemaining={diasRestantes})");
        });
    }

    /// <summary>
    /// For any valid plan price > 0 and any valid date, the cuota is always > 0 and &lt;= precioPlan.
    /// **Validates: Requirements 19.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Result_AlwaysPositiveAndBounded()
    {
        var gen =
            from precio in GenPositivePrice()
            from year in GenYear()
            from month in GenMonth()
            from day in Gen.Choose(1, DateTime.DaysInMonth(year, month))
            select (precio, year, month, day);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (precioPlan, year, month, day) = tuple;
            var fecha = new DateTime(year, month, day);

            var result = _service.CalcularCuotaProporcional(precioPlan, fecha);

            return (result > 0m && result <= precioPlan)
                .Label($"Expected 0 < cuota <= {precioPlan}, got {result} (date={fecha:yyyy-MM-dd})");
        });
    }
}
