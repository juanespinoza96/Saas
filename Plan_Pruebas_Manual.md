# Plan de Pruebas Manuales — SaaS POS Multipropósito

## Indicadores de Estado

| Letra | Significado |
|-------|-------------|
| **P** | Aprobado — Probado y todo funcionó sin novedades |
| **F** | Fallido — Probado y presentó situaciones distintas a la esperada |
| **—** | Pendiente — Aún no se ha puesto en pruebas |

> En la columna "Estado" colocar la letra correspondiente. Si es **F**, anotar observaciones en la columna "Notas".

---

## Roles de prueba requeridos

| Rol | Plan mínimo | Descripción |
|-----|-------------|-------------|
| SuperAdmin | N/A | Dueño del SaaS, accede al Panel Admin |
| Dueño | Empresarial | Propietario del comercio |
| Gerente | Básico+ | Administrador operativo del comercio |
| Supervisor | Empresarial | Monitoreo y reportes |
| Bodeguero | Empresarial | Gestión de inventario |
| Cajero | Básico+ | Registro de ventas |

---

## 1. AUTENTICACIÓN Y SEGURIDAD

### 1.1 Login (POS)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 1.1.1 | Ingresar credenciales válidas (Gerente) | Redirige al Dashboard POS | — | |
| 1.1.2 | Ingresar credenciales válidas (Cajero) | Redirige al Dashboard POS | — | |
| 1.1.3 | Ingresar credenciales válidas (Dueño, Plan Empresarial) | Redirige al Dashboard POS | — | |
| 1.1.4 | Ingresar credenciales válidas (Supervisor) | Redirige al Dashboard POS | — | |
| 1.1.5 | Ingresar credenciales válidas (Bodeguero) | Redirige al Dashboard POS | — | |
| 1.1.6 | Email correcto, contraseña incorrecta | Muestra "Credenciales inválidas" (genérico) | — | |
| 1.1.7 | Email incorrecto | Muestra "Credenciales inválidas" (genérico) | — | |
| 1.1.8 | Cuenta inactiva (Activo=FALSE) | Muestra "Credenciales inválidas" (genérico) | — | |
| 1.1.9 | Comercio suspendido | Muestra error de acceso (403) | — | |
| 1.1.10 | Campos vacíos | Validación inline impide envío | — | |
| 1.1.11 | 10 intentos fallidos consecutivos (mismo email) | Bloqueo de 30 min, mismo mensaje genérico | — | |
| 1.1.12 | 5 intentos fallidos por IP en 1 minuto | Respuesta 429, bloqueo de 15 min | — | |
| 1.1.13 | Intentar acceder a ruta POS sin autenticación | Redirige al Login | — | |

### 1.2 Login (SuperAdmin Panel)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 1.2.1 | Ingresar credenciales de SuperAdmin | Redirige al Dashboard Admin | — | |
| 1.2.2 | Ingresar credenciales de Gerente en panel Admin | Error 403 o mensaje de acceso denegado | — | |
| 1.2.3 | Ingresar credenciales de Cajero en panel Admin | Error 403 o mensaje de acceso denegado | — | |
| 1.2.4 | Credenciales inválidas | Muestra "Credenciales inválidas" | — | |

### 1.3 Sesión y Expiración

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 1.3.1 | Inactividad de 30 minutos | Cierra sesión automáticamente, redirige a Login | — | |
| 1.3.2 | Token JWT expirado (60 min) | API retorna 401, redirige a Login | — | |
| 1.3.3 | Usar token después de logout | API retorna 401 | — | |
| 1.3.4 | Sesión abierta y comercio es suspendido | Sesión se invalida inmediatamente | — | |

### 1.4 Recuperación de Contraseña

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 1.4.1 | Cajero solicita recuperación | Se crea solicitud, requiere aprobación del Gerente | — | |
| 1.4.2 | Gerente aprueba solicitud de recuperación | El usuario puede establecer nueva contraseña | — | |
| 1.4.3 | Solicitar con email inexistente | No revela si el email existe o no | — | |
| 1.4.4 | Más de 3 solicitudes por hora (misma IP) | Respuesta 429 | — | |

---

## 2. CONTROL DE ACCESO POR ROL (RBAC)

### 2.1 Visibilidad del Sidebar (POS)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 2.1.1 | Login como Cajero — verificar opciones sidebar | Solo ve: Dashboard, Punto de Venta, Clientes | — | |
| 2.1.2 | Login como Gerente — verificar opciones sidebar | Ve: Dashboard, POS, Productos, Categorías, Inventario, Clientes, Reportes, Notificaciones, Usuarios, Sucursales, Configuración | — | |
| 2.1.3 | Login como Dueño — verificar opciones sidebar | Mismo que Gerente | — | |
| 2.1.4 | Login como Supervisor — verificar opciones sidebar | Solo ve: Dashboard, Reportes, Notificaciones (monitoreo) | — | |
| 2.1.5 | Login como Bodeguero — verificar opciones sidebar | Solo ve: Dashboard, Inventario, Notificaciones | — | |

