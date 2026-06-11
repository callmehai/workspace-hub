using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateOAuthScopesSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Integrations",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "DefaultScopes",
                value: "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/drive.readonly");

            migrationBuilder.InsertData(
                table: "Integrations",
                columns: new[] { "Id", "AuthorizationEndpoint", "ClientIdEncrypted", "ClientSecretEncrypted", "DefaultScopes", "Description", "DisplayName", "IconUrl", "IsEnabled", "Key", "Provider", "SupportedServices", "TokenEndpoint" },
                values: new object[] { new Guid("22222222-2222-2222-2222-222222222222"), "https://auth.atlassian.com/authorize", "", "", "read:jira-work write:jira-work manage:jira-project read:jira-user offline_access", "Jira issues & projects", "Jira Cloud", "https://www.atlassian.com/favicon.ico", true, "jira", "Atlassian", "[\"Jira\"]", "https://auth.atlassian.com/oauth/token" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Integrations",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));

            migrationBuilder.UpdateData(
                table: "Integrations",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "DefaultScopes",
                value: "gmail.readonly calendar.readonly drive.readonly");
        }
    }
}
