using SaasPOS.Domain.Entities;
using SaasPOS.Infrastructure.Data;

namespace SaasPOS.Tests.Integration.Fixtures;

/// <summary>
/// Fluent builder factory for creating test entities.
/// Each builder uses sensible defaults and provides fluent methods to customize.
/// Call CrearAsync(AppDbContext) to persist the entity.
/// </summary>
public static class TestDataBuilder
{
    public static ComercioBuilder Comercio() => new();
    public static UsuarioBuilder Usuario() => new();
    public static ProductoBuilder Producto() => new();
    public static SucursalBuilder Sucursal() => new();
    public static CategoriaBuilder Categoria() => new();
    public static ClienteBuilder Cliente() => new();
}

public class ComercioBuilder
{
    private readonly Comercio _comercio = new()
    {
        Ruc = $"17{Guid.NewGuid().ToString("N")[..11]}",
        RazonSocial = "Test Comercio",
        PlanId = 1,
        Estado = "Activo",
        FechaRegistro = DateTime.UtcNow
    };

    public ComercioBuilder ConPlan(int planId) { _comercio.PlanId = planId; return this; }
    public ComercioBuilder ConRuc(string ruc) { _comercio.Ruc = ruc; return this; }
    public ComercioBuilder ConRazonSocial(string razonSocial) { _comercio.RazonSocial = razonSocial; return this; }
    public ComercioBuilder ConEstado(string estado) { _comercio.Estado = estado; return this; }
    public ComercioBuilder ConFacturacionSRI(bool usa) { _comercio.UsaFacturacionSRI = usa; return this; }

    public async Task<Comercio> CrearAsync(AppDbContext db)
    {
        db.Comercios.Add(_comercio);
        await db.SaveChangesAsync();
        return _comercio;
    }
}

public class UsuarioBuilder
{
    private readonly Usuario _usuario = new()
    {
        Nombre = "Test User",
        Email = $"test-{Guid.NewGuid():N}@test.com",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test123!"),
        Rol = "Cajero",
        Activo = true
    };

    public UsuarioBuilder ConRol(string rol) { _usuario.Rol = rol; return this; }
    public UsuarioBuilder EnComercio(int comercioId) { _usuario.ComercioId = comercioId; return this; }
    public UsuarioBuilder EnSucursal(int sucursalId) { _usuario.SucursalId = sucursalId; return this; }
    public UsuarioBuilder ConEmail(string email) { _usuario.Email = email; return this; }
    public UsuarioBuilder ConNombre(string nombre) { _usuario.Nombre = nombre; return this; }
    public UsuarioBuilder Inactivo() { _usuario.Activo = false; return this; }

    public async Task<Usuario> CrearAsync(AppDbContext db)
    {
        db.Usuarios.Add(_usuario);
        await db.SaveChangesAsync();
        return _usuario;
    }
}

public class ProductoBuilder
{
    private readonly Producto _producto = new()
    {
        Nombre = "Test Producto",
        TipoArticulo = "Venta Directa",
        PrecioLista = 10.00m,
        PrecioMinimo = 5.00m,
        ManejaStock = true,
        UnidadMedida = "Unidades"
    };

    public ProductoBuilder EnComercio(int comercioId) { _producto.ComercioId = comercioId; return this; }
    public ProductoBuilder EnCategoria(int categoriaId) { _producto.CategoriaId = categoriaId; return this; }
    public ProductoBuilder ConTipo(string tipo) { _producto.TipoArticulo = tipo; return this; }
    public ProductoBuilder ConPrecio(decimal precio) { _producto.PrecioLista = precio; return this; }
    public ProductoBuilder ConNombre(string nombre) { _producto.Nombre = nombre; return this; }

    public async Task<Producto> CrearAsync(AppDbContext db)
    {
        db.Productos.Add(_producto);
        await db.SaveChangesAsync();
        return _producto;
    }
}

public class SucursalBuilder
{
    private readonly Sucursal _sucursal = new()
    {
        Nombre = "Sucursal Test",
        Direccion = "Calle Test 123"
    };

    public SucursalBuilder EnComercio(int comercioId) { _sucursal.ComercioId = comercioId; return this; }
    public SucursalBuilder ConNombre(string nombre) { _sucursal.Nombre = nombre; return this; }
    public SucursalBuilder ConDireccion(string direccion) { _sucursal.Direccion = direccion; return this; }

    public async Task<Sucursal> CrearAsync(AppDbContext db)
    {
        db.Sucursales.Add(_sucursal);
        await db.SaveChangesAsync();
        return _sucursal;
    }
}

public class CategoriaBuilder
{
    private readonly Categoria _categoria = new()
    {
        Nombre = "Categoría Test"
    };

    public CategoriaBuilder EnComercio(int comercioId) { _categoria.ComercioId = comercioId; return this; }
    public CategoriaBuilder ConNombre(string nombre) { _categoria.Nombre = nombre; return this; }

    public async Task<Categoria> CrearAsync(AppDbContext db)
    {
        db.Categorias.Add(_categoria);
        await db.SaveChangesAsync();
        return _categoria;
    }
}

public class ClienteBuilder
{
    private readonly Cliente _cliente = new()
    {
        Identificacion = $"09{Guid.NewGuid().ToString("N")[..8]}",
        Nombre = "Cliente Test",
        Correo = $"cliente-{Guid.NewGuid():N}@test.com",
        Direccion = "Calle Test 456",
        Telefono = "0991234567",
        EsConsumidorFinal = false
    };

    public ClienteBuilder EnComercio(int comercioId) { _cliente.ComercioId = comercioId; return this; }
    public ClienteBuilder ConIdentificacion(string identificacion) { _cliente.Identificacion = identificacion; return this; }
    public ClienteBuilder ConNombre(string nombre) { _cliente.Nombre = nombre; return this; }
    public ClienteBuilder ConCorreo(string correo) { _cliente.Correo = correo; return this; }
    public ClienteBuilder ConsumidorFinal() { _cliente.EsConsumidorFinal = true; return this; }

    public async Task<Cliente> CrearAsync(AppDbContext db)
    {
        db.Clientes.Add(_cliente);
        await db.SaveChangesAsync();
        return _cliente;
    }
}
