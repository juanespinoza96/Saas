using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SaasPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguracionesComprobante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Agregar campo MostrarVentasAlCajero a ConfiguracionesSucursal
            migrationBuilder.AddColumn<bool>(
                name: "MostrarVentasAlCajero",
                table: "ConfiguracionesSucursal",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Crear tabla ConfiguracionesComprobante
            migrationBuilder.CreateTable(
                name: "ConfiguracionesComprobante",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ComercioId = table.Column<int>(type: "integer", nullable: false),
                    TipoComprobante = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Habilitado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesComprobante", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracionesComprobante_Comercios_ComercioId",
                        column: x => x.ComercioId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Índice único: un comercio no puede tener duplicados del mismo tipo
            migrationBuilder.CreateIndex(
                name: "ix_configuraciones_comprobante_comercio_tipo",
                table: "ConfiguracionesComprobante",
                columns: new[] { "ComercioId", "TipoComprobante" },
                unique: true);

            // Seed: crear configuración de Ticket Digital (habilitado) para todos los comercios existentes
            migrationBuilder.Sql(@"
                INSERT INTO ""ConfiguracionesComprobante"" (""ComercioId"", ""TipoComprobante"", ""Habilitado"", ""FechaCreacion"")
                SELECT c.""Id"", 'Ticket Digital', TRUE, NOW()
                FROM ""Comercios"" c;
            ");

            // Seed: crear configuración de Ticket Impreso (deshabilitado) para todos los comercios existentes
            migrationBuilder.Sql(@"
                INSERT INTO ""ConfiguracionesComprobante"" (""ComercioId"", ""TipoComprobante"", ""Habilitado"", ""FechaCreacion"")
                SELECT c.""Id"", 'Ticket Impreso', FALSE, NOW()
                FROM ""Comercios"" c;
            ");

            // Seed: crear configuración de Factura Electrónica (habilitada solo si UsaFacturacionSRI = true)
            migrationBuilder.Sql(@"
                INSERT INTO ""ConfiguracionesComprobante"" (""ComercioId"", ""TipoComprobante"", ""Habilitado"", ""FechaCreacion"")
                SELECT c.""Id"", 'Factura Electrónica',
                    CASE WHEN c.""UsaFacturacionSRI"" = TRUE THEN TRUE ELSE FALSE END,
                    NOW()
                FROM ""Comercios"" c;
            ");

            // Actualizar valores legacy en Ventas existentes: 'Ticket Interno' → 'Ticket Digital'
            migrationBuilder.Sql(@"
                UPDATE ""Ventas""
                SET ""TipoComprobante"" = 'Ticket Digital'
                WHERE ""TipoComprobante"" = 'Ticket Interno';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revertir el cambio en Ventas (restaurar valor legacy)
            migrationBuilder.Sql(@"
                UPDATE ""Ventas""
                SET ""TipoComprobante"" = 'Ticket Interno'
                WHERE ""TipoComprobante"" = 'Ticket Digital';
            ");

            migrationBuilder.DropTable(
                name: "ConfiguracionesComprobante");

            migrationBuilder.DropColumn(
                name: "MostrarVentasAlCajero",
                table: "ConfiguracionesSucursal");
        }
    }
}
