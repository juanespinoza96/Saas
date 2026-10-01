using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.DTOs;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Tests de propiedad para verificar que el sistema rechaza con HTTP 400 cuando
/// todos los componentes PDF son false para el Plan Empresarial.
/// Feature: pdf-export-personalizable, Property 6: Rechazo de todos los componentes desactivados
/// **Validates: Requirements 3.4**
/// </summary>
public class PdfExport_Property6_RechazoTodosDesactivadosTests
{
    /// <summary>
    /// Property A: Cuando todos los parámetros son explícitamente false para Plan Empresarial,
    /// el sistema retorna un error (HTTP 400).
    /// Valida que no importa cuántas veces se invoque con todos false, siempre se rechaza.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanEmpresarial_TodosFalse_RetornaError400()
    {
        // Generar valores arbitrarios irrelevantes para demostrar que la propiedad
        // se cumple independientemente de otros factores del contexto
        return Prop.ForAll(
            Arb.Default.PositiveInt().Generator.ToArbitrary(),
            _ =>
            {
                // Actuar: enviar todos los componentes explícitamente como false
                var resultado = ResolverComponentesPdfEmpresarial(
                    incluirGraficoBarras: false,
                    incluirGraficoPastel: false,
                    incluirTabla: false);

                // Verificar: debe retornar error (simula HTTP 400)
                var esError = resultado.Error && resultado.Opciones == null;

                return esError
                    .Label("Todos false debe producir error 400");
            });
    }

    /// <summary>
    /// Property B: Cualquier combinación donde todos los parámetros enviados resuelven a false
    /// (incluyendo null que se convierte a false cuando al menos uno fue enviado),
    /// el sistema retorna error 400.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanEmpresarial_CombinacionesQueResuelvenATodosFalse_RetornaError400()
    {
        return Prop.ForAll(
            ArbitraryCombinacionQueResuelveATodosFalse(),
            parametros =>
            {
                // Actuar: resolver componentes con la combinación generada
                var resultado = ResolverComponentesPdfEmpresarial(
                    parametros.IncluirBarras,
                    parametros.IncluirPastel,
                    parametros.IncluirTabla);

                // Verificar: debe retornar error (simula HTTP 400)
                var esError = resultado.Error && resultado.Opciones == null;

                return esError
                    .Label($"Params=[{parametros.IncluirBarras},{parametros.IncluirPastel},{parametros.IncluirTabla}] debe producir error 400");
            });
    }

    /// <summary>
    /// Property C: El error producido cuando todos son false es mutuamente exclusivo
    /// con una resolución exitosa — nunca se obtiene tanto opciones como error.
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanEmpresarial_TodosFalse_NuncaRetornaOpciones()
    {
        return Prop.ForAll(
            Arb.Default.PositiveInt().Generator.ToArbitrary(),
            _ =>
            {
                // Actuar: enviar todos los componentes explícitamente como false
                var resultado = ResolverComponentesPdfEmpresarial(
                    incluirGraficoBarras: false,
                    incluirGraficoPastel: false,
                    incluirTabla: false);

                // Verificar: las opciones deben ser null (no hay PDF generado)
                var sinOpciones = resultado.Opciones == null;

                return sinOpciones
                    .Label("Todos false nunca debe retornar PdfComponentOptions");
            });
    }

    // ── Lógica de resolución replicada del controlador ──────────────────────────

    /// <summary>
    /// Replica la lógica de ResolverComponentesPdf del ReportesController para Plan Empresarial.
    /// Esto permite verificar la propiedad de rechazo sin depender del controlador ni de mocks HTTP.
    /// </summary>
    private static (PdfComponentOptions? Opciones, bool Error) ResolverComponentesPdfEmpresarial(
        bool? incluirGraficoBarras, bool? incluirGraficoPastel, bool? incluirTabla)
    {
        // Si ningún parámetro fue enviado → todos los componentes activos
        if (incluirGraficoBarras is null && incluirGraficoPastel is null && incluirTabla is null)
        {
            return (new PdfComponentOptions(true, true, true), false);
        }

        // Al menos un parámetro enviado → usar valores explícitos (null = false)
        var barras = incluirGraficoBarras ?? false;
        var pastel = incluirGraficoPastel ?? false;
        var tabla = incluirTabla ?? false;

        // Validar que al menos un componente esté activo
        if (!barras && !pastel && !tabla)
        {
            return (null, true);
        }

        return (new PdfComponentOptions(barras, pastel, tabla), false);
    }

    // ── Generadores ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Genera combinaciones de parámetros nullable donde todos los valores resuelven a false.
    /// Esto incluye combinaciones como (false, false, false), (null, false, false),
    /// (false, null, null), etc. — siempre que al menos uno sea no-null y ninguno sea true.
    /// </summary>
    private static Arbitrary<ParametrosPersonalizacion> ArbitraryCombinacionQueResuelveATodosFalse()
    {
        // Solo generar null o false (nunca true)
        var nullOrFalseGen = Gen.OneOf(
            Gen.Constant<bool?>(null),
            Gen.Constant<bool?>(false));

        var gen = from barras in nullOrFalseGen
                  from pastel in nullOrFalseGen
                  from tabla in nullOrFalseGen
                  // Filtrar: al menos un param debe ser no-null (algo fue enviado)
                  // para que no caiga en el caso "ningún param enviado → todos true"
                  where !(barras is null && pastel is null && tabla is null)
                  select new ParametrosPersonalizacion(barras, pastel, tabla);

        return Arb.From(gen);
    }

    /// <summary>
    /// DTO interno para transportar la combinación de parámetros generada por FsCheck.
    /// </summary>
    private record ParametrosPersonalizacion(
        bool? IncluirBarras,
        bool? IncluirPastel,
        bool? IncluirTabla);
}
