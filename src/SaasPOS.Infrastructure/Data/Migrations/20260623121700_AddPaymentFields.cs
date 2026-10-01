using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaasPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetodoPago",
                table: "Ventas",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Efectivo");

            migrationBuilder.AddColumn<int>(
                name: "CuotasMeses",
                table: "Ventas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorCuota",
                table: "Ventas",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenciaTransaccion",
                table: "Ventas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetodoPago",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "CuotasMeses",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "ValorCuota",
                table: "Ventas");

            migrationBuilder.DropColumn(
                name: "ReferenciaTransaccion",
                table: "Ventas");
        }
    }
}
