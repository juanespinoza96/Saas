using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaasPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCobroLicenciaMensualidadYMotivoSuspension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PagosComercio_ComercioId",
                table: "PagosComercio");

            migrationBuilder.AddColumn<decimal>(
                name: "CuotaMensual",
                table: "Planes",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PlanId",
                table: "PagosComercio",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tipo",
                table: "PagosComercio",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Mensualidad");

            migrationBuilder.AddColumn<string>(
                name: "MotivoSuspension",
                table: "Comercios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // ── Relleno de datos (después de añadir las columnas) ────────────────

            // Mensualidad de cada plan por nombre. Idempotente: es un UPDATE con valor fijo.
            // Precio (licencia) no se toca.
            migrationBuilder.Sql(@"
UPDATE ""Planes""
SET ""CuotaMensual"" = CASE ""Nombre""
    WHEN 'Básico' THEN 50
    WHEN 'Intermedio' THEN 200
    WHEN 'Empresarial' THEN 420
END
WHERE ""Nombre"" IN ('Básico', 'Intermedio', 'Empresarial');
");

            // Motivo de los comercios ya suspendidos, según su suscripción más reciente:
            //   'Suspendido'     -> 'Mora'
            //   'Trial_Expirado' -> 'TrialExpirado'
            //   cualquier otro (o sin suscripción) -> 'Administrativa'
            // Solo se rellenan los que aún no tienen motivo, para poder reaplicar sin pisar datos.
            migrationBuilder.Sql(@"
UPDATE ""Comercios"" c
SET ""MotivoSuspension"" = CASE
    WHEN s.""Estado"" = 'Suspendido' THEN 'Mora'
    WHEN s.""Estado"" = 'Trial_Expirado' THEN 'TrialExpirado'
    ELSE 'Administrativa'
END
FROM ""Comercios"" c2
LEFT JOIN LATERAL (
    SELECT ""Estado""
    FROM ""Suscripciones""
    WHERE ""ComercioId"" = c2.""Id""
    ORDER BY ""FechaInicio"" DESC, ""Id"" DESC
    LIMIT 1
) s ON TRUE
WHERE c.""Id"" = c2.""Id""
  AND c.""Estado"" = 'Suspendido'
  AND c.""MotivoSuspension"" IS NULL;
");

            migrationBuilder.CreateIndex(
                name: "IX_PagosComercio_PlanId",
                table: "PagosComercio",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "ux_pagoscomercio_licencia",
                table: "PagosComercio",
                column: "ComercioId",
                unique: true,
                filter: "\"Tipo\" = 'Licencia'");

            migrationBuilder.AddForeignKey(
                name: "FK_PagosComercio_Planes_PlanId",
                table: "PagosComercio",
                column: "PlanId",
                principalTable: "Planes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PagosComercio_Planes_PlanId",
                table: "PagosComercio");

            migrationBuilder.DropIndex(
                name: "IX_PagosComercio_PlanId",
                table: "PagosComercio");

            migrationBuilder.DropIndex(
                name: "ux_pagoscomercio_licencia",
                table: "PagosComercio");

            migrationBuilder.DropColumn(
                name: "CuotaMensual",
                table: "Planes");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "PagosComercio");

            migrationBuilder.DropColumn(
                name: "Tipo",
                table: "PagosComercio");

            migrationBuilder.DropColumn(
                name: "MotivoSuspension",
                table: "Comercios");

            migrationBuilder.CreateIndex(
                name: "IX_PagosComercio_ComercioId",
                table: "PagosComercio",
                column: "ComercioId");
        }
    }
}