### 2.2 Acceso directo a rutas no permitidas (POS)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 2.2.1 | Cajero accede a /productos por URL | Redirige o muestra 403 | — | |
| 2.2.2 | Cajero accede a /reportes por URL | Redirige o muestra 403 | — | |
| 2.2.3 | Cajero accede a /usuarios por URL | Redirige o muestra 403 | — | |
| 2.2.4 | Cajero accede a /configuracion por URL | Redirige o muestra 403 | — | |
| 2.2.5 | Supervisor accede a /pos por URL | Redirige o muestra 403 | — | |
| 2.2.6 | Supervisor accede a /productos por URL | Redirige o muestra 403 | — | |
| 2.2.7 | Bodeguero accede a /pos por URL | Redirige o muestra 403 | — | |
| 2.2.8 | Bodeguero accede a /reportes por URL | Redirige o muestra 403 | — | |
| 2.2.9 | Usuario POS intenta acceder a /api/admin/ | API retorna 403 | — | |

### 2.3 Roles exclusivos de Plan Empresarial

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 2.3.1 | Crear usuario Dueño en Plan Básico | Rechazado con 403 | — | |
| 2.3.2 | Crear usuario Supervisor en Plan Intermedio | Rechazado con 403 | — | |
| 2.3.3 | Crear usuario Bodeguero en Plan Básico | Rechazado con 403 | — | |
| 2.3.4 | Crear usuario Dueño en Plan Empresarial | Se crea exitosamente | — | |
| 2.3.5 | Crear usuario Supervisor en Plan Empresarial | Se crea exitosamente | — | |
| 2.3.6 | Crear usuario Bodeguero en Plan Empresarial | Se crea exitosamente | — | |

---

## 3. SUBSCRIPTION MIDDLEWARE (Límites por Plan)

### 3.1 Límites de Usuarios

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 3.1.1 | Plan Básico: crear 3er usuario | Rechazado con 403 (máx 2) | — | |
| 3.1.2 | Plan Intermedio: crear 4to usuario en una sucursal | Rechazado con 403 (máx 3 por sucursal) | — | |
| 3.1.3 | Plan Empresarial: crear múltiples usuarios | Se crean sin restricción | — | |

### 3.2 Límites de Sucursales

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 3.2.1 | Plan Básico: crear 2da sucursal | Rechazado con 403 (máx 1) | — | |
| 3.2.2 | Plan Intermedio: crear múltiples sucursales | Se crean sin restricción | — | |
| 3.2.3 | Plan Empresarial: crear múltiples sucursales | Se crean sin restricción | — | |

### 3.3 Límites de Atributos Dinámicos

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 3.3.1 | Plan Básico: crear 3er atributo en categoría | Rechazado con 403 (máx 2) | — | |
| 3.3.2 | Plan Intermedio: crear 6to atributo en categoría | Rechazado con 403 (máx 5) | — | |
| 3.3.3 | Plan Empresarial: crear atributos ilimitados | Se crean sin restricción | — | |

### 3.4 Acceso a Reportes por Plan

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 3.4.1 | Plan Básico: acceder a Reportes | Bloqueado, opción oculta en sidebar | — | |
| 3.4.2 | Plan Básico: llamada directa al API de reportes | 403 Forbidden | — | |
| 3.4.3 | Plan Intermedio: acceder a Reportes | Solo 3 predefinidos, sin filtros | — | |
| 3.4.4 | Plan Empresarial: acceder a Reportes | Reportes predefinidos + personalizables con filtros | — | |

### 3.5 Facturación Electrónica por Plan

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 3.5.1 | Plan Básico con UsaFacturacionSRI=TRUE: emitir factura | Bloqueado | — | |
| 3.5.2 | Plan Intermedio con UsaFacturacionSRI=TRUE: emitir factura | Permitido | — | |
| 3.5.3 | Plan Empresarial con UsaFacturacionSRI=TRUE: emitir factura | Permitido | — | |
| 3.5.4 | Comercio con UsaFacturacionSRI=FALSE: ver opción factura | Opción oculta en POS | — | |

---

## 4. GESTIÓN DE COMERCIOS (SuperAdmin Panel)

### 4.1 CRUD de Comercios

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 4.1.1 | Crear comercio con RUC válido | Se crea con estado Activo | — | |
| 4.1.2 | Crear comercio con RUC duplicado | Error: RUC ya registrado | — | |
| 4.1.3 | Crear comercio sin RUC | Validación impide envío | — | |
| 4.1.4 | Crear comercio sin RazónSocial | Validación impide envío | — | |
| 4.1.5 | Suspender comercio activo | Cambia a Suspendido, sesiones invalidadas | — | |
| 4.1.6 | Reactivar comercio suspendido | Cambia a Activo, acceso restaurado | — | |
| 4.1.7 | Cambiar plan de un comercio | Plan aplicado inmediatamente | — | |

### 4.2 Dashboard SuperAdmin

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 4.2.1 | Visualizar resumen de comercios | Muestra conteo por estado (Activo, Por vencer, En mora, Suspendido) | — | |
| 4.2.2 | Verificar ingresos acumulados | Muestra total desglosado por plan | — | |
| 4.2.3 | Verificar proyección mensual | Muestra total basado en comercios activos | — | |
| 4.2.4 | Comercios "Por vencer" resaltados | Estilo visual diferenciado (color/badge) | — | |
| 4.2.5 | Comercios "En mora" resaltados | Estilo visual diferenciado (color/badge) | — | |

### 4.3 Detalle de Comercio

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 4.3.1 | Ver historial de pagos de un comercio | Lista de pagos con monto, fecha, método | — | |
| 4.3.2 | Ver usuarios activos del comercio | Lista de usuarios con rol y sucursal | — | |
| 4.3.3 | Ver sucursales del comercio | Lista con nombre y dirección | — | |
| 4.3.4 | Ver estado de suscripción | Plan, próximo corte, monto, estado de pago | — | |

