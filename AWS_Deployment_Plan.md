# Plan de Despliegue en AWS — SaaS POS Multipropósito

## Resumen de Viabilidad

El stack actual (ASP.NET Core 9 + PostgreSQL + React SPAs) es **100% compatible con AWS** y es una de las opciones más naturales para llevarlo a producción. No hay incompatibilidades. .NET 9 tiene soporte de primera clase en AWS, PostgreSQL en RDS es managed y battle-tested, y los SPAs en S3+CloudFront son la forma estándar de servir frontends.

---

## Arquitectura Recomendada en AWS

| Componente del Sistema | Servicio AWS | Notas |
|---|---|---|
| **API (.NET 9)** | ECS Fargate o App Runner | Contenedores sin gestionar servidores. App Runner es más simple; ECS da más control |
| **Base de datos (PostgreSQL)** | RDS PostgreSQL o Aurora PostgreSQL | Managed, backups automáticos, réplicas de lectura |
| **Frontends (React SPAs)** | S3 + CloudFront | Hosting estático con CDN global, muy económico |
| **Dominio y DNS** | Route 53 | Gestión de dominios y routing |
| **HTTPS/SSL** | ACM (Certificate Manager) | Certificados gratuitos, renovación automática |
| **Secretos** | Secrets Manager o Parameter Store | Para JWT keys, connection strings, credenciales SRI |
| **Cache distribuido** | ElastiCache (Redis) | Si se necesita mover IMemoryCache a cache distribuido |
| **Emails** | SES (Simple Email Service) | Para el EmailProcessorBackgroundService |
| **Almacenamiento de archivos** | S3 | Comprobantes XML firmados, logos de comercios |
| **Logs y monitoreo** | CloudWatch | Logs centralizados, métricas, alarmas |
| **CI/CD** | CodePipeline + CodeBuild o GitHub Actions | Deploy automatizado |
| **Firewall** | WAF (Web Application Firewall) | Protección frente a CloudFront |

---

## Aspectos Clave a Tener en Cuenta

### 1. Contenedorización

- Se necesita un `Dockerfile` para la API .NET 9 (multi-stage build).
- Los frontends (SaasPOS.Web y SaasPOS.Admin) se despliegan como archivos estáticos generados por `npm run build`, no necesitan contenedor.
- Imagen base recomendada: `mcr.microsoft.com/dotnet/aspnet:9.0` (runtime) y `mcr.microsoft.com/dotnet/sdk:9.0` (build stage).

### 2. Base de Datos

- RDS PostgreSQL 15+ soporta todo lo que usa el sistema (JSONB, etc.).
- Configurar backups automáticos y Multi-AZ para alta disponibilidad.
- Migrar el schema desde `BaseData_Completa.sql` o EF Core migrations.
- La DB **NO debe estar expuesta a internet** — solo accesible desde la VPC interna.

### 3. Multi-tenancy y Seguridad

- El aislamiento por `ComercioId` funciona igual en AWS.
- Security Groups y VPC para aislar red.
- WAF frente a CloudFront para protección adicional contra ataques comunes (SQL injection, XSS, etc.).
- El middleware pipeline existente (IP blocking, rate limiting, JWT validation) se mantiene sin cambios.

### 4. Variables de Entorno y Secretos

- Las connection strings, JWT secrets, credenciales SRI van en **Secrets Manager**.
- Nunca en el código ni en variables de entorno planas.
- ECS Fargate puede inyectar secretos desde Secrets Manager directamente en las variables de entorno del contenedor.

### 5. Background Services

- `BillingCutBackgroundService` y `EmailProcessorBackgroundService` corren dentro del contenedor de la API.
- Alternativa más robusta para producción: moverlos a tareas ECS separadas o Lambda + EventBridge para scheduling.
- Consideración: si se escala horizontalmente la API, los background services deben ejecutarse en una sola instancia (o usar un lock distribuido).

### 6. Integración SRI

- La firma digital (AES-256) y envío a SRI funcionan igual desde AWS.
- El key store debe estar en Secrets Manager o S3 con encriptación server-side (SSE-KMS).
- Verificar que las IPs de salida de AWS no estén bloqueadas por el SRI (usar NAT Gateway con IP elástica fija si es necesario).

### 7. Rate Limiting

- El rate limiting de ASP.NET Core funciona correctamente en un solo contenedor.
- Si se escala a múltiples instancias, se necesita rate limiting distribuido (Redis/ElastiCache + librería compatible).
- Lo mismo aplica para el JTI blocklist e IP blocking que actualmente usan `IMemoryCache`.

### 8. CORS

- Configurar CORS en la API para permitir requests desde los dominios de CloudFront donde se sirvan los frontends.
- En producción, restringir a dominios específicos (no usar `*`).

---

## Estimación de Costos

### Producción Básica (1 instancia, tráfico bajo-medio)

| Recurso | Costo mensual aprox. (USD) |
|---|---|
| ECS Fargate (1 task, 0.5 vCPU, 1GB RAM) | ~$15-25 |
| RDS PostgreSQL (db.t3.micro, single-AZ) | ~$15-20 |
| S3 + CloudFront (2 frontends) | ~$1-5 |
| Route 53 (1 hosted zone) | ~$0.50 |
| ACM (SSL certificates) | Gratis |
| SES (emails transaccionales) | ~$0.10/1000 emails |
| Secrets Manager (5-10 secretos) | ~$2-3 |
| CloudWatch (logs básicos) | ~$3-5 |
| **Total mínimo** | **~$35-60/mes** |

### Producción con Alta Disponibilidad

