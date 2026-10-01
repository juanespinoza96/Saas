-- ============================================================
-- SEED DATA para SaaS POS
-- Generado a partir de la base de datos existente
-- Password para todos los usuarios: misma clave de gerente@demo.com
-- Hash: $2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q
-- ============================================================

-- ============================================================
-- 1. PLANES
-- ============================================================
INSERT INTO "Planes" ("Id", "Nombre", "Precio", "LimiteUsuarios", "LimiteAtributos", "LimiteSucursales") VALUES
(1, 'Básico', 350.00, 2, 2, 1),
(2, 'Intermedio', 750.00, 3, 5, 0),
(3, 'Empresarial', 1200.00, 0, 0, 0);

SELECT setval(pg_get_serial_sequence('"Planes"', 'Id'), (SELECT MAX("Id") FROM "Planes"));

-- ============================================================
-- 2. COMERCIOS
-- ============================================================
INSERT INTO "Comercios" ("Id", "Ruc", "RazonSocial", "PlanId", "UsaFacturacionSRI", "RutaFirmaElectronica", "ClaveFirmaEncriptada", "Estado", "FechaRegistro") VALUES
(1, '1234567890001', 'Comercio Demo', 3, false, NULL, NULL, 'Activo', '2026-06-24T20:50:17.321Z'),
(2, '0912345678001', 'Moda Express S.A.', 1, false, NULL, NULL, 'Activo', '2025-01-01T13:00:00.000Z'),
(3, '1798765432001', 'Sabores del Valle Cía. Ltda.', 2, true, NULL, NULL, 'Activo', '2025-01-10T15:00:00.000Z'),
(4, '0501234567001', 'Bar Escolar Unidad Educativa Nacional', 1, false, NULL, NULL, 'Activo', '2024-12-01T14:00:00.000Z'),
(5, '1891234567001', 'Negocio Moroso S.A.', 2, false, NULL, NULL, 'Suspendido', '2024-11-01T14:00:00.000Z');

SELECT setval(pg_get_serial_sequence('"Comercios"', 'Id'), (SELECT MAX("Id") FROM "Comercios"));

-- ============================================================
-- 3. SUCURSALES
-- ============================================================
INSERT INTO "Sucursales" ("Id", "ComercioId", "Nombre", "Direccion", "Telefono", "SerieFacturacion") VALUES
(1, 1, 'Sucursal Principal', 'Av. Principal 123', NULL, NULL),
(2, 1, 'Sucursal Norte', 'Av. de la Prensa N50-100', '0998765432', '001-002'),
(3, 2, 'Local Centro Comercial', 'C.C. Mall del Sol, Local 215', '0412345678', '001-001'),
(4, 3, 'Restaurante Principal', 'Calle Bolívar 5-42 y Sucre', '0398765432', '001-001'),
(5, 3, 'Sucursal Express', 'Food Court - Mall del Río', '0398765433', '001-002'),
(6, 4, 'Bar Principal', 'Unidad Educativa Nacional, Bloque A', '0351234567', NULL),
(7, 5, 'Sucursal Única', 'Av. Quito 12-34', '0381234567', '001-001');

SELECT setval(pg_get_serial_sequence('"Sucursales"', 'Id'), (SELECT MAX("Id") FROM "Sucursales"));