### 4.4 Gestión de Planes

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 4.4.1 | Ver planes existentes | Muestra los 3 planes con precios y límites | — | |
| 4.4.2 | Editar precio de un plan | Se guarda y refleja en próximo ciclo | — | |

### 4.5 Registro Manual de Pagos

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 4.5.1 | Registrar pago de comercio en mora | Comercio se reactiva, nuevo día de corte calculado | — | |
| 4.5.2 | Registrar pago de comercio al día | Se registra el pago normalmente | — | |

---

## 5. GESTIÓN DE USUARIOS (POS)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 5.1 | Crear usuario Cajero (como Gerente) | Se crea exitosamente, aparece en lista | — | |
| 5.2 | Crear usuario Gerente (como Gerente) | Se crea exitosamente | — | |
| 5.3 | Crear usuario con email duplicado | Error: email ya registrado | — | |
| 5.4 | Crear usuario sin completar campos requeridos | Validación inline en cada campo | — | |
| 5.5 | Editar nombre/email de usuario existente | Se actualiza correctamente | — | |
| 5.6 | Desactivar usuario sin sesiones activas | Se desactiva, campo Activo=FALSE | — | |
| 5.7 | Desactivar usuario CON sesiones activas | Muestra advertencia, requiere confirmación | — | |
| 5.8 | Confirmar desactivación de usuario con sesiones | Sesiones invalidadas, usuario bloqueado | — | |
| 5.9 | Cancelar desactivación de usuario con sesiones | No se desactiva, sesiones permanecen | — | |
| 5.10 | Cajero intenta acceder a /usuarios | Acceso denegado | — | |

---

## 6. GESTIÓN DE SUCURSALES (POS)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 6.1 | Crear sucursal con datos válidos | Se crea y aparece en lista | — | |
| 6.2 | Crear sucursal sin nombre | Validación impide envío | — | |
| 6.3 | Editar datos de sucursal existente | Se actualiza correctamente | — | |
| 6.4 | Eliminar sucursal con usuarios asignados | Usuarios quedan con SucursalId=NULL, historial preservado | — | |
| 6.5 | Verificar que sucursal pertenece al comercio activo | Solo muestra sucursales del ComercioId del JWT | — | |

---

## 7. CATÁLOGO DE PRODUCTOS

### 7.1 CRUD de Productos

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 7.1.1 | Crear producto tipo Venta Directa | Se crea con tipo correcto | — | |
| 7.1.2 | Crear producto tipo Insumo | Se crea, NO aparece en flujo de venta | — | |
| 7.1.3 | Crear producto tipo Ensamblado con receta | Se crea con BOM asociado | — | |
| 7.1.4 | Crear Ensamblado SIN receta | Error: requiere al menos un ingrediente | — | |
| 7.1.5 | Editar producto existente | Se actualiza correctamente | — | |
| 7.1.6 | Eliminar producto | Se elimina (o desactiva) | — | |
| 7.1.7 | Crear producto sin campos obligatorios | Validación inline por campo | — | |
| 7.1.8 | Verificar aislamiento: solo productos del comercio | No aparecen productos de otros comercios | — | |

### 7.2 Categorías y Atributos Dinámicos

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 7.2.1 | Crear categoría | Se crea y aparece en lista | — | |
| 7.2.2 | Editar categoría | Se actualiza nombre/descripción | — | |
| 7.2.3 | Crear atributo dinámico en categoría | Aparece en formulario de productos de esa categoría | — | |
| 7.2.4 | Asignar valores dinámicos a producto (JSONB) | Se almacena y muestra correctamente | — | |
| 7.2.5 | Verificar aislamiento de categorías por comercio | Solo categorías del ComercioId | — | |

### 7.3 Precios por Volumen

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 7.3.1 | Crear regla de precio por volumen | Se guarda con CantidadMinima y PrecioEspecial | — | |
| 7.3.2 | Precio especial menor al PrecioMinimo | Rechazado con error descriptivo | — | |
| 7.3.3 | Múltiples reglas para un producto | Se guardan todas correctamente | — | |
| 7.3.4 | En venta: cantidad alcanza regla de volumen | Se aplica PrecioEspecial automáticamente | — | |
| 7.3.5 | En venta: cantidad NO alcanza ninguna regla | Se aplica PrecioLista | — | |
| 7.3.6 | En venta: cantidad alcanza múltiples reglas | Se aplica la de mayor CantidadMinima | — | |

---

## 8. GESTIÓN DE STOCK / INVENTARIO

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 8.1 | Ver stock por sucursal | Muestra CantidadFisica por producto/sucursal | — | |
| 8.2 | Ingresar mercadería (aumentar stock) | CantidadFisica incrementa | — | |
| 8.3 | Ajustar inventario manualmente | CantidadFisica se actualiza al valor indicado | — | |
| 8.4 | Venta de Venta Directa: descuento directo | CantidadFisica decrementa por cantidad vendida | — | |
| 8.5 | Venta de Ensamblado: descuento por BOM | Insumos decrementan según receta × cantidad | — | |
| 8.6 | Venta de Ensamblado: stock del ensamblado NO cambia | El producto ensamblado mantiene su cantidad | — | |
| 8.7 | PermiteVentaEnNegativo=FALSE: vender sin stock (Venta Directa) | Bloqueado, mensaje de error | — | |
| 8.8 | PermiteVentaEnNegativo=FALSE: vender Ensamblado sin insumo | Bloqueado si algún insumo queda negativo | — | |
| 8.9 | PermiteVentaEnNegativo=TRUE: vender sin stock | Permitido, stock queda negativo | — | |
| 8.10 | Notificación de stock bajo al cruzar umbral mínimo | Se crea notificación + correo encolado | — | |
| 8.11 | Bodeguero puede gestionar stock | Acceso permitido | — | |
| 8.12 | Cajero NO puede acceder a inventario | Acceso denegado | — | |

