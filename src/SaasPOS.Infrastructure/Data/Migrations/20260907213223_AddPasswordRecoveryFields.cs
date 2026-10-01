using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaasPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordRecoveryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DebeCambiarPassword",
                table: "Usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaExpiracion",
                table: "SolicitudesRecuperacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoRechazo",
                table: "SolicitudesRecuperacion",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordTemporalCifrada",
                table: "SolicitudesRecuperacion",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DebeCambiarPassword",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "FechaExpiracion",
                table: "SolicitudesRecuperacion");

            migrationBuilder.DropColumn(
                name: "MotivoRechazo",
                table: "SolicitudesRecuperacion");

            migrationBuilder.DropColumn(
                name: "PasswordTemporalCifrada",
                table: "SolicitudesRecuperacion");
        }
    }
}
