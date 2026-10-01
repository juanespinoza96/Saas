using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SaasPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTimezoneColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ZonaHoraria",
                table: "Usuarios",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StockMinimo",
                table: "StockSucursal",
                type: "numeric(10,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "LimiteSucursales",
                table: "Planes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Comercios",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Activo");

            migrationBuilder.AddColumn<string>(
                name: "ZonaHorariaDefecto",
                table: "Comercios",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConfiguracionesSucursal",
                columns: table => new
                {
                    SucursalId = table.Column<int>(type: "integer", nullable: false),
                    EsBarEscolar = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MostrarBotonCliente = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PermiteVentaEnNegativo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ImpresionAutomaticaTicket = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    PermitePrecioNegociado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesSucursal", x => x.SucursalId);
                    table.ForeignKey(
                        name: "FK_ConfiguracionesSucursal_Sucursales_SucursalId",
                        column: x => x.SucursalId,
                        principalTable: "Sucursales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SolicitudesRecuperacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    ComercioId = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pendiente"),
                    AprobadoPor = table.Column<int>(type: "integer", nullable: true),
                    FechaSolicitud = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaResolucion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesRecuperacion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolicitudesRecuperacion_Comercios_ComercioId",
                        column: x => x.ComercioId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SolicitudesRecuperacion_Usuarios_AprobadoPor",
                        column: x => x.AprobadoPor,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SolicitudesRecuperacion_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_solicitudes_recuperacion_comercio",
                table: "SolicitudesRecuperacion",
                column: "ComercioId");

            migrationBuilder.CreateIndex(
                name: "idx_solicitudes_recuperacion_estado",
                table: "SolicitudesRecuperacion",
                column: "Estado",
                filter: "\"Estado\" = 'Pendiente'");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesRecuperacion_AprobadoPor",
                table: "SolicitudesRecuperacion",
                column: "AprobadoPor");

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesRecuperacion_UsuarioId",
                table: "SolicitudesRecuperacion",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesSucursal");

            migrationBuilder.DropTable(
                name: "SolicitudesRecuperacion");

            migrationBuilder.DropColumn(
                name: "ZonaHoraria",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "StockMinimo",
                table: "StockSucursal");

            migrationBuilder.DropColumn(
                name: "LimiteSucursales",
                table: "Planes");

            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Comercios");

            migrationBuilder.DropColumn(
                name: "ZonaHorariaDefecto",
                table: "Comercios");
        }
    }
}
