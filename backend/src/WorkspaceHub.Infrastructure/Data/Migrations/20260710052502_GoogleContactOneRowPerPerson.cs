using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class GoogleContactOneRowPerPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GoogleContacts_ConnectionId_Email",
                table: "GoogleContacts");

            // Gộp row trùng ExternalResourceName (model cũ: 1 row/email) trước khi tạo unique mới.
            migrationBuilder.Sql("""
                ;WITH ranked AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY ConnectionId, ExternalResourceName
                               ORDER BY CASE WHEN Source = N'Contact' THEN 0 ELSE 1 END,
                                        COALESCE(UpdatedAt, SyncedAt) DESC,
                                        Id
                           ) AS rn
                    FROM GoogleContacts
                    WHERE ExternalResourceName IS NOT NULL AND ExternalResourceName <> N''
                )
                DELETE FROM GoogleContacts
                WHERE Id IN (SELECT Id FROM ranked WHERE rn > 1);
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "GoogleContacts",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320);

            migrationBuilder.CreateIndex(
                name: "IX_GoogleContacts_ConnectionId_Email",
                table: "GoogleContacts",
                columns: new[] { "ConnectionId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_GoogleContacts_ConnectionId_ExternalResourceName",
                table: "GoogleContacts",
                columns: new[] { "ConnectionId", "ExternalResourceName" },
                unique: true,
                filter: "[ExternalResourceName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GoogleContacts_ConnectionId_Email",
                table: "GoogleContacts");

            migrationBuilder.DropIndex(
                name: "IX_GoogleContacts_ConnectionId_ExternalResourceName",
                table: "GoogleContacts");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "GoogleContacts",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoogleContacts_ConnectionId_Email",
                table: "GoogleContacts",
                columns: new[] { "ConnectionId", "Email" },
                unique: true);
        }
    }
}
