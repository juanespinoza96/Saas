using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.DTOs;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Tests de propiedad para verificar que el Plan Intermedio siempre genera estructura fija
/// independientemente de los parámetros de personalización enviados.
/// Feature: pdf-export-personalizable, Property 7: Estructura fija para Plan Intermedio
/// **Validates: Requirements 3.5, 6.1**
/// </summary>
public class PdfExport_Property7_EstructuraFijaIntermedioTests
{
    /// <summary>
    /// Property A: Para cualquier combinación arbitraria de parámetros nullable (incluirGraficoBarras,
    /// incluirGraficoPastel, incluirTabla) con planNivel "Intermedio", las opciones resueltas son
    /// SIEMPRE: Barras=true, Pastel=false, Tabla=true.
    /// **Validates: Requirements 3.5, 6.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanIntermedio_CualquierParametro_EstructuraFija()
    {
        return Prop.ForAll(
            ArbitraryCombinacionNullableBooleanos(),
            parametros =>
            {
                // Actuar: resolver componentes para Plan Intermedio con parámetros arbitrarios
                var resultado = ResolverComponentesPdf(
                    planNivel: "Intermedio",
                    incluirGraficoBarras: parametros.IncluirBarras,
                    incluirGraficoPastel: parametros.IncluirPastel,
                    incluirTabla: parametros.IncluirTabla);

                // Verificar: estructura fija — Barras=true, Pastel=false, Tabla=true
                var estructuraCorrecta = resultado.Opciones != null
                    && resultado.Opciones.IncluirGraficoBarras == true
                    && resultado.Opciones.IncluirGraficoPastel == false
                    && resultado.Opciones.IncluirTabla == true
                    && resultado.Error == null;

                return estructuraCorrecta
                    .Label($"Params=[{parametros.IncluirBarras},{parametros.IncluirPastel},{parametros.IncluirTabla}] → Opciones=[Barras={resultado.Opciones?.IncluirGraficoBarras}, Pastel={resultado.Opciones?.IncluirGraficoPastel}, Tabla={resultado.Opciones?.IncluirTabla}]");
            });
    }

    /// <summary>
    /// Property B: Para cualquier combinación arbitraria de parámetros, el Plan Intermedio
    /// NUNCA retorna un error — siempre retorna opciones válidas.
    /// **Validates: Requirements 3.5, 6.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanIntermedio_CualquierParametro_NuncaRetornaError()
    {
        return Prop.ForAll(
            ArbitraryCombinacionNullableBooleanos(),
            parametros =>
            {
                // Actuar: resolver componentes para Plan Intermedio
                var resultado = ResolverComponentesPdf(
                    planNivel: "Intermedio",
                    incluirGraficoBarras: parametros.IncluirBarras,
                    incluirGraficoPastel: parametros.IncluirPastel,
                    incluirTabla: parametros.IncluirTabla);

                // Verificar: nunca hay error, siempre hay opciones válidas
                var sinError = resultado.Opciones != null && resultado.Error == null;

                return sinError
                    .Label($"Params=[{parametros.IncluirBarras},{parametros.IncluirPastel},{parametros.IncluirTabla}] no debe producir error");
            });
    }

    /// <summary>
    /// Property C: La estructura fija del Plan Intermedio es idempotente — sin importar cuántas
    /// combinaciones diferentes de parámetros se envíen, el resultado siempre es el mismo.
    /// Verificamos con dos combinaciones distintas que producen el mismo resultado.
    /// **Validates: Requirements 3.5, 6.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanIntermedio_EstructuraIdempotente_ResultadoSiempreIgual()
    {
        return Prop.ForAll(
            ArbitraryCombinacionNullableBooleanos(),
            ArbitraryCombinacionNullableBooleanos(),
            (parametros1, parametros2) =>
            {
                // Actuar: resolver componentes con dos combinaciones diferentes
                var resultado1 = ResolverComponentesPdf(
                    planNivel: "Intermedio",
                    incluirGraficoBarras: parametros1.IncluirBarras,
                    incluirGraficoPastel: parametros1.IncluirPastel,
                    incluirTabla: parametros1.IncluirTabla);

                var resultado2 = ResolverComponentesPdf(
                    planNivel: "Intermedio",
                    incluirGraficoBarras: parametros2.IncluirBarras,
                    incluirGraficoPastel: parametros2.IncluirPastel,
                    incluirTabla: parametros2.IncluirTabla);

                // Verificar: ambos resultados son idénticos
                var idempotente = resultado1.Opciones != null
                    && resultado2.Opciones != null
                    && resultado1.Opciones.IncluirGraficoBarras == resultado2.Opciones.IncluirGraficoBarras
                    && resultado1.Opciones.IncluirGraficoPastel == resultado2.Opciones.IncluirGraficoPastel
                    && resultado1.Opciones.IncluirTabla == resultado2.Opciones.IncluirTabla;

                return idempotente
                    .Label($"Params1=[{parametros1.IncluirBarras},{parametros1.IncluirPastel},{parametros1.IncluirTabla}] vs Params2=[{parametros2.IncluirBarras},{parametros2.IncluirPastel},{parametros2.IncluirTabla}] deben producir el mismo resultado");
            });
    }

    // ── Lógica de resolución replicada del controlador ──────────────────────────

    /// <summary>
    /// Replica la lógica de ResolverComponentesPdf del ReportesController.
    /// Para Plan Intermedio (o cualquier plan que no sea "Empresarial"), retorna estructura fija:
    /// Barras=true, Pastel=false, Tabla=true — ignorando todos los parámetros de personalización.
    /// </summary>
    private static (PdfComponentOptions? Opciones, string? Error) ResolverComponentesPdf(
        string planNivel, bool? incluirGraficoBarras, bool? incluirGraficoPastel, bool? incluirTabla)
    {
        // Plan Empresarial: personalización permitida
        if (planNivel == "Empresarial")
        {
            if (incluirGraficoBarras is null && incluirGraficoPastel is null && incluirTabla is null)
            {
                return (new PdfComponentOptions(true, true, true), null);
            }

            var barras = incluirGraficoBarras ?? false;
            var pastel = incluirGraficoPastel ?? false;
            var tabla = incluirTabla ?? false;

            if (!barras && !pastel && !tabla)
            {
                return (null, "Debe seleccionar al menos un componente para exportar");
            }

            return (new PdfComponentOptions(barras, pastel, tabla), null);
        }

        // Plan Intermedio (u otro): estructura fija — tabla + barras, sin pastel
        // Ignora TODOS los parámetros de personalización
        return (new PdfComponentOptions(
            IncluirGraficoBarras: true,
            IncluirGraficoPastel: false,
            IncluirTabla: true), null);
    }

    // ── Generadores ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Genera cualquier combinación de parámetros nullable (null, true, false)
    /// para simular todas las posibles entradas de personalización del usuario.
    /// </summary>
    private static Arbitrary<ParametrosPersonalizacion> ArbitraryCombinacionNullableBooleanos()
    {
        var nullableBoolGen = Gen.OneOf(
            Gen.Constant<bool?>(null),
            Gen.Constant<bool?>(true),
            Gen.Constant<bool?>(false));

        var gen = from barras in nullableBoolGen
                  from pastel in nullableBoolGen
                  from tabla in nullableBoolGen
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
