using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Options;
using SaasPOS.Infrastructure.Configuration;
using SaasPOS.Infrastructure.Services;

namespace SaasPOS.Tests.Services;

// Feature: mejoras-operativas-v2, Property 3: Validación SSRF rechaza URLs fuera de lista blanca
/// <summary>
/// Property-based tests para la validación SSRF de URLs.
/// **Validates: Requirements 3.1**
///
/// Property 3: Validación SSRF rechaza URLs fuera de lista blanca
/// "For any URL U y for any lista blanca de dominios W, la función de validación SHALL retornar
/// true si y solo si el dominio de U pertenece a W."
/// </summary>
public class UrlValidationSsrfPropertyTests
{
    /// <summary>
    /// Generador de dominios válidos (letras minúsculas + dígitos, con TLD).
    /// </summary>
    private static Gen<string> DomainGen()
    {
        // Generar nombre de dominio simple: [a-z0-9]{3,10}.[a-z]{2,4}
        var labelChars = Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
            'u', 'v', 'w', 'x', 'y', 'z',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

        var tldChars = Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
            'u', 'v', 'w', 'x', 'y', 'z');

        return from labelLen in Gen.Choose(3, 10)
               from label in Gen.ArrayOf(labelLen, labelChars)
               from tldLen in Gen.Choose(2, 4)
               from tld in Gen.ArrayOf(tldLen, tldChars)
               select $"{new string(label)}.{new string(tld)}";
    }

    /// <summary>
    /// Generador de rutas URL opcionales.
    /// </summary>
    private static Gen<string> PathGen()
    {
        var pathChars = Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'k', 'l', 'm', 'n', 'o', 'p', '/', '-', '_');

        return from len in Gen.Choose(0, 20)
               from chars in Gen.ArrayOf(len, pathChars)
               let path = new string(chars).TrimStart('/').Replace("//", "/")
               select string.IsNullOrEmpty(path) ? "" : $"/{path}";
    }

    /// <summary>
    /// Crea una instancia del servicio con la lista blanca especificada.
    /// </summary>
    private static UrlValidationService CreateService(List<string> allowedDomains)
    {
        var settings = Options.Create(new SecuritySettings
        {
            AllowedCallbackDomains = allowedDomains
        });
        return new UrlValidationService(settings);
    }

    #region Property 3a: URL con dominio EN la lista blanca → IsUrlAllowed retorna true

    /// <summary>
    /// Property 3a: Para cualquier URL cuyo dominio SÍ está en la lista blanca,
    /// IsUrlAllowed debe retornar true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UrlConDominioEnListaBlanca_RetornaTrue()
    {
        var gen = from domain in DomainGen()
                  from numExtra in Gen.Choose(0, 3)
                  from extraDomains in Gen.ArrayOf(numExtra, DomainGen())
                  from scheme in Gen.Elements("http", "https")
                  from path in PathGen()
                  let whitelist = extraDomains.Append(domain).Distinct().ToList()
                  select (domain, scheme, path, whitelist);

        return Prop.ForAll(gen.ToArbitrary(), ((string domain, string scheme, string path, List<string> whitelist) input) =>
        {
            var service = CreateService(input.whitelist);
            var url = $"{input.scheme}://{input.domain}{input.path}";

            var result = service.IsUrlAllowed(url);

            return result.ToProperty()
                .Label($"URL '{url}' con dominio '{input.domain}' en whitelist [{string.Join(", ", input.whitelist)}] " +
                       $"debería ser permitida pero fue rechazada.");
        });
    }

    #endregion

    #region Property 3b: URL con dominio NO en la lista blanca → IsUrlAllowed retorna false

    /// <summary>
    /// Property 3b: Para cualquier URL cuyo dominio NO está en la lista blanca,
    /// IsUrlAllowed debe retornar false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UrlConDominioFueraDeListaBlanca_RetornaFalse()
    {
        var gen = from targetDomain in DomainGen()
                  from whitelistDomain1 in DomainGen()
                  from whitelistDomain2 in DomainGen()
                  from scheme in Gen.Elements("http", "https")
                  from path in PathGen()
                  let whitelist = new List<string> { whitelistDomain1, whitelistDomain2 }
                      .Where(d => d != targetDomain && !targetDomain.EndsWith($".{d}"))
                      .ToList()
                  where whitelist.Count > 0
                  where !whitelist.Any(w => targetDomain == w || targetDomain.EndsWith($".{w}"))
                  select (targetDomain, scheme, path, whitelist);

        return Prop.ForAll(gen.ToArbitrary(), ((string targetDomain, string scheme, string path, List<string> whitelist) input) =>
        {
            var service = CreateService(input.whitelist);
            var url = $"{input.scheme}://{input.targetDomain}{input.path}";

            var result = service.IsUrlAllowed(url);

            return (!result).ToProperty()
                .Label($"URL '{url}' con dominio '{input.targetDomain}' NO en whitelist [{string.Join(", ", input.whitelist)}] " +
                       $"debería ser rechazada pero fue permitida.");
        });
    }

    #endregion

    #region Property 3c: URL con IP privada/interna → IsUrlAllowed retorna false (aun si dominio coincide)

    /// <summary>
    /// Property 3c: Para cualquier URL que apunte a una dirección IP privada o interna
    /// (127.x, 10.x, 192.168.x, 172.16-31.x, ::1, localhost),
    /// IsUrlAllowed debe retornar false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UrlConIpPrivadaOInterna_RetornaFalse()
    {
        // Generador de hosts privados/internos
        var privateHostGen = Gen.OneOf(
            // 127.x.x.x - Loopback
            from b2 in Gen.Choose(0, 255)
            from b3 in Gen.Choose(0, 255)
            from b4 in Gen.Choose(1, 255)
            select $"127.{b2}.{b3}.{b4}",

            // 10.x.x.x - Privada clase A
            from b2 in Gen.Choose(0, 255)
            from b3 in Gen.Choose(0, 255)
            from b4 in Gen.Choose(1, 255)
            select $"10.{b2}.{b3}.{b4}",

            // 192.168.x.x - Privada clase C
            from b3 in Gen.Choose(0, 255)
            from b4 in Gen.Choose(1, 255)
            select $"192.168.{b3}.{b4}",

            // 172.16-31.x.x - Privada clase B
            from b2 in Gen.Choose(16, 31)
            from b3 in Gen.Choose(0, 255)
            from b4 in Gen.Choose(1, 255)
            select $"172.{b2}.{b3}.{b4}",

            // localhost
            Gen.Constant("localhost"),

            // IPv6 loopback
            Gen.Constant("[::1]")
        );

        var gen = from host in privateHostGen
                  from scheme in Gen.Elements("http", "https")
                  from path in PathGen()
                  // La whitelist incluye el host para verificar que IP privada siempre es rechazada
                  let whitelist = new List<string> { host.Replace("[", "").Replace("]", ""), "example.com" }
                  select (host, scheme, path, whitelist);

        return Prop.ForAll(gen.ToArbitrary(), ((string host, string scheme, string path, List<string> whitelist) input) =>
        {
            var service = CreateService(input.whitelist);
            var url = $"{input.scheme}://{input.host}{input.path}";

            var result = service.IsUrlAllowed(url);

            return (!result).ToProperty()
                .Label($"URL '{url}' con host privado/interno '{input.host}' " +
                       $"debería ser rechazada pero fue permitida.");
        });
    }

    #endregion

    #region Property 3d: URL con esquema no-HTTP/HTTPS → IsUrlAllowed retorna false

    /// <summary>
    /// Property 3d: Para cualquier URL con esquema diferente a http/https
    /// (ftp, file, gopher, javascript, etc.), IsUrlAllowed debe retornar false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UrlConEsquemaNoHttp_RetornaFalse()
    {
        var nonHttpSchemes = Gen.Elements("ftp", "file", "gopher", "telnet", "ssh", "ldap", "data", "mailto");

        var gen = from scheme in nonHttpSchemes
                  from domain in DomainGen()
                  from path in PathGen()
                  // El dominio está en la whitelist, pero el esquema no es válido
                  let whitelist = new List<string> { domain }
                  select (scheme, domain, path, whitelist);

        return Prop.ForAll(gen.ToArbitrary(), ((string scheme, string domain, string path, List<string> whitelist) input) =>
        {
            var service = CreateService(input.whitelist);
            var url = $"{input.scheme}://{input.domain}{input.path}";

            var result = service.IsUrlAllowed(url);

            return (!result).ToProperty()
                .Label($"URL '{url}' con esquema '{input.scheme}' (no HTTP/HTTPS) " +
                       $"debería ser rechazada pero fue permitida.");
        });
    }

    #endregion
}