-- ============================================================
-- 4. USUARIOS
-- ============================================================
INSERT INTO "Usuarios" ("Id", "ComercioId", "SucursalId", "Nombre", "Email", "PasswordHash", "Rol", "Activo") VALUES
(1, 1, NULL, 'Super Administrador', 'admin@saaspos.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'SuperAdmin', true),
(2, 1, 1, 'Juan Gerente', 'gerente@demo.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Gerente', true),
(3, 1, 1, 'María Supervisora', 'supervisor@demo.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Supervisor', true),
(4, 1, 1, 'Carlos Cajero', 'cajero@demo.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Cajero', true),
(5, 1, 1, 'Pedro Bodeguero', 'bodeguero@demo.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Bodeguero', true),
(6, 1, 2, 'Ana Cajera Norte', 'cajero.norte@demo.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Cajero', true),
(7, 1, NULL, 'Roberto Dueño', 'dueno@demo.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Dueño', true),
(8, 1, 1, 'Laura Inactiva', 'inactivo@demo.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Cajero', false),
(9, 2, NULL, 'Elena Dueña Moda', 'duena@modaexpress.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Dueño', true),
(10, 2, 3, 'Luis Cajero Moda', 'cajero@modaexpress.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Cajero', true),
(11, 3, 4, 'Fernando Gerente Rest', 'gerente@sabores.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Gerente', true),
(12, 3, 4, 'Diana Cajera Rest', 'cajera@sabores.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Cajero', true),
(13, 3, 5, 'Miguel Cajero Express', 'cajero.express@sabores.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Cajero', true),
(14, 3, 4, 'Rosa Bodeguera Rest', 'bodeguera@sabores.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Bodeguero', true),
(15, 4, NULL, 'Patricia Dueña Bar', 'duena@barescolar.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Dueño', true),
(16, 4, 6, 'Marcos Vendedor Bar', 'vendedor@barescolar.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Cajero', true),
(17, 5, 7, 'Jorge Gerente Moroso', 'gerente@moroso.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Gerente', true),
(18, 3, NULL, 'Carolina Dueña Sabores', 'duena@sabores.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Dueño', true),
(19, 5, NULL, 'Verónica Dueña Moroso', 'duena@moroso.com', '$2a$12$ZUIY08dFnj.43qMVP1rwf.u8gpigi.85FTDB2bpbRRiZ9B5K1nc5q', 'Dueño', true);

SELECT setval(pg_get_serial_sequence('"Usuarios"', 'Id'), (SELECT MAX("Id") FROM "Usuarios"));

-- ============================================================
-- 5. CATEGORIAS
-- ============================================================
INSERT INTO "Categorias" ("Id", "ComercioId", "Nombre") VALUES
(1, 1, 'Electrónica'),
(2, 1, 'Alimentos'),
(3, 1, 'Bebidas'),
(4, 1, 'Limpieza'),
(5, 2, 'Camisetas'),
(6, 2, 'Pantalones'),
(7, 2, 'Accesorios'),
(8, 3, 'Platos Fuertes'),
(9, 3, 'Bebidas'),
(10, 3, 'Postres'),
(11, 3, 'Insumos Cocina'),
(12, 4, 'Snacks'),
(13, 4, 'Bebidas'),
(14, 4, 'Almuerzos');

SELECT setval(pg_get_serial_sequence('"Categorias"', 'Id'), (SELECT MAX("Id") FROM "Categorias"));

-- ============================================================
-- 6. ATRIBUTOS CATEGORIA
-- ============================================================
INSERT INTO "AtributosCategoria" ("Id", "CategoriaId", "NombreAtributo", "TipoDato") VALUES
(1, 1, 'Marca', 'Texto'),
(2, 1, 'Garantía (meses)', 'Numero'),
(3, 1, 'Color', 'Texto'),
(4, 5, 'Talla', 'Texto'),
(5, 5, 'Color', 'Texto'),
(6, 8, 'Tiempo preparación (min)', 'Numero'),
(7, 8, 'Porciones', 'Numero'),
(8, 8, 'Alérgenos', 'Texto');

SELECT setval(pg_get_serial_sequence('"AtributosCategoria"', 'Id'), (SELECT MAX("Id") FROM "AtributosCategoria"));

-- ============================================================
-- 7. PRODUCTOS
-- ============================================================
INSERT INTO "Productos" ("Id", "ComercioId", "CategoriaId", "Nombre", "TipoArticulo", "ValoresDinamicos", "ManejaStock", "UnidadMedida", "CostoProduccion", "PrecioLista", "PrecioMinimo") VALUES
(1, 1, 1, 'Audífonos Bluetooth', 'Venta Directa', '{"Color": "Negro", "Marca": "Sony", "Garantía (meses)": 12}', true, 'Unidades', 15.00, 35.00, 28.00),
(2, 1, 1, 'Cable USB-C 1m', 'Venta Directa', '{"Color": "Blanco", "Marca": "Anker", "Garantía (meses)": 6}', true, 'Unidades', 3.00, 8.50, 6.00),
(3, 1, 1, 'Mouse Inalámbrico', 'Venta Directa', '{"Color": "Gris", "Marca": "Logitech", "Garantía (meses)": 24}', true, 'Unidades', 12.00, 25.00, 20.00),
(4, 1, 2, 'Arroz 1kg', 'Venta Directa', NULL, true, 'Unidades', 0.80, 1.50, 1.20),
(5, 1, 2, 'Aceite Girasol 1L', 'Venta Directa', NULL, true, 'Unidades', 1.50, 3.25, 2.80),
(6, 1, 3, 'Coca-Cola 500ml', 'Venta Directa', NULL, true, 'Unidades', 0.45, 1.00, 0.80),
(7, 1, 3, 'Agua Mineral 600ml', 'Venta Directa', NULL, true, 'Unidades', 0.20, 0.75, 0.50),
(8, 1, 4, 'Detergente 1kg', 'Venta Directa', NULL, true, 'Unidades', 2.00, 4.50, 3.80),
(9, 1, 4, 'Jabón Líquido 500ml', 'Venta Directa', NULL, true, 'Unidades', 1.20, 3.00, 2.50),
(10, 1, 3, 'Servicio de Envío', 'Venta Directa', NULL, false, 'Unidades', 0.00, 2.50, 2.50),
(11, 2, 5, 'Camiseta Polo Negra M', 'Venta Directa', '{"Color": "Negro", "Talla": "M"}', true, 'Unidades', 8.00, 22.00, 18.00),
(12, 2, 5, 'Camiseta Polo Blanca L', 'Venta Directa', '{"Color": "Blanco", "Talla": "L"}', true, 'Unidades', 8.00, 22.00, 18.00),
(13, 2, 6, 'Jean Clásico 32', 'Venta Directa', NULL, true, 'Unidades', 15.00, 45.00, 35.00),
(14, 2, 6, 'Jean Clásico 34', 'Venta Directa', NULL, true, 'Unidades', 15.00, 45.00, 35.00),
(15, 2, 7, 'Cinturón Cuero', 'Venta Directa', NULL, true, 'Unidades', 5.00, 18.00, 14.00),
(16, 2, 7, 'Billetera', 'Venta Directa', NULL, true, 'Unidades', 7.00, 25.00, 20.00),
(17, 3, 11, 'Arroz (kg)', 'Insumo', NULL, true, 'Libras', 0.80, 0.00, 0.00),
(18, 3, 11, 'Pollo (kg)', 'Insumo', NULL, true, 'Libras', 3.50, 0.00, 0.00),
(19, 3, 11, 'Aceite Vegetal (L)', 'Insumo', NULL, true, 'Litros', 2.50, 0.00, 0.00),
(20, 3, 11, 'Papa (kg)', 'Insumo', NULL, true, 'Libras', 0.60, 0.00, 0.00),
(21, 3, 11, 'Tomate (kg)', 'Insumo', NULL, true, 'Libras', 1.20, 0.00, 0.00),
(22, 3, 11, 'Cebolla (kg)', 'Insumo', NULL, true, 'Libras', 0.90, 0.00, 0.00),
(23, 3, 8, 'Arroz con Pollo', 'Ensamblado', '{"Porciones": 1, "Alérgenos": "Ninguno", "Tiempo preparación (min)": 25}', true, 'Unidades', 3.20, 6.50, 5.00),
(24, 3, 8, 'Seco de Pollo', 'Ensamblado', '{"Porciones": 1, "Alérgenos": "Ninguno", "Tiempo preparación (min)": 30}', true, 'Unidades', 3.80, 7.00, 5.50),
(25, 3, 8, 'Papas Fritas Porción', 'Ensamblado', '{"Porciones": 1, "Alérgenos": "Ninguno", "Tiempo preparación (min)": 10}', true, 'Unidades', 1.00, 3.50, 2.50),
(26, 3, 9, 'Jugo Natural 500ml', 'Venta Directa', NULL, true, 'Unidades', 0.80, 2.50, 2.00),
(27, 3, 9, 'Coca-Cola Personal', 'Venta Directa', NULL, true, 'Unidades', 0.50, 1.25, 1.00),
(28, 3, 10, 'Flan de Caramelo', 'Venta Directa', NULL, true, 'Unidades', 1.00, 3.00, 2.50),
(29, 4, 12, 'Papas Fritas Paquete', 'Venta Directa', NULL, true, 'Unidades', 0.30, 0.75, 0.60),
(30, 4, 12, 'Galletas Oreo', 'Venta Directa', NULL, true, 'Unidades', 0.40, 1.00, 0.80),
(31, 4, 12, 'Chupete', 'Venta Directa', NULL, true, 'Unidades', 0.10, 0.25, 0.20),
(32, 4, 13, 'Jugo Caja 200ml', 'Venta Directa', NULL, true, 'Unidades', 0.35, 0.75, 0.60),
(33, 4, 13, 'Agua 500ml', 'Venta Directa', NULL, true, 'Unidades', 0.20, 0.50, 0.40),
(34, 4, 14, 'Almuerzo Escolar', 'Venta Directa', NULL, true, 'Unidades', 1.50, 3.00, 2.50);

SELECT setval(pg_get_serial_sequence('"Productos"', 'Id'), (SELECT MAX("Id") FROM "Productos"));

-- ============================================================
-- 8. RECETAS PRODUCTO (BOM para productos Ensamblados)
-- ============================================================
INSERT INTO "RecetasProducto" ("Id", "ProductoFinalId", "IngredienteId", "CantidadRequerida") VALUES
(1, 23, 17, 0.3000),
(2, 23, 18, 0.5000),
(3, 23, 19, 0.0500),
(4, 23, 21, 0.1000),
(5, 23, 22, 0.0500),
(6, 24, 17, 0.3000),
(7, 24, 18, 0.6000),
(8, 24, 21, 0.1500),
(9, 24, 22, 0.1000),
(10, 25, 20, 0.5000),
(11, 25, 19, 0.1000);

SELECT setval(pg_get_serial_sequence('"RecetasProducto"', 'Id'), (SELECT MAX("Id") FROM "RecetasProducto"));

-- ============================================================
-- 9. PRECIOS POR VOLUMEN
-- ============================================================
INSERT INTO "PreciosVolumen" ("Id", "ProductoId", "CantidadMinima", "PrecioEspecial") VALUES
(1, 4, 12.0000, 1.30),
(2, 4, 24.0000, 1.20),
(3, 6, 6.0000, 0.90),
(4, 6, 12.0000, 0.80),
(5, 2, 5.0000, 7.50),
(6, 2, 10.0000, 6.50),
(7, 11, 3.0000, 20.00),
(8, 11, 6.0000, 18.00);

SELECT setval(pg_get_serial_sequence('"PreciosVolumen"', 'Id'), (SELECT MAX("Id") FROM "PreciosVolumen"));

-- ============================================================
-- 10. STOCK POR SUCURSAL
-- ============================================================
INSERT INTO "StockSucursal" ("Id", "ProductoId", "SucursalId", "CantidadFisica", "StockMinimo") VALUES
(1, 1, 1, 25.0000, 5.0000),
(2, 2, 1, 50.0000, 10.0000),
(3, 3, 1, 15.0000, 3.0000),
(4, 4, 1, 100.0000, 20.0000),
(5, 5, 1, 40.0000, 10.0000),
(6, 6, 1, 200.0000, 48.0000),
(7, 7, 1, 150.0000, 30.0000),
(8, 8, 1, 30.0000, 5.0000),
(9, 9, 1, 3.0000, 5.0000),
(10, 1, 2, 10.0000, 3.0000),
(11, 2, 2, 30.0000, 5.0000),
(12, 3, 2, 8.0000, 3.0000),
(13, 4, 2, 60.0000, 15.0000),
(14, 6, 2, 120.0000, 24.0000),
(15, 7, 2, 80.0000, 20.0000),
(16, 11, 3, 20.0000, 5.0000),
(17, 12, 3, 15.0000, 5.0000),
(18, 13, 3, 12.0000, 3.0000),
(19, 14, 3, 10.0000, 3.0000),
(20, 15, 3, 8.0000, 2.0000),
(21, 16, 3, 6.0000, 2.0000),
(22, 17, 4, 50.0000, 10.0000),
(23, 18, 4, 30.0000, 8.0000),
(24, 19, 4, 10.0000, 3.0000),
(25, 20, 4, 40.0000, 10.0000),
(26, 21, 4, 15.0000, 5.0000),
(27, 22, 4, 12.0000, 4.0000),
(28, 23, 4, 0.0000, 0.0000),
(29, 24, 4, 0.0000, 0.0000),
(30, 25, 4, 0.0000, 0.0000),
(31, 26, 4, 30.0000, 10.0000),
(32, 27, 4, 48.0000, 12.0000),
(33, 28, 4, 15.0000, 5.0000),
(34, 26, 5, 20.0000, 8.0000),
(35, 27, 5, 36.0000, 12.0000),
(36, 28, 5, 10.0000, 3.0000),
(37, 29, 6, 97.0000, 20.0000),
(38, 30, 6, 54.0000, 15.0000),
(39, 31, 6, 198.0000, 50.0000),
(40, 32, 6, 76.0000, 20.0000),
(41, 33, 6, 47.0000, 15.0000),
(42, 34, 6, 28.0000, 5.0000);

SELECT setval(pg_get_serial_sequence('"StockSucursal"', 'Id'), (SELECT MAX("Id") FROM "StockSucursal"));

-- ============================================================
-- 11. CLIENTES
-- ============================================================
INSERT INTO "Clientes" ("Id", "ComercioId", "Identificacion", "Nombre", "Correo", "Direccion", "Telefono", "EsConsumidorFinal") VALUES
(1, 1, '9999999999999', 'Consumidor Final', NULL, NULL, NULL, true),
(2, 1, '1712345678', 'Carlos Mendoza', 'carlos.mendoza@gmail.com', 'Av. Amazonas N34-56', '0991234567', false),
(3, 1, '0912345679', 'María García López', 'maria.garcia@hotmail.com', 'Cdla. Kennedy Norte', '0987654321', false),
(4, 1, '1798765432001', 'Empresa ABC Cía. Ltda.', 'compras@empresaabc.com', 'Zona Industrial Km 4.5', '042123456', false),
(5, 2, '9999999999999', 'Consumidor Final', NULL, NULL, NULL, true),
(6, 2, '0501234568', 'Ana Martínez', 'ana.martinez@yahoo.com', 'Centro Histórico', '0976543210', false),
(7, 3, '9999999999999', 'Consumidor Final', NULL, NULL, NULL, true),
(8, 3, '0102345678', 'Pedro Sánchez', 'pedro.sanchez@gmail.com', 'Av. Remigio Crespo 5-67', '0712345678', false),
(9, 4, '0927286401', 'juAn diego espinoza moreira', 'jd@gmail.com', 'san martin', '0999999999', false),
(10, 4, '1201999115', 'RAQUEL MOREIRA', 'RM@HOTMAIL.COM', 'tulcan', '098888888888', false);

SELECT setval(pg_get_serial_sequence('"Clientes"', 'Id'), (SELECT MAX("Id") FROM "Clientes"));

-- ============================================================
-- 12. SUSCRIPCIONES
-- ============================================================
INSERT INTO "Suscripciones" ("Id", "ComercioId", "PlanId", "FechaInicio", "FechaProximoCorte", "MontoCuota", "EsProporcional", "Estado", "FechaUltimoPago") VALUES
(1, 1, 3, '2026-06-24T05:00:00.000Z', '2026-07-03T05:00:00.000Z', 1200.00, false, 'Activo', NULL),
(2, 2, 1, '2025-01-01T05:00:00.000Z', '2025-02-03T05:00:00.000Z', 350.00, false, 'Activo', '2025-01-03T05:00:00.000Z'),
(3, 3, 2, '2025-01-10T05:00:00.000Z', '2025-02-03T05:00:00.000Z', 562.50, true, 'Activo', '2025-01-10T05:00:00.000Z'),
(4, 4, 1, '2024-12-01T05:00:00.000Z', '2025-01-03T05:00:00.000Z', 350.00, false, 'PorVencer', NULL),
(5, 5, 2, '2024-11-01T05:00:00.000Z', '2024-12-03T05:00:00.000Z', 750.00, false, 'Suspendido', NULL);

SELECT setval(pg_get_serial_sequence('"Suscripciones"', 'Id'), (SELECT MAX("Id") FROM "Suscripciones"));

-- ============================================================
-- 13. CONFIGURACIONES COMERCIO
-- ============================================================
INSERT INTO "ConfiguracionesComercio" ("ComercioId", "EsBarEscolar", "MostrarBotonCliente", "PermiteVentaEnNegativo", "ImpresionAutomaticaTicket") VALUES
(1, true, true, true, true),
(2, false, true, false, true),
(3, false, true, true, true),
(4, true, false, false, false),
(5, false, false, false, true);

-- ============================================================
-- 14. CONFIGURACIONES SUCURSAL
-- ============================================================
INSERT INTO "ConfiguracionesSucursal" ("SucursalId", "EsBarEscolar", "MostrarBotonCliente", "PermiteVentaEnNegativo", "ImpresionAutomaticaTicket") VALUES
(1, true, true, true, true),
(2, true, true, true, true),
(3, false, true, false, true),
(4, false, true, true, true),
(5, false, true, true, true),
(6, true, false, false, false),
(7, false, false, false, true);

-- ============================================================
-- 15. PAGOS COMERCIO
-- ============================================================
INSERT INTO "PagosComercio" ("Id", "ComercioId", "SuscripcionId", "MontoPagado", "FechaPago", "MetodoPago", "Referencia", "RegistradoPor") VALUES
(1, 1, 1, 1200.00, '2025-01-03T05:00:00.000Z', 'Transferencia', 'TRF-2025-001', 1),
(2, 2, 2, 350.00, '2025-01-03T05:00:00.000Z', 'Efectivo', 'REC-001', 1),
(3, 3, 3, 562.50, '2025-01-10T05:00:00.000Z', 'Transferencia', 'TRF-2025-003', 1);

SELECT setval(pg_get_serial_sequence('"PagosComercio"', 'Id'), (SELECT MAX("Id") FROM "PagosComercio"));

-- ============================================================
-- 16. VENTAS
-- ============================================================
INSERT INTO "Ventas" ("Id", "ComercioId", "SucursalId", "UsuarioId", "ClienteId", "Total", "TipoComprobante", "EstadoSRI", "MetodoPago", "CuotasMeses", "ValorCuota", "ReferenciaTransaccion", "FechaVenta") VALUES
(1, 1, 1, 4, 1, 37.50, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2025-01-15T15:30:00.000Z'),
(2, 1, 1, 4, 2, 75.00, 'Ticket Interno', 'No Aplica', 'TarjetaCredito', 3, 25.00, 'VOUCHER-001234', '2025-01-15T19:20:00.000Z'),
(3, 1, 1, 4, 4, 27.40, 'Ticket Interno', 'No Aplica', 'Transferencia', 0, NULL, NULL, '2025-01-16T14:00:00.000Z'),
(4, 1, 2, 6, 1, 17.00, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2025-01-16T16:45:00.000Z'),
(5, 2, 3, 10, 6, 67.00, 'Ticket Interno', 'No Aplica', 'TarjetaDebito', 0, NULL, 'REF-DB-5678', '2025-01-16T20:30:00.000Z'),
(6, 3, 4, 12, 8, 16.50, 'Factura Electronica', 'Autorizada', 'Efectivo', 0, NULL, NULL, '2025-01-17T17:30:00.000Z'),
(7, 3, 4, 12, 7, 9.75, 'Factura Electronica', 'Error', 'Efectivo', 0, NULL, NULL, '2025-01-17T18:00:00.000Z'),
(8, 4, 6, 16, NULL, 0.75, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2025-01-17T14:30:00.000Z'),
(9, 4, 6, 16, NULL, 1.75, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2025-01-17T14:32:00.000Z'),
(10, 1, 1, 4, 3, 54.25, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2025-01-18T21:00:00.000Z'),
(11, 1, 1, 4, 2, 95.00, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2026-06-29T19:17:21.322Z'),
(12, 1, 1, 4, 1, 32.00, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2026-06-29T20:17:21.322Z'),
(13, 1, 2, 6, 1, 22.50, 'Ticket Interno', 'No Aplica', 'TarjetaDebito', 0, NULL, NULL, '2026-06-30T19:17:21.322Z'),
(14, 1, 1, 4, 3, 60.50, 'Ticket Interno', 'No Aplica', 'TarjetaCredito', 3, 20.17, 'VOUCHER-2025-100', '2026-06-30T21:17:21.322Z'),
(15, 1, 1, 4, 4, 130.00, 'Ticket Interno', 'No Aplica', 'Transferencia', 0, NULL, NULL, '2026-07-01T07:17:21.322Z'),
(16, 1, 1, 4, 1, 15.60, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2026-07-01T16:17:21.322Z'),
(17, 1, 2, 6, 1, 50.00, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2026-07-01T17:17:21.322Z'),
(18, 1, 1, 4, 2, 45.75, 'Ticket Interno', 'No Aplica', 'TarjetaDebito', 0, NULL, NULL, '2026-07-01T18:17:21.322Z'),
(19, 1, 2, 6, 1, 18.00, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2026-07-01T18:47:21.322Z'),
(20, 1, 1, 4, 3, 27.00, 'Ticket Interno', 'No Aplica', 'Efectivo', 0, NULL, NULL, '2026-07-01T19:02:21.322Z'),
(21, 4, 6, 16, NULL, 1.00, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T17:26:01.092Z'),
(22, 4, 6, 16, NULL, 1.00, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T17:26:03.715Z'),
(23, 4, 6, 16, NULL, 1.00, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T17:26:04.072Z'),
(24, 4, 6, 16, NULL, 1.00, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T17:26:04.463Z'),
(25, 4, 6, 16, NULL, 1.00, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T17:26:04.879Z'),
(26, 4, 6, 16, NULL, 0.75, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T17:26:27.623Z'),
(27, 4, 6, 16, NULL, 0.50, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T17:26:31.540Z'),
(28, 4, 6, 16, NULL, 0.75, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T17:26:32.469Z'),
(29, 4, 6, 16, NULL, 3.00, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T17:26:33.448Z'),
(30, 4, 6, 16, NULL, 0.75, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T19:53:27.842Z'),
(31, 4, 6, 16, NULL, 1.00, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T19:53:28.424Z'),
(32, 4, 6, 16, NULL, 0.25, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T19:53:28.829Z'),
(33, 4, 6, 16, NULL, 0.75, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T20:49:19.323Z'),
(34, 4, 6, 16, NULL, 0.50, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T20:49:20.693Z'),
(35, 4, 6, 16, NULL, 0.25, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T20:56:40.875Z'),
(36, 4, 6, 16, NULL, 0.75, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T20:56:42.135Z'),
(37, 4, 6, 16, NULL, 0.75, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T21:01:05.849Z'),
(38, 4, 6, 16, NULL, 3.00, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T21:01:08.866Z'),
(39, 4, 6, 16, NULL, 0.75, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T21:05:52.806Z'),
(40, 4, 6, 16, NULL, 0.50, 'Ticket Interno', NULL, 'Efectivo', 0, NULL, NULL, '2026-07-02T21:06:01.528Z');

SELECT setval(pg_get_serial_sequence('"Ventas"', 'Id'), (SELECT MAX("Id") FROM "Ventas"));

-- ============================================================
-- 17. DETALLE VENTAS
-- ============================================================
INSERT INTO "DetalleVentas" ("Id", "VentaId", "ProductoId", "Cantidad", "PrecioRealCobrado") VALUES
(1, 1, 1, 1.0000, 35.00),
(2, 1, 7, 1.0000, 0.75),
(3, 1, 6, 2.0000, 0.88),
(4, 2, 3, 3.0000, 25.00),
(5, 3, 4, 18.0000, 1.30),
(6, 3, 5, 1.0000, 3.25),
(7, 3, 7, 1.0000, 0.75),
(8, 4, 2, 2.0000, 8.50),
(9, 5, 11, 1.0000, 22.00),
(10, 5, 13, 1.0000, 45.00),
(11, 6, 23, 1.0000, 6.50),
(12, 6, 24, 1.0000, 7.00),
(13, 6, 28, 1.0000, 3.00),
(14, 7, 25, 2.0000, 3.50),
(15, 7, 27, 2.0000, 1.25),
(16, 7, 26, 1.0000, 0.25),
(17, 8, 29, 1.0000, 0.75),
(18, 9, 30, 1.0000, 1.00),
(19, 9, 32, 1.0000, 0.75),
(20, 10, 1, 1.0000, 35.00),
(21, 10, 8, 2.0000, 4.50),
(22, 10, 9, 1.0000, 3.00),
(23, 10, 7, 3.0000, 0.75),
(24, 10, 6, 1.0000, 1.00),
(25, 11, 1, 2.0000, 35.00),
(26, 11, 3, 1.0000, 25.00),
(27, 12, 4, 20.0000, 1.30),
(28, 12, 6, 6.0000, 0.90),
(29, 12, 7, 2.0000, 0.75),
(30, 13, 8, 3.0000, 4.50),
(31, 13, 9, 3.0000, 3.00),
(32, 14, 1, 1.0000, 35.00),
(33, 14, 3, 1.0000, 25.00),
(34, 14, 7, 1.0000, 0.50),
(35, 15, 2, 20.0000, 6.50),
(36, 16, 6, 12.0000, 0.80),
(37, 16, 7, 8.0000, 0.75),
(38, 17, 3, 2.0000, 25.00),
(39, 18, 1, 1.0000, 35.00),
(40, 18, 5, 1.0000, 3.25),
(41, 18, 7, 10.0000, 0.75),
(42, 19, 4, 12.0000, 1.30),
(43, 19, 5, 1.0000, 2.40),
(44, 20, 8, 4.0000, 4.50),
(45, 20, 9, 3.0000, 3.00),
(46, 21, 30, 1.0000, 1.00),
(47, 22, 30, 1.0000, 1.00),
(48, 23, 30, 1.0000, 1.00),
(49, 24, 30, 1.0000, 1.00),
(50, 25, 30, 1.0000, 1.00),
(51, 26, 32, 1.0000, 0.75),
(52, 27, 33, 1.0000, 0.50),
(53, 28, 32, 1.0000, 0.75),
(54, 29, 34, 1.0000, 3.00),
(55, 30, 29, 1.0000, 0.75),
(56, 31, 30, 1.0000, 1.00),
(57, 32, 31, 1.0000, 0.25),
(58, 33, 32, 1.0000, 0.75),
(59, 34, 33, 1.0000, 0.50),
(60, 35, 31, 1.0000, 0.25),
(61, 36, 32, 1.0000, 0.75),
(62, 37, 29, 1.0000, 0.75),
(63, 38, 34, 1.0000, 3.00),
(64, 39, 29, 1.0000, 0.75),
(65, 40, 33, 1.0000, 0.50);

SELECT setval(pg_get_serial_sequence('"DetalleVentas"', 'Id'), (SELECT MAX("Id") FROM "DetalleVentas"));

-- ============================================================
-- 18. NOTIFICACIONES
-- ============================================================
INSERT INTO "Notificaciones" ("Id", "ComercioId", "SucursalId", "ProductoId", "Titulo", "Mensaje", "Leida", "TipoNotificacion", "FechaEmision") VALUES
(1, 1, 1, 9, 'Stock Bajo: Jabón Líquido 500ml', 'El producto Jabón Líquido 500ml tiene 3 unidades. Stock mínimo: 5', false, 'StockBajo', '2025-01-18T13:00:00.000Z'),
(2, 4, NULL, NULL, 'Pago Próximo a Vencer', 'Su suscripción vence el 2025-01-03. Realice el pago para evitar suspensión.', false, 'PagoProximo', '2025-01-01T11:00:00.000Z'),
(3, 5, NULL, NULL, 'Cuenta Suspendida por Mora', 'Su cuenta ha sido suspendida por falta de pago.', true, 'PagoMora', '2024-12-06T11:00:00.000Z'),
(4, 1, NULL, NULL, 'Mantenimiento Programado', 'El sistema estará en mantenimiento el 2025-01-25 de 02:00 a 04:00.', false, 'Sistema', '2025-01-20T15:00:00.000Z'),
(5, 3, 4, 22, 'Stock Bajo: Cebolla', 'El insumo Cebolla tiene 12 lb. Stock mínimo: 4 lb.', false, 'StockBajo', '2025-01-17T12:00:00.000Z');

SELECT setval(pg_get_serial_sequence('"Notificaciones"', 'Id'), (SELECT MAX("Id") FROM "Notificaciones"));

-- ============================================================
-- 19. COLA CORREOS
-- ============================================================
INSERT INTO "ColaCorreos" ("Id", "ComercioId", "Destinatario", "Asunto", "CuerpoHtml", "Intentos", "MaxIntentos", "Estado", "FechaCreacion", "FechaEnvio", "UltimoError") VALUES
(1, 4, 'duena@barescolar.com', 'Recordatorio: Pago', '<p>Su suscripción vence el 2025-01-03.</p>', 3, 3, 'Fallido', '2025-01-01T11:00:00.000Z', NULL, 'The SMTP server requires a secure connection or the client was not authenticated.'),
(2, 2, 'duena@modaexpress.com', 'Bienvenido a SaaS POS', '<p>Su cuenta ha sido creada.</p>', 1, 3, 'Enviado', '2025-01-01T12:00:00.000Z', '2025-01-01T13:00:00.000Z', NULL),
(3, 5, 'gerente@moroso.com', 'Suspensión de Cuenta', '<p>Suspendida por falta de pago.</p>', 3, 3, 'Fallido', '2024-12-06T11:00:00.000Z', NULL, 'SMTP Error 550'),
(4, 3, 'gerente@sabores.com', 'Reporte Diario', '<p>Total ventas: $26.50</p>', 3, 3, 'Fallido', '2025-01-18T04:00:00.000Z', NULL, 'The SMTP server requires a secure connection or the client was not authenticated.');

SELECT setval(pg_get_serial_sequence('"ColaCorreos"', 'Id'), (SELECT MAX("Id") FROM "ColaCorreos"));

-- ============================================================
-- 20. SOLICITUDES RECUPERACION
-- ============================================================
INSERT INTO "SolicitudesRecuperacion" ("Id", "UsuarioId", "ComercioId", "Estado", "AprobadoPor", "FechaSolicitud", "FechaResolucion") VALUES
(1, 4, 1, 'Pendiente', NULL, '2025-01-19T19:00:00.000Z', NULL),
(2, 5, 1, 'Aprobada', 2, '2025-01-10T14:00:00.000Z', '2025-01-10T14:30:00.000Z'),
(3, 10, 2, 'Rechazada', 9, '2025-01-12T21:00:00.000Z', '2025-01-12T21:45:00.000Z');

SELECT setval(pg_get_serial_sequence('"SolicitudesRecuperacion"', 'Id'), (SELECT MAX("Id") FROM "SolicitudesRecuperacion"));

-- ============================================================
-- 21. LOGS AUDITORIA (registros representativos)
-- ============================================================
INSERT INTO "LogsAuditoria" ("Id", "ComercioId", "UsuarioId", "Accion", "FechaHora", "TablaAfectada", "RegistroId", "ValoresAnteriores", "ValoresNuevos") VALUES
(1, 1, 2, 'Login Exitoso', '2025-01-18T13:00:00.000Z', 'Usuarios', '2', NULL, '{"IP": "192.168.1.100"}'),
(2, 1, 2, 'Producto Creado', '2025-01-14T15:00:00.000Z', 'Productos', '1', NULL, '{"Nombre": "Audífonos Bluetooth", "PrecioLista": 35}'),
(3, 1, 2, 'Precio Modificado', '2025-01-15T14:00:00.000Z', 'Productos', '3', '{"PrecioLista": 22}', '{"PrecioLista": 25}'),
(4, 1, 4, 'Venta Registrada', '2025-01-15T15:30:00.000Z', 'Ventas', '1', NULL, '{"Total": 37.5, "MetodoPago": "Efectivo"}'),
(5, 1, 4, 'Acceso Denegado', '2025-01-16T19:30:00.000Z', 'Seguridad', '0', NULL, '{"Motivo": "Cajero intentó acceder a configuración"}'),
(6, 2, 1, 'Pago Registrado', '2025-01-03T16:00:00.000Z', 'PagosComercio', '2', NULL, '{"MontoPagado": 350}'),
(7, 5, 1, 'Comercio Suspendido', '2024-12-06T11:00:00.000Z', 'Comercios', '5', '{"Estado": "Activo"}', '{"Estado": "Suspendido"}'),
(8, 1, NULL, 'Login Fallido', '2025-01-19T08:15:00.000Z', 'Seguridad', '0', NULL, '{"IP": "203.0.113.45", "Email": "inexistente@test.com"}');

SELECT setval(pg_get_serial_sequence('"LogsAuditoria"', 'Id'), (SELECT MAX("Id") FROM "LogsAuditoria"));
