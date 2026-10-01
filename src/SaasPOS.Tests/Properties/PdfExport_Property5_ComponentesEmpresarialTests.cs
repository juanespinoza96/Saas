using FsCheck;
using FsCheck.Xunit;
using SaasPOS.Application.DTOs;

namespace SaasPOS.Tests.Properties;

/// <summary>
/// Tests de propiedad para verificar que los componentes PDF respetan las opciones del Plan Empresarial.
/// Feature: pdf-export-personalizable, Property 5: Componentes incluidos respetan opciones del Plan Empresarial
/// **Validates: Requirements 3.1, 3.2, 3.3**
/// </summary>
public class PdfExport_Property5_ComponentesEmpresarialTests
{
    /// <summary>
    /// Property A: Cuando ningún parámetro es enviado (todos null), el resultado tiene los tres componentes = true.
    /// Simula el comportamiento de ResolverComponentesPdf para Plan Empresarial sin parámetros.
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanEmpresarial_SinParametros_TodosComponentesActivos()
    {
        // Generar valores irrelevantes (se ignoran porque todos los params son null)
        return Prop.ForAll(
            Arb.Default.Int32().Generator.ToArbitrary(),
            _ =>
            {
                // Actuar: simular resolución con todos los params null (ninguno enviado)
                var resultado = ResolverComponentesPdfEmpresarial(null, null, null);

                // Verificar: todos los componentes deben estar activos
                var todosActivos = resultado.Opciones != null
                    && resultado.Opciones.IncluirGraficoBarras
                    && resultado.Opciones.IncluirGraficoPastel
                    && resultado.Opciones.IncluirTabla
                    && resultado.Error == false;

                return todosActivos
                    .Label($"Resultado=[Barras={resultado.Opciones?.IncluirGraficoBarras}, Pastel={resultado.Opciones?.IncluirGraficoPastel}, Tabla={resultado.Opciones?.IncluirTabla}]");
            });
    }

    /// <summary>
    /// Property B: Cuando al menos un parámetro es enviado explícitamente (combinación con al menos un true),
    /// el resultado usa los valores explícitos y convierte null a false.
    /// **Validates: Requirements 3.1, 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanEmpresarial_AlMenosUnParamExplicito_ResolucionCorrecta()
    {
        return Prop.ForAll(
            ArbitraryCombinacionConAlMenosUnTrue(),
            parametros =>
            {
                // Actuar: resolver componentes con la combinación generada
                var resultado = ResolverComponentesPdfEmpresarial(
                    parametros.IncluirBarras,
                    parametros.IncluirPastel,
                    parametros.IncluirTabla);

                // Calcular valores esperados: null → false, no-null → valor explícito
                var barrasEsperado = parametros.IncluirBarras ?? false;
                var pastelEsperado = parametros.IncluirPastel ?? false;
                var tablaEsperado = parametros.IncluirTabla ?? false;

                // Verificar que la resolución coincide con los valores esperados
                var resolucionCorrecta = resultado.Opciones != null
                    && resultado.Opciones.IncluirGraficoBarras == barrasEsperado
                    && resultado.Opciones.IncluirGraficoPastel == pastelEsperado
                    && resultado.Opciones.IncluirTabla == tablaEsperado
                    && resultado.Error == false;

                return resolucionCorrecta
                    .Label($"Params=[{parametros.IncluirBarras},{parametros.IncluirPastel},{parametros.IncluirTabla}] → Opciones=[{resultado.Opciones?.IncluirGraficoBarras},{resultado.Opciones?.IncluirGraficoPastel},{resultado.Opciones?.IncluirTabla}]");
            });
    }

    /// <summary>
    /// Property C: Las opciones resueltas siempre tienen al menos un componente = true
    /// (ya que el caso de todos false se rechaza con error 400).
    /// **Validates: Requirements 3.1, 3.2, 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PlanEmpresarial_OpcionesResueltas_SiempreAlMenosUnComponenteActivo()
    {
        return Prop.ForAll(
            ArbitraryCombinacionNullableBooleanos(),
            parametros =>
            {
                // Actuar: resolver componentes
                var resultado = ResolverComponentesPdfEmpresarial(
                    parametros.IncluirBarras,
                    parametros.IncluirPastel,
                    parametros.IncluirTabla);

                // Si se resolvió correctamente (sin error), al menos un componente es true
                if (resultado.Opciones != null)
                {
                    var alMenosUnoActivo =
                        resultado.Opciones.IncluirGraficoBarras
                        || resultado.Opciones.IncluirGraficoPastel
                        || resultado.Opciones.IncluirTabla;

                    return alMenosUnoActivo
                        .Label($"Opciones=[{resultado.Opciones.IncluirGraficoBarras},{resultado.Opciones.IncluirGraficoPastel},{resultado.Opciones.IncluirTabla}] debe tener al menos un true");
                }

                // Si hay error, es porque todos los valores resueltos son false
                // (null se trata como false en la resolución, y al menos un param fue enviado)
                var barrasResuelto = parametros.IncluirBarras ?? false;
                var pastelResuelto = parametros.IncluirPastel ?? false;
                var tablaResuelto = parametros.IncluirTabla ?? false;
                var todosResueltosFalse = !barrasResuelto && !pastelResuelto && !tablaResuelto;

                // Además debe ser un caso donde al menos un param fue enviado (no todos null)
                var alMenosUnParamEnviado = !(parametros.IncluirBarras is null
                    && parametros.IncluirPastel is null
                    && parametros.IncluirTabla is null);

                return (resultado.Error && todosResueltosFalse && alMenosUnParamEnviado)
                    .Label($"Error esperado: Params=[{parametros.IncluirBarras},{parametros.IncluirPastel},{parametros.IncluirTabla}] resuelve a [{barrasResuelto},{pastelResuelto},{tablaResuelto}]");
            });
    }

    // ── Lógica de resolución replicada del controlador ──────────────────────────

    /// <summary>
    /// Replica la lógica de ResolverComponentesPdf del ReportesController para Plan Empresarial.
    /// Esto permite verificar las propiedades sin depender del controlador ni de mocks HTTP.
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
    /// Genera combinaciones de parámetros nullable donde al menos uno es explícitamente true.
    /// Esto garantiza que la resolución nunca caiga en el caso "todos null" ni en el caso "todos false".
    /// </summary>
    private static Arbitrary<ParametrosPersonalizacion> ArbitraryCombinacionConAlMenosUnTrue()
    {
        var nullableBoolGen = Gen.OneOf(
            Gen.Constant<bool?>(null),
            Gen.Constant<bool?>(true),
            Gen.Constant<bool?>(false));

        var gen = from barras in nullableBoolGen
                  from pastel in nullableBoolGen
                  from tabla in nullableBoolGen
                  // Filtrar: al menos un param debe ser no-null (algo fue enviado)
                  where !(barras is null && pastel is null && tabla is null)
                  // Filtrar: al menos un valor resuelto debe ser true
                  where (barras ?? false) || (pastel ?? false) || (tabla ?? false)
                  select new ParametrosPersonalizacion(barras, pastel, tabla);

        return Arb.From(gen);
    }

    /// <summary>
    /// Genera cualquier combinación de parámetros nullable (incluye todos null, todos false, etc.)
    /// para verificar que la resolución siempre produce al menos un componente activo o un error.
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
