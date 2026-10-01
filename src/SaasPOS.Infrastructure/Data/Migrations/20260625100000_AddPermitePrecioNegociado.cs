using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaasPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPermitePrecioNegociado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PermitePrecioNegociado",
                table: "ConfiguracionesSucursal",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermitePrecioNegociado",
                table: "ConfiguracionesSucursal");
        }
    }
}