---

## 9. REGISTRO DE VENTAS (Flujo Normal)

### 9.1 Flujo de Venta Básico

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 9.1.1 | Agregar producto Venta Directa al carrito | Se agrega con precio correcto | — | |
| 9.1.2 | Agregar producto Ensamblado al carrito | Se agrega con precio correcto | — | |
| 9.1.3 | Verificar que Insumos NO aparecen en selección | No visibles en interfaz de venta | — | |
| 9.1.4 | Modificar cantidad de producto en carrito | Total se recalcula | — | |
| 9.1.5 | Eliminar producto del carrito | Se elimina, total se recalcula | — | |
| 9.1.6 | Confirmar venta con Ticket Interno | Venta registrada, ticket generado | — | |
| 9.1.7 | Verificar cálculo del Total | Suma de (Cantidad × PrecioRealCobrado) por línea | — | |
| 9.1.8 | Venta con precio por volumen aplicado | PrecioEspecial usado en cálculo | — | |

### 9.2 Métodos de Pago

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 9.2.1 | Pagar en Efectivo — monto suficiente | Calcula cambio correctamente | — | |
| 9.2.2 | Pagar en Efectivo — monto insuficiente | Bloquea confirmación, muestra error | — | |
| 9.2.3 | Pagar con Tarjeta de Crédito — sin diferir (Corriente) | Registra CuotasMeses=0 | — | |
| 9.2.4 | Pagar con Tarjeta de Crédito — 3 meses | Muestra ValorCuota = Total/3 | — | |
| 9.2.5 | Pagar con Tarjeta de Crédito — 6 meses | Muestra ValorCuota = Total/6 | — | |
| 9.2.6 | Pagar con Tarjeta de Crédito — 9 meses | Muestra ValorCuota = Total/9 | — | |
| 9.2.7 | Pagar con Tarjeta de Crédito — 12 meses | Muestra ValorCuota = Total/12 | — | |
| 9.2.8 | Pagar con Tarjeta de Crédito — 18 meses | Muestra ValorCuota = Total/18 | — | |
| 9.2.9 | Pagar con Tarjeta de Débito | Sin opciones de diferimiento, procede directo | — | |
| 9.2.10 | Pagar con Transferencia | Sin opciones de diferimiento, procede directo | — | |
| 9.2.11 | Tarjeta (crédito/débito) — campo Referencia opcional | Permite ingresar o dejar vacío | — | |
| 9.2.12 | Resumen de pago antes de confirmar | Muestra método, cuotas, valor cuota, total | — | |

### 9.3 Asociación de Cliente

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 9.3.1 | MostrarBotonCliente=TRUE: campo cliente visible | Muestra campo de búsqueda de cliente | — | |
| 9.3.2 | MostrarBotonCliente=FALSE: campo cliente oculto | No aparece nada de clientes en venta | — | |
| 9.3.3 | Buscar cliente por identificación — encontrado | Muestra datos del cliente | — | |
| 9.3.4 | Buscar cliente — no encontrado, crear nuevo | Abre formulario con datos de búsqueda prellenados | — | |
| 9.3.5 | Venta con cliente asociado | ClienteId guardado en Ventas | — | |
| 9.3.6 | Venta sin cliente | ClienteId NULL, venta procede normal | — | |

### 9.4 Impresión Automática

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 9.4.1 | ImpresionAutomaticaTicket=TRUE: confirmar venta | Envía a impresión automáticamente | — | |
| 9.4.2 | ImpresionAutomaticaTicket=FALSE: confirmar venta | No imprime, permite hacerlo manual | — | |

---

## 10. FLUJO BAR ESCOLAR

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 10.1 | EsBarEscolar=TRUE: interfaz de venta | Muestra grilla de productos con precios | — | |
| 10.2 | Seleccionar producto en grilla | Venta registrada: qty=1, PrecioLista, Ticket Interno | — | |
| 10.3 | Verificar que NO pide selección de cliente | No aparece campo de cliente | — | |
| 10.4 | Verificar que NO pide tipo de comprobante | Siempre Ticket Interno | — | |
| 10.5 | Verificar que NO hay pantalla intermedia | Registro directo en 1 clic | — | |
| 10.6 | Venta de Venta Directa en modo Bar | Stock decrementa directamente | — | |
| 10.7 | Venta de Ensamblado en modo Bar | Insumos decrementan por BOM | — | |
| 10.8 | Verificar que Insumos NO aparecen en grilla | Solo Venta Directa y Ensamblado | — | |
| 10.9 | EsBarEscolar=FALSE: interfaz normal | Muestra flujo de venta estándar | — | |
| 10.10 | Cambiar config a EsBarEscolar=TRUE sin reiniciar | Siguiente solicitud usa modo Bar | — | |

---

