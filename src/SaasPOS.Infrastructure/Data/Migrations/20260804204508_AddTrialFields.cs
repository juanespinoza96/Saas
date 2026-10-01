using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaasPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiasExtendidos",
                table: "Suscripciones",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SuscripcionId",
                table: "Notificaciones",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_suscripciones_trial",
                table: "Suscripciones",
                columns: new[] { "Estado", "FechaProximoCorte" },
                filter: "\"Estado\" IN ('Trial', 'Trial_Expirado')");

            migrationBuilder.CreateIndex(
                name: "idx_notificaciones_suscripcion_tipo",
                table: "Notificaciones",
                columns: new[] { "SuscripcionId", "TipoNotificacion" },
                filter: "\"SuscripcionId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Notificaciones_Suscripciones_SuscripcionId",
                table: "Notificaciones",
                column: "SuscripcionId",
                principalTable: "Suscripciones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notificaciones_Suscripciones_SuscripcionId",
                table: "Notificaciones");

            migrationBuilder.DropIndex(
                name: "idx_suscripciones_trial",
                table: "Suscripciones");

            migrationBuilder.DropIndex(
                name: "idx_notificaciones_suscripcion_tipo",
                table: "Notificaciones");

            migrationBuilder.DropColumn(
                name: "DiasExtendidos",
                table: "Suscripciones");

            migrationBuilder.DropColumn(
                name: "SuscripcionId",
                table: "Notificaciones");
        }
    }
}
