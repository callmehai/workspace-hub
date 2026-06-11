using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ModelBConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mô hình B (SCRUM-34): gộp OAuthConnections + ServiceConnections → Connections.
            // Thứ tự: tạo bảng mới → copy data → dọn orphan → drop bảng cũ → rename FK column.
            migrationBuilder.CreateTable(
                name: "Connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ServiceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProviderAccountId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AccessTokenEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CursorType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CursorValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Connections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Connections_Integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "Integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Connections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_IntegrationId",
                table: "Connections",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_UserId_Provider_ServiceType_ProviderAccountId",
                table: "Connections",
                columns: new[] { "UserId", "Provider", "ServiceType", "ProviderAccountId" },
                unique: true);

            // Copy data: mỗi ServiceConnection đang bật → 1 row Connections, token lấy từ grant cha.
            // Tái dùng ServiceConnections.Id làm Connections.Id → Items/ScheduledEmails giữ nguyên giá trị FK.
            migrationBuilder.Sql("""
                INSERT INTO [Connections] (
                    [Id], [UserId], [IntegrationId], [Provider], [ServiceType], [ProviderAccountId],
                    [AccessTokenEncrypted], [RefreshTokenEncrypted], [ExpiresAt], [Status],
                    [CursorType], [CursorValue], [LastSyncedAt], [LastError], [CreatedAt])
                SELECT
                    sc.[Id], oc.[UserId], oc.[IntegrationId], i.[Provider], sc.[ServiceType], oc.[ProviderAccountId],
                    oc.[AccessTokenEncrypted], oc.[RefreshTokenEncrypted], oc.[ExpiresAt], oc.[Status],
                    sc.[CursorType], sc.[CursorValue], sc.[LastSyncedAt], sc.[LastError], SYSUTCDATETIME()
                FROM [ServiceConnections] sc
                INNER JOIN [OAuthConnections] oc ON oc.[Id] = sc.[OAuthConnectionId]
                INNER JOIN [Integrations] i ON i.[Id] = oc.[IntegrationId]
                WHERE sc.[IsEnabled] = 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Items_ServiceConnections_ServiceConnectionId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_ScheduledEmails_ServiceConnections_ServiceConnectionId",
                table: "ScheduledEmails");

            // Dọn orphan: row trỏ tới ServiceConnection bị tắt (IsEnabled = 0, không copy sang Connections).
            // Mô hình B: service tắt = không có row. Items giữ lại history với ConnectionId NULL.
            migrationBuilder.Sql("""
                UPDATE [Items] SET [ServiceConnectionId] = NULL
                WHERE [ServiceConnectionId] IS NOT NULL
                  AND [ServiceConnectionId] NOT IN (SELECT [Id] FROM [Connections]);
                """);

            migrationBuilder.Sql("""
                DELETE FROM [ScheduledEmails]
                WHERE [ServiceConnectionId] NOT IN (SELECT [Id] FROM [Connections]);
                """);

            migrationBuilder.DropTable(
                name: "ServiceConnections");

            migrationBuilder.DropTable(
                name: "OAuthConnections");

            migrationBuilder.DropColumn(
                name: "DefaultScopes",
                table: "Integrations");

            migrationBuilder.RenameColumn(
                name: "ServiceConnectionId",
                table: "ScheduledEmails",
                newName: "ConnectionId");

            migrationBuilder.RenameIndex(
                name: "IX_ScheduledEmails_ServiceConnectionId",
                table: "ScheduledEmails",
                newName: "IX_ScheduledEmails_ConnectionId");

            migrationBuilder.RenameColumn(
                name: "ServiceConnectionId",
                table: "Items",
                newName: "ConnectionId");

            migrationBuilder.RenameIndex(
                name: "IX_Items_ServiceConnectionId_ExternalId",
                table: "Items",
                newName: "IX_Items_ConnectionId_ExternalId");

            migrationBuilder.AddColumn<string>(
                name: "ETag",
                table: "Items",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Connections_ConnectionId",
                table: "Items",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduledEmails_Connections_ConnectionId",
                table: "ScheduledEmails",
                column: "ConnectionId",
                principalTable: "Connections",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ⚠️ DESTRUCTIVE — best-effort rollback: tạo lại schema cũ nhưng KHÔNG khôi phục data.
            // Down sẽ XOÁ toàn bộ Connections, set NULL mọi Items.ConnectionId và DELETE HẾT ScheduledEmails
            // (2 bảng cũ recreate rỗng nên FK bắt buộc phải dọn). TUYỆT ĐỐI không chạy trên môi trường có data thật.
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Connections_ConnectionId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_ScheduledEmails_Connections_ConnectionId",
                table: "ScheduledEmails");

            migrationBuilder.DropTable(
                name: "Connections");

            migrationBuilder.DropColumn(
                name: "ETag",
                table: "Items");

            migrationBuilder.RenameColumn(
                name: "ConnectionId",
                table: "ScheduledEmails",
                newName: "ServiceConnectionId");

            migrationBuilder.RenameIndex(
                name: "IX_ScheduledEmails_ConnectionId",
                table: "ScheduledEmails",
                newName: "IX_ScheduledEmails_ServiceConnectionId");

            migrationBuilder.RenameColumn(
                name: "ConnectionId",
                table: "Items",
                newName: "ServiceConnectionId");

            migrationBuilder.RenameIndex(
                name: "IX_Items_ConnectionId_ExternalId",
                table: "Items",
                newName: "IX_Items_ServiceConnectionId_ExternalId");

            migrationBuilder.AddColumn<string>(
                name: "DefaultScopes",
                table: "Integrations",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "OAuthConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessTokenEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRefreshedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProviderAccountId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Scopes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OAuthConnections_Integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "Integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OAuthConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OAuthConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CursorType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CursorValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ServiceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceConnections_OAuthConnections_OAuthConnectionId",
                        column: x => x.OAuthConnectionId,
                        principalTable: "OAuthConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Integrations",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "DefaultScopes",
                value: "gmail.readonly calendar.readonly drive.readonly");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthConnections_IntegrationId",
                table: "OAuthConnections",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthConnections_UserId_IntegrationId_ProviderAccountId",
                table: "OAuthConnections",
                columns: new[] { "UserId", "IntegrationId", "ProviderAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceConnections_OAuthConnectionId",
                table: "ServiceConnections",
                column: "OAuthConnectionId");

            migrationBuilder.Sql("UPDATE [Items] SET [ServiceConnectionId] = NULL WHERE [ServiceConnectionId] IS NOT NULL;");

            migrationBuilder.Sql("DELETE FROM [ScheduledEmails];");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_ServiceConnections_ServiceConnectionId",
                table: "Items",
                column: "ServiceConnectionId",
                principalTable: "ServiceConnections",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduledEmails_ServiceConnections_ServiceConnectionId",
                table: "ScheduledEmails",
                column: "ServiceConnectionId",
                principalTable: "ServiceConnections",
                principalColumn: "Id");
        }
    }
}