## 11. FACTURACIÓN ELECTRÓNICA SRI

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 11.1 | UsaFacturacionSRI=FALSE: opción de factura | Oculta en POS | — | |
| 11.2 | UsaFacturacionSRI=TRUE: seleccionar Factura Electrónica | Requiere identificación del cliente | — | |
| 11.3 | Emitir factura sin cliente identificado | Bloqueado, pide identificación | — | |
| 11.4 | Emitir factura con cliente válido | Se genera XML, firma y envía al SRI | — | |
| 11.5 | SRI responde Autorizada | EstadoSRI = 'Autorizada', mensaje de éxito | — | |
| 11.6 | SRI responde error/rechazo | EstadoSRI = 'Error', mensaje con motivo | — | |
| 11.7 | SRI no responde (timeout) | EstadoSRI = 'Error', mensaje de timeout | — | |
| 11.8 | Intentar reenviar factura ya Autorizada | EstadoSRI permanece 'Autorizada' sin cambio | — | |

---

## 12. GESTIÓN DE CLIENTES

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 12.1 | Crear cliente con datos completos | Se crea exitosamente | — | |
| 12.2 | Crear cliente con Identificación duplicada (mismo comercio) | Error: ya existe | — | |
| 12.3 | Crear consumidor final (EsConsumidorFinal=TRUE) | Correo, Dirección, Teléfono opcionales | — | |
| 12.4 | Crear cliente regular sin correo | Validación: campo requerido | — | |
| 12.5 | Buscar cliente por Identificación | Retorna resultado correcto | — | |
| 12.6 | MostrarBotonCliente=FALSE: toda funcionalidad cliente oculta | Sin acceso al directorio desde venta | — | |
| 12.7 | Verificar aislamiento: clientes solo del comercio | No ve clientes de otro ComercioId | — | |

---

## 13. REPORTERÍA Y GRÁFICOS

### 13.1 Acceso según Plan

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 13.1.1 | Plan Básico: sección Reportes | No visible / 403 | — | |
| 13.1.2 | Plan Intermedio: ver reportes disponibles | Solo 3 reportes predefinidos | — | |
| 13.1.3 | Plan Intermedio: intentar filtros personalizados | No disponible | — | |
| 13.1.4 | Plan Empresarial: ver reportes disponibles | Predefinidos + personalizables | — | |
| 13.1.5 | Plan Empresarial: filtros por fecha, sucursal, categoría, producto | Todos funcionales | — | |

### 13.2 Reportes Predefinidos (Plan Intermedio+)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 13.2.1 | Reporte: Producto con mayor cantidad vendida (mes actual) | Muestra producto correcto con cifras | — | |
| 13.2.2 | Reporte: Categoría con mayor monto de ventas (mes actual) | Muestra categoría correcta | — | |
| 13.2.3 | Reporte: Sucursal con mayor monto total (mes actual) | Muestra sucursal correcta | — | |

### 13.3 Reportes Personalizables (Plan Empresarial)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 13.3.1 | Filtrar por rango de fechas | Datos del período seleccionado | — | |
| 13.3.2 | Filtrar por sucursal específica | Solo datos de esa sucursal | — | |
| 13.3.3 | Filtrar por categoría | Solo productos de esa categoría | — | |
| 13.3.4 | Filtrar por producto | Solo ventas de ese producto | — | |
| 13.3.5 | Filtrar por método de pago | Desglose por método seleccionado | — | |
| 13.3.6 | Combinar múltiples filtros | Intersección de resultados | — | |

### 13.4 Gráficos Estadísticos

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 13.4.1 | Gráfico de ventas por día (líneas/barras) | Se renderiza correctamente con datos reales | — | |
| 13.4.2 | Gráfico de ventas por categoría (pie/donut) | Proporción visual correcta | — | |
| 13.4.3 | Gráfico de ventas por sucursal (barras comparativas) | Barras con valores correctos | — | |
| 13.4.4 | Gráfico de métodos de pago (distribución) | Porcentajes correctos | — | |
| 13.4.5 | Gráfico de productos más vendidos (top N) | Ranking correcto | — | |
| 13.4.6 | Gráficos sin datos (período sin ventas) | Muestra estado vacío apropiado, no error | — | |
| 13.4.7 | Gráficos con un solo dato | Se renderiza sin distorsión | — | |
| 13.4.8 | Tooltip al pasar sobre punto/barra en gráfico | Muestra valor exacto | — | |
| 13.4.9 | Leyenda del gráfico | Visible y corresponde a los datos | — | |

### 13.5 Exportación

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 13.5.1 | Exportar reporte en PDF | Descarga archivo PDF legible | — | |
| 13.5.2 | Exportar reporte en CSV | Descarga CSV con datos correctos | — | |
| 13.5.3 | Exportar reporte sin datos | PDF/CSV vacío o mensaje indicando sin resultados | — | |

### 13.6 Aislamiento en Reportes

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 13.6.1 | Reporte solo incluye ventas del ComercioId | No incluye datos de otros comercios | — | |
| 13.6.2 | Supervisor puede ver reportes | Acceso permitido | — | |
| 13.6.3 | Cajero NO puede ver reportes | Acceso denegado | — | |
| 13.6.4 | Bodeguero NO puede ver reportes financieros | Acceso denegado | — | |

---

## 14. NOTIFICACIONES

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 14.1 | Stock cae bajo umbral → notificación creada | Aparece en lista con Leida=FALSE | — | |
| 14.2 | Contador de notificaciones no leídas | Badge/icono con número correcto | — | |
| 14.3 | Marcar notificación como leída | Leida=TRUE, contador decrementa | — | |
| 14.4 | Aislamiento: solo notificaciones del comercio | No ve notificaciones de otros comercios | — | |
| 14.5 | Notificación de stock bajo genera correo | Registro en ColaCorreos | — | |
| 14.6 | Notificación de facturación (7 días antes) | Aparece correctamente | — | |
| 14.7 | Notificación de mora | Aparece correctamente | — | |

