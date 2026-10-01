-- Seed ventas agosto 2026 - Comercio 1 (faltante)
INSERT INTO "Ventas" ("ComercioId","SucursalId","UsuarioId","ClienteId","Total","TipoComprobante","EstadoSRI","MetodoPago","CuotasMeses","ValorCuota","ReferenciaTransaccion","FechaVenta") VALUES
(1,1,4,NULL,45.50,'NotaVenta',NULL,'Efectivo',0,NULL,NULL,'2026-08-02 10:15:00+00'),
(1,2,2,NULL,78.90,'NotaVenta',NULL,'Tarjeta',0,NULL,'TXN-2026-001','2026-08-03 14:30:00+00'),
(1,1,4,NULL,23.75,'NotaVenta',NULL,'Efectivo',0,NULL,NULL,'2026-08-04 09:00:00+00'),
(1,2,4,NULL,156.20,'NotaVenta',NULL,'Transferencia',0,NULL,'TRF-2026-001','2026-08-05 16:45:00+00'),
(1,1,7,NULL,92.00,'NotaVenta',NULL,'Efectivo',0,NULL,NULL,'2026-08-06 11:20:00+00'),
(1,1,4,NULL,35.60,'NotaVenta',NULL,'Tarjeta',0,NULL,'TXN-2026-002','2026-08-07 08:30:00+00'),
(1,2,2,NULL,210.50,'NotaVenta',NULL,'Efectivo',0,NULL,NULL,'2026-08-08 15:10:00+00'),
(1,1,4,NULL,18.25,'NotaVenta',NULL,'Efectivo',0,NULL,NULL,'2026-08-10 12:00:00+00'),
(1,2,4,NULL,67.80,'NotaVenta',NULL,'Tarjeta',0,NULL,'TXN-2026-003','2026-08-12 17:30:00+00'),
(1,1,4,NULL,54.30,'NotaVenta',NULL,'Efectivo',0,NULL,NULL,'2026-08-14 10:45:00+00'),
(1,2,2,NULL,125.00,'NotaVenta',NULL,'Transferencia',0,NULL,'TRF-2026-002','2026-08-16 13:20:00+00'),
(1,1,4,NULL,42.10,'NotaVenta',NULL,'Efectivo',0,NULL,NULL,'2026-08-18 09:30:00+00'),
(1,2,4,NULL,89.40,'NotaVenta',NULL,'Tarjeta',0,NULL,'TXN-2026-004','2026-08-20 14:00:00+00'),
(1,1,7,NULL,176.50,'NotaVenta',NULL,'Efectivo',0,NULL,NULL,'2026-08-22 16:15:00+00'),
(1,2,4,NULL,31.50,'NotaVenta',NULL,'Efectivo',0,NULL,NULL,'2026-08-24 11:00:00+00');

-- Detalles para las 15 ventas de Comercio 1
DO $$
DECLARE
  v_base INT;
  v INT;
BEGIN
  SELECT MAX("Id") - 14 INTO v_base FROM "Ventas" WHERE "ComercioId" = 1 AND "FechaVenta" >= '2026-08-01';

  v := v_base;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,1,2,15.00),(v,2,1,15.50);

  v := v_base + 1;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,3,3,18.30),(v,1,1,24.00);

  v := v_base + 2;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,4,2,3.50),(v,5,1,16.75);

  v := v_base + 3;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,1,5,22.00),(v,3,2,23.10);

  v := v_base + 4;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,6,10,1.50),(v,7,8,1.00),(v,8,2,30.00);

  v := v_base + 5;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,2,2,12.80),(v,9,1,10.00);

  v := v_base + 6;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,1,6,25.00),(v,4,5,3.50),(v,6,15,1.70);

  v := v_base + 7;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,7,5,1.05),(v,9,2,6.50);

  v := v_base + 8;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,3,2,21.90),(v,8,1,24.00);

  v := v_base + 9;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,4,4,3.50),(v,5,2,12.50),(v,6,6,1.55);

  v := v_base + 10;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,1,3,25.00),(v,2,4,12.50);

  v := v_base + 11;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,6,8,1.50),(v,7,6,1.10),(v,4,3,3.50);

  v := v_base + 12;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,3,3,22.80),(v,8,1,21.00);

  v := v_base + 13;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,1,5,24.50),(v,5,3,14.00),(v,3,2,17.25);

  v := v_base + 14;
  INSERT INTO "DetalleVentas" ("VentaId","ProductoId","Cantidad","PrecioRealCobrado") VALUES (v,7,10,1.05),(v,9,3,7.00);
END $$;

-- Verificacion
SELECT 'Ventas agosto 2026:' as info, COUNT(*) as total FROM "Ventas" WHERE "FechaVenta" >= '2026-08-01' AND "FechaVenta" < '2026-09-01';
SELECT 'DetalleVentas total:' as info, COUNT(*) as total FROM "DetalleVentas";
