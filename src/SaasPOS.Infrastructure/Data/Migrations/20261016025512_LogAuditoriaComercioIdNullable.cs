using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaasPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LogAuditoriaComercioIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LogsAuditoria_Comercios_ComercioId",
                table: "LogsAuditoria");

            migrationBuilder.AlterColumn<int>(
                name: "ComercioId",
                table: "LogsAuditoria",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_LogsAuditoria_Comercios_ComercioId",
                table: "LogsAuditoria",
                column: "ComercioId",
                principalTable: "Comercios",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LogsAuditoria_Comercios_ComercioId",
                table: "LogsAuditoria");

            migrationBuilder.AlterColumn<int>(
                name: "ComercioId",
                table: "LogsAuditoria",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LogsAuditoria_Comercios_ComercioId",
                table: "LogsAuditoria",
                column: "ComercioId",
                principalTable: "Comercios",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
