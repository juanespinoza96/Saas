using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SaasPOS.Application.Interfaces;
using SaasPOS.Infrastructure.Data;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

/// <summary>
/// Property-based tests para ComprobantesConfigService.NormalizarTipoComprobante.
/// Feature: mejoras-operativas-v2, Property 13: Normalización de TipoComprobante mapea valores legacy correctamente
///
/// **Validates: Requirements 8.3**
///
/// "For any valor de TipoComprobante del conjunto {"Ticket Interno", "Ticket Digital", "Ticket Impreso",
/// "Factura Electronica"}, la función de normalización SHALL mapear "Ticket Interno" a "Ticket Digital"
/// y dejar todos los demás valores sin modificación."
/// </summary>
public class NormalizarTipoComprobantePropertyTests
{
    private static ComprobantesConfigService CreateService()
    {
        var tenantContextMock = new Mock<ITenantContext>();
        tenantContextMock.Setup(t => t.IsSuperAdmin).Returns(true);
        tenantContextMock.Setup(t => t.ComercioId).Returns((int?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, tenantContextMock.Object);
        var logger = new Mock<ILogger<ComprobantesConfigService>>();

        return new ComprobantesConfigService(db, logger.Object);
    }

    /// <summary>
    /// Property 13A: "Ticket Interno" siempre se mapea a "Ticket Digital".
    /// Generamos iteraciones para verificar que el mapeo es consistente.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TicketInterno_SiempreMapeaATicketDigital(byte _)
    {
        // Arrange
        var service = CreateService();

        // Act
        var resultado = service.NormalizarTipoComprobante("Ticket Interno");

        // Assert
        return resultado == "Ticket Digital";
    }

    /// <summary>
    /// Property 13B: "Factura Electronica" (sin tilde) siempre se mapea a "Factura Electrónica" (con tilde).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FacturaElectronica_SiempreMapeaConTilde(byte _)
    {
        // Arrange
        var service = CreateService();

        // Act
        var resultado = service.NormalizarTipoComprobante("Factura Electronica");

        // Assert
        return resultado == "Factura Electrónica";
    }

    /// <summary>
    /// Property 13C: "Ticket Digital" permanece sin cambio (no es valor legacy).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TicketDigital_PermaneceIgual(byte _)
    {
        // Arrange
        var service = CreateService();

        // Act
        var resultado = service.NormalizarTipoComprobante("Ticket Digital");

        // Assert
        return resultado == "Ticket Digital";
    }

    /// <summary>
    /// Property 13D: "Ticket Impreso" permanece sin cambio (no es valor legacy).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TicketImpreso_PermaneceIgual(byte _)
    {
        // Arrange
        var service = CreateService();

        // Act
        var resultado = service.NormalizarTipoComprobante("Ticket Impreso");

        // Assert
        return resultado == "Ticket Impreso";
    }

    /// <summary>
    /// Property 13E: Para cualquier string arbitrario que NO sea un valor legacy conocido,
    /// la función de normalización retorna el mismo string sin modificación (idempotente).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StringArbitrario_NoLegacy_PasaSinCambio()
    {
        // Generador: strings no vacíos que no sean los valores legacy
        var valoresLegacy = new HashSet<string>
        {
            "Ticket Interno",
            "Factura Electronica"
        };

        var stringGen = Arb.Generate<NonEmptyString>()
            .Select(s => s.Get)
            .Where(s => !valoresLegacy.Contains(s));

        return Prop.ForAll(stringGen.ToArbitrary(), (string tipo) =>
        {
            // Arrange
            var service = CreateService();

            // Act
            var resultado = service.NormalizarTipoComprobante(tipo);

            // Assert — debe retornar exactamente el mismo string
            return (resultado == tipo).ToProperty()
                .Label($"Esperaba '{tipo}' sin cambio pero obtuvo '{resultado}'");
        });
    }

    /// <summary>
    /// Property 13F: Para cualquier valor del conjunto completo de tipos conocidos
    /// (legacy + nuevos), la función produce un resultado determinístico consistente.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TodosLosTiposConocidos_MapeoConsistente()
    {
        var tiposConocidos = Gen.Elements(
            "Ticket Interno",
            "Ticket Digital",
            "Ticket Impreso",
            "Factura Electronica"
        );

        return Prop.ForAll(tiposConocidos.ToArbitrary(), (string tipo) =>
        {
            var service = CreateService();

            var resultado1 = service.NormalizarTipoComprobante(tipo);
            var resultado2 = service.NormalizarTipoComprobante(tipo);

            // Verificar determinismo
            var esDeterministico = resultado1 == resultado2;

            // Verificar mapeo correcto según tabla
            var mapeoEsperado = tipo switch
            {
                "Ticket Interno" => "Ticket Digital",
                "Factura Electronica" => "Factura Electrónica",
                _ => tipo
            };

            var mapeoCorrecto = resultado1 == mapeoEsperado;

            return (esDeterministico && mapeoCorrecto).ToProperty()
                .Label($"Input: '{tipo}' → Esperado: '{mapeoEsperado}', Obtenido: '{resultado1}'");
        });
    }
}
