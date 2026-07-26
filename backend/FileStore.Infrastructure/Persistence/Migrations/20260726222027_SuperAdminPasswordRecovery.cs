using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuperAdminPasswordRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PasswordResetTokens_Clients_ClientId",
                table: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_ClientId",
                table: "PasswordResetTokens");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                table: "PasswordResetTokens",
                newName: "UserId");

            // 1 = UserType.Client. Todas las filas que existan son de clientes:
            // el super-admin no tenia recuperacion hasta esta migracion. El 0 que
            // genera EF por defecto no corresponde a ningun valor del enum.
            migrationBuilder.AddColumn<int>(
                name: "UserType",
                table: "PasswordResetTokens",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId_UserType",
                table: "PasswordResetTokens",
                columns: new[] { "UserId", "UserType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_UserId_UserType",
                table: "PasswordResetTokens");

            migrationBuilder.DropColumn(
                name: "UserType",
                table: "PasswordResetTokens");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "PasswordResetTokens",
                newName: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_ClientId",
                table: "PasswordResetTokens",
                column: "ClientId");

            migrationBuilder.AddForeignKey(
                name: "FK_PasswordResetTokens_Clients_ClientId",
                table: "PasswordResetTokens",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