| Recurso | Costo mensual aprox. (USD) |
|---|---|
| ECS Fargate (2+ tasks, auto-scaling) | ~$50-80 |
| RDS PostgreSQL (db.t3.small, Multi-AZ) | ~$50-70 |
| ElastiCache Redis (cache.t3.micro) | ~$15-20 |
| ALB (Application Load Balancer) | ~$20-25 |
| NAT Gateway | ~$35-45 |
| WAF | ~$5-10 |
| S3 + CloudFront | ~$3-10 |
| Otros (Route 53, ACM, SES, CloudWatch) | ~$10-15 |
| **Total HA** | **~$190-275/mes** |

---

## Pasos para Llegar a Producción

### Fase 1: Preparación (Local)
1. Crear `Dockerfile` multi-stage para la API .NET 9
2. Crear `docker-compose.yml` para validar que todo funciona contenedorizado
3. Configurar la API para leer secretos desde variables de entorno
4. Hacer build de los frontends y verificar que funcionan como archivos estáticos

### Fase 2: Infraestructura AWS
5. Configurar VPC (subnets públicas y privadas, NAT Gateway)
6. Crear Security Groups (API → DB, ALB → API, internet → ALB)
7. Crear RDS PostgreSQL y migrar schema
8. Crear bucket S3 + distribución CloudFront para cada frontend
9. Configurar dominio en Route 53 con certificados SSL (ACM)

### Fase 3: Despliegue
10. Subir imagen Docker a ECR (Elastic Container Registry)
11. Crear servicio ECS Fargate con task definition
12. Configurar ALB apuntando al servicio ECS
13. Deploy de archivos estáticos de frontends a S3
14. Configurar variables de entorno y secretos en ECS

### Fase 4: CI/CD
15. Configurar pipeline (GitHub Actions o CodePipeline)
16. Automatizar: push → build → test → deploy

### Fase 5: Monitoreo y Operaciones
17. Configurar CloudWatch Logs y métricas
18. Crear alarmas (CPU, memoria, errores 5xx, latencia)
19. Configurar health checks en el ALB
20. Plan de backups y disaster recovery

---

## Consideraciones Especiales para Ecuador / SRI

- **Latencia**: La región AWS más cercana a Ecuador es `us-east-1` (Virginia) o `sa-east-1` (São Paulo). São Paulo tiene menor latencia para usuarios ecuatorianos.
- **IP fija para SRI**: Si el SRI requiere IP de origen conocida, usar NAT Gateway con Elastic IP.
- **Almacenamiento de comprobantes**: Los XMLs firmados y autorizados deben conservarse por el tiempo que exija la normativa tributaria (mínimo 7 años).
- **Disponibilidad**: Considerar que los servicios del SRI tienen ventanas de mantenimiento — implementar reintentos y cola de envío.

---

## Diagrama de Arquitectura (Alto Nivel)

```
                    ┌─────────────────────────────────────────────────┐
                    │                   Internet                       │
                    └─────────────────┬───────────────────────────────┘
                                      │
                         ┌────────────┼────────────┐
                         │            │            │
                         ▼            ▼            ▼
                   ┌──────────┐ ┌──────────┐ ┌──────────┐
                   │Route 53  │ │CloudFront│ │CloudFront│
                   │  (DNS)   │ │(POS SPA) │ │(Admin)   │
                   └────┬─────┘ └────┬─────┘ └────┬─────┘
                        │            │            │
                        │            ▼            ▼
                        │       ┌─────────┐ ┌─────────┐
                        │       │  S3     │ │  S3     │
                        │       │(Web)    │ │(Admin)  │
                        │       └─────────┘ └─────────┘
                        │
                        ▼
              ┌───────────────────┐
              │   ALB + WAF       │
              │(Application LB)   │
              └────────┬──────────┘
                       │
            ┌──────────┼──────────┐
            │     VPC Privada     │
            │          │          │
            │          ▼          │
            │  ┌──────────────┐   │
            │  │  ECS Fargate │   │
            │  │  (.NET 9 API)│   │
            │  └──────┬───────┘   │
            │         │           │
            │    ┌────┼────┐      │
            │    │         │      │
            │    ▼         ▼      │
            │ ┌──────┐ ┌───────┐  │
            │ │ RDS  │ │Secrets│  │
            │ │(PgSQL)│ │Manager│  │
            │ └──────┘ └───────┘  │
            │                     │
            └─────────────────────┘
```

---

## Alternativas Evaluadas

| Alternativa | Pros | Contras |
|---|---|---|
| **Azure App Service** | Excelente integración con .NET | Menos presencia en LATAM, costos similares |
| **DigitalOcean** | Más simple, precios predecibles | Menos servicios managed, sin presencia LATAM |
| **Railway / Render** | Deploy ultra-simple | Menos control, puede ser caro al escalar |
| **AWS (seleccionado)** | Ecosistema completo, región São Paulo, maduro | Curva de aprendizaje más alta |

---

## Decisiones Pendientes

- [ ] Región AWS: `us-east-1` vs `sa-east-1` (evaluar latencia y costos)
- [ ] ECS Fargate vs App Runner (simplicidad vs control)
- [ ] IaC: Terraform vs AWS CDK vs CloudFormation
- [ ] CI/CD: GitHub Actions vs AWS CodePipeline
- [ ] ¿Migrar IMemoryCache a Redis desde el inicio o solo al escalar?
- [ ] ¿Background services dentro del contenedor API o separados?
- [ ] Estrategia de migración de DB (EF migrations vs script SQL directo)