---

## 15. CICLO DE FACTURACIÓN (Background)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 15.1 | Comercio contratado el día 3 | Cuota completa, período normal | — | |
| 15.2 | Comercio contratado después del día 3 | Cuota proporcional calculada correctamente | — | |
| 15.3 | 7 días antes del corte | Notificación "Por vencer" + correo encolado | — | |
| 15.4 | Día del corte sin pago | Notificación de mora + correo encolado | — | |
| 15.5 | 3 días después del corte sin pago | Comercio suspendido automáticamente | — | |
| 15.6 | Comercio suspendido → usuarios no pueden ingresar | Login devuelve error | — | |
| 15.7 | SuperAdmin registra pago de comercio en mora | Se reactiva, nuevo día de corte | — | |
| 15.8 | Verificar fórmula proporcional | (PrecioPlan / DíasTotalesMes) × DíasRestantes | — | |

---

## 16. COLA DE CORREOS (Email Service)

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 16.1 | Correo encolado con Estado='Pendiente' | Aparece en ColaCorreos | — | |
| 16.2 | Envío exitoso | Estado cambia a 'Enviado' con timestamp | — | |
| 16.3 | Envío fallido (1 intento) | Intentos incrementa, sigue Pendiente | — | |
| 16.4 | 3 fallos consecutivos | Estado = 'Fallido', UltimoError registrado | — | |

---

## 17. AUDITORÍA

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 17.1 | Crear usuario → registro en LogsAuditoria | Registro con Accion, ValoresNuevos | — | |
| 17.2 | Modificar producto → registro en LogsAuditoria | Registro con ValoresAnteriores y ValoresNuevos | — | |
| 17.3 | Registrar venta → registro en LogsAuditoria | TablaAfectada='Ventas', RegistroId correcto | — | |
| 17.4 | Eliminar usuario → UsuarioId NULL en log | ON DELETE SET NULL preserva el log | — | |
| 17.5 | SuperAdmin accede a LogsAuditoria | Acceso permitido | — | |
| 17.6 | Gerente intenta acceder a LogsAuditoria | 403 Forbidden | — | |
| 17.7 | Cajero intenta acceder a LogsAuditoria | 403 Forbidden | — | |
| 17.8 | Evento de seguridad (rate limit) → log creado | TablaAfectada='Seguridad' | — | |
| 17.9 | Cambio de configuración → log creado | ValoresAnteriores y ValoresNuevos | — | |

---

## 18. AISLAMIENTO MULTI-TENANT

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 18.1 | Gerente de Comercio A intenta ver productos de Comercio B | Solo ve los suyos | — | |
| 18.2 | Manipular request con ComercioId de otro tenant | 403 sin revelar existencia del recurso | — | |
| 18.3 | Cajero de Comercio A intenta buscar cliente de Comercio B | No lo encuentra | — | |
| 18.4 | Verificar que ventas solo muestran las del comercio | Aislamiento completo | — | |
| 18.5 | SuperAdmin puede ver datos cross-tenant vía /api/admin/ | Acceso permitido | — | |
| 18.6 | Endpoint /api/tenants/ NUNCA retorna datos cross-tenant | Verificar con diferentes ComercioId | — | |

---

## 19. CONFIGURACIÓN DEL COMERCIO

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 19.1 | Nuevo comercio: ConfiguracionesComercio con valores default | EsBarEscolar=F, MostrarBotonCliente=F, PermiteVentaEnNegativo=F, ImpresionAutomatica=T | — | |
| 19.2 | Gerente cambia EsBarEscolar a TRUE | POS cambia a modo Bar sin reiniciar | — | |
| 19.3 | Gerente cambia MostrarBotonCliente | Efecto inmediato en flujo de venta | — | |
| 19.4 | Gerente cambia PermiteVentaEnNegativo | Efecto inmediato en validación de stock | — | |
| 19.5 | Cajero intenta acceder a Configuración | Opción oculta + 403 en API | — | |
| 19.6 | Cambio genera registro de auditoría | Log con valores anteriores y nuevos | — | |

---

## 20. INTERFAZ VISUAL Y UX

### 20.1 Tema Visual

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 20.1.1 | Toggle Light → Dark (POS) | Fondo oscuro, texto claro, se persiste | — | |
| 20.1.2 | Toggle Dark → Light (POS) | Fondo claro, se persiste | — | |
| 20.1.3 | Toggle Light → Dark (Admin) | Fondo oscuro, texto claro, se persiste | — | |
| 20.1.4 | Recargar página después de cambio de tema | Tema persistido se aplica | — | |
| 20.1.5 | Tema almacenado en localStorage | Verificar key en DevTools | — | |

### 20.2 Colores Semánticos de Botones

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 20.2.1 | Botones de confirmación/guardado | Verde suave (~#4CAF7D) | — | |
| 20.2.2 | Botones de edición/modificación | Amarillo suave (~#F5A623) | — | |
| 20.2.3 | Botones de eliminación/cancelación | Rojo suave (~#E57373) | — | |
| 20.2.4 | Saturación de colores ≤ 70% HSL | Verificar con inspector CSS | — | |
| 20.2.5 | Consistencia en todas las pantallas POS | Mismo patrón de colores | — | |
| 20.2.6 | Consistencia en todas las pantallas Admin | Mismo patrón de colores | — | |

### 20.3 Navegación y Estructura

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 20.3.1 | Sidebar fijo a la izquierda (POS) | Visible con iconos + etiquetas | — | |
| 20.3.2 | Nombre del Comercio visible en cabecera/sidebar | Siempre visible en todas las pantallas | — | |
| 20.3.3 | Sidebar muestra solo opciones del rol actual | Opciones filtradas correctamente | — | |
| 20.3.4 | Formularios abren en modal (no nueva página) | Confirmar en: Productos, Usuarios, Clientes, Categorías, Sucursales | — | |
| 20.3.5 | Modal cierra con botón X | Se cierra | — | |
| 20.3.6 | Modal cierra con tecla Escape (sin cambios) | Se cierra | — | |
| 20.3.7 | Modal con cambios no guardados + Escape | Muestra confirmación antes de cerrar | — | |

### 20.4 Accesibilidad y UX

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 20.4.1 | Validación inline al guardar datos inválidos | Mensajes junto a cada campo erróneo | — | |
| 20.4.2 | Botones deshabilitados durante request | Botón cambia aspecto + indicador de carga | — | |
| 20.4.3 | Confirmación antes de eliminar producto | Diálogo de confirmación con resumen | — | |
| 20.4.4 | Confirmación antes de desactivar usuario | Diálogo con advertencia de sesiones | — | |
| 20.4.5 | Confirmación antes de suspender comercio | Diálogo de confirmación | — | |
| 20.4.6 | Doble clic en botón de guardar/confirmar | Solo se envía una vez (botón deshabilitado) | — | |

---

## 21. SEGURIDAD AVANZADA (Ataques y Protección)

### 21.1 Rate Limiting

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 21.1.1 | Más de 100 requests/min (IP global) | 429 con Retry-After header | — | |
| 21.1.2 | Más de 5 intentos login/min (IP) | 429, bloqueo 15 min | — | |
| 21.1.3 | Más de 3 recovery/hora (IP) | 429 | — | |
| 21.1.4 | 500 requests en 5 min (misma IP) | IP bloqueada 30 min (403) | — | |

### 21.2 Validación de Input

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 21.2.1 | Enviar SQL injection en campo de texto | 422 rechazado | — | |
| 21.2.2 | Enviar `<script>` en campo de texto | 422 rechazado | — | |
| 21.2.3 | Enviar `javascript:` en campo | 422 rechazado | — | |
| 21.2.4 | Body mayor a 1MB | 413 Payload Too Large | — | |
| 21.2.5 | Content-Type incorrecto (ej: text/plain) | 415 Unsupported Media Type | — | |

### 21.3 Headers de Seguridad

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 21.3.1 | Verificar X-Content-Type-Options: nosniff | Presente en respuesta | — | |
| 21.3.2 | Verificar X-Frame-Options: DENY | Presente en respuesta | — | |
| 21.3.3 | Verificar Strict-Transport-Security | Presente con max-age correcto | — | |
| 21.3.4 | Verificar Referrer-Policy | strict-origin-when-cross-origin | — | |

### 21.4 Account Lockout

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 21.4.1 | 10 intentos fallidos (mismo email, 30 min) | Cuenta bloqueada 30 min | — | |
| 21.4.2 | Intento con contraseña correcta durante bloqueo | Mismo mensaje genérico (no revela bloqueo) | — | |
| 21.4.3 | Login después de 30 min de bloqueo | Acceso restaurado | — | |

---

## 22. DASHBOARD POS

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 22.1 | Ventas del día mostradas | Total correcto según ventas registradas hoy | — | |
| 22.2 | Notificaciones pendientes | Contador visible y correcto | — | |
| 22.3 | Accesos rápidos según rol (Gerente) | Muestra accesos a módulos permitidos | — | |
| 22.4 | Accesos rápidos según rol (Cajero) | Solo accesos a venta y clientes | — | |
| 22.5 | Dashboard sin ventas del día | Muestra $0 o estado vacío apropiado | — | |

---

## 23. ERRORES Y ESTADOS VACÍOS

### 23.1 Errores de Red/API

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 23.1.1 | API no disponible (servidor caído) | Mensaje amigable de error, no pantalla blanca | — | |
| 23.1.2 | Base de datos no disponible | API retorna 503 con mensaje genérico | — | |
| 23.1.3 | Timeout de request | Mensaje de error, permite reintentar | — | |
| 23.1.4 | Error 500 inesperado | Mensaje genérico, no expone stack trace | — | |

### 23.2 Estados Vacíos

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 23.2.1 | Lista de productos vacía | Mensaje "No hay productos" o similar | — | |
| 23.2.2 | Lista de ventas vacía | Mensaje apropiado | — | |
| 23.2.3 | Lista de clientes vacía | Mensaje apropiado | — | |
| 23.2.4 | Notificaciones vacías | Mensaje "Sin notificaciones" | — | |
| 23.2.5 | Reportes sin datos en período | Gráficos vacíos con estado apropiado | — | |

### 23.3 Validaciones de Formularios

| # | Caso de prueba | Resultado esperado | Estado | Notas |
|---|----------------|-------------------|--------|-------|
| 23.3.1 | Email con formato inválido | Mensaje inline: formato incorrecto | — | |
| 23.3.2 | Campo numérico con texto | Validación impide o muestra error | — | |
| 23.3.3 | Precio negativo | Error de validación | — | |
| 23.3.4 | Cantidad = 0 en venta | Validación impide | — | |
| 23.3.5 | Campo requerido vacío | Mensaje inline específico | — | |
| 23.3.6 | RUC con formato inválido (no 13 dígitos) | Error de validación | — | |

---

## 24. PANTALLAS COMPLETAS — CHECKLIST VISUAL

### 24.1 POS (Frontend A)

| # | Pantalla | Renderiza | Navegación OK | Tema Light | Tema Dark | Estado | Notas |
|---|----------|-----------|---------------|------------|-----------|--------|-------|
| 24.1.1 | Login | — | — | — | — | — | |
| 24.1.2 | Dashboard | — | — | — | — | — | |
| 24.1.3 | Punto de Venta (Normal) | — | — | — | — | — | |
| 24.1.4 | Punto de Venta (Bar Escolar) | — | — | — | — | — | |
| 24.1.5 | Productos | — | — | — | — | — | |
| 24.1.6 | Categorías y Atributos | — | — | — | — | — | |
| 24.1.7 | Inventario / Stock | — | — | — | — | — | |
| 24.1.8 | Clientes | — | — | — | — | — | |
| 24.1.9 | Reportes | — | — | — | — | — | |
| 24.1.10 | Notificaciones | — | — | — | — | — | |
| 24.1.11 | Usuarios | — | — | — | — | — | |
| 24.1.12 | Sucursales | — | — | — | — | — | |
| 24.1.13 | Configuración | — | — | — | — | — | |
| 24.1.14 | Recuperación de Contraseña | — | — | — | — | — | |

### 24.2 SuperAdmin Panel (Frontend B)

| # | Pantalla | Renderiza | Navegación OK | Tema Light | Tema Dark | Estado | Notas |
|---|----------|-----------|---------------|------------|-----------|--------|-------|
| 24.2.1 | Login SuperAdmin | — | — | — | — | — | |
| 24.2.2 | Dashboard SuperAdmin | — | — | — | — | — | |
| 24.2.3 | Gestión de Comercios | — | — | — | — | — | |
| 24.2.4 | Detalle de Comercio | — | — | — | — | — | |
| 24.2.5 | Gestión de Planes | — | — | — | — | — | |
| 24.2.6 | Logs de Auditoría | — | — | — | — | — | |

---

## 25. FLUJOS END-TO-END (Escenarios Completos)

| # | Escenario | Pasos resumidos | Estado | Notas |
|---|-----------|-----------------|--------|-------|
| 25.1 | Ciclo completo de venta normal | Login Cajero → Agregar productos → Seleccionar pago → Confirmar → Verificar stock decrementó → Verificar auditoría | — | |
| 25.2 | Venta con factura electrónica | Login Cajero → Productos → Cliente con ID → Factura Electrónica → Verificar estado SRI | — | |
| 25.3 | Venta en modo Bar Escolar | Login Cajero (comercio bar) → Clic en producto → Verificar venta qty=1, Ticket, sin cliente | — | |
| 25.4 | Crear comercio y primer venta | SuperAdmin crea comercio → Crea usuario Gerente → Gerente crea Cajero → Cajero hace venta | — | |
| 25.5 | Suspensión por mora | Simular mora → Verificar notificaciones → 3 días → Comercio suspendido → Login falla | — | |
| 25.6 | Venta con precio por volumen | Crear regla 10+ unidades → Venta con 12 unidades → Verificar PrecioEspecial aplicado | — | |
| 25.7 | Stock de ensamblado por BOM | Crear ensamblado con receta → Vender → Verificar insumos decrementan, ensamblado no | — | |
| 25.8 | Cambio de plan y efecto inmediato | Comercio en Básico → SuperAdmin cambia a Empresarial → Verificar acceso a roles nuevos | — | |
| 25.9 | Recuperación de contraseña completa | Cajero solicita → Gerente aprueba → Cajero cambia contraseña → Login exitoso | — | |
| 25.10 | Notificación de stock bajo | Vender hasta cruzar umbral → Verificar notificación creada + correo encolado | — | |

---

## Resumen de Progreso

| Sección | Total | P | F | — |
|---------|-------|---|---|---|
| 1. Autenticación y Seguridad | 21 | | | |
| 2. Control de Acceso (RBAC) | 20 | | | |
| 3. Subscription Middleware | 16 | | | |
| 4. Gestión Comercios (Admin) | 18 | | | |
| 5. Gestión Usuarios (POS) | 10 | | | |
| 6. Gestión Sucursales | 5 | | | |
| 7. Catálogo de Productos | 17 | | | |
| 8. Gestión de Stock | 12 | | | |
| 9. Registro de Ventas | 22 | | | |
| 10. Flujo Bar Escolar | 10 | | | |
| 11. Facturación SRI | 8 | | | |
| 12. Gestión Clientes | 7 | | | |
| 13. Reportería y Gráficos | 22 | | | |
| 14. Notificaciones | 7 | | | |
| 15. Ciclo Facturación | 8 | | | |
| 16. Cola de Correos | 4 | | | |
| 17. Auditoría | 9 | | | |
| 18. Aislamiento Multi-Tenant | 6 | | | |
| 19. Configuración Comercio | 6 | | | |
| 20. Interfaz Visual y UX | 19 | | | |
| 21. Seguridad Avanzada | 12 | | | |
| 22. Dashboard POS | 5 | | | |
| 23. Errores y Estados Vacíos | 15 | | | |
| 24. Checklist Visual Pantallas | 20 | | | |
| 25. Flujos End-to-End | 10 | | | |
| **TOTAL** | **329** | | | |
