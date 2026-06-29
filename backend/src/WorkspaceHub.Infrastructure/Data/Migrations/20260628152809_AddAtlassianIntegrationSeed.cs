using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAtlassianIntegrationSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Integrations",
                columns: new[] { "Id", "AuthorizationEndpoint", "Description", "DisplayName", "IconUrl", "IsEnabled", "Key", "Provider", "SupportedServices", "TokenEndpoint" },
                values: new object[] { new Guid("22222222-2222-2222-2222-222222222222"), "https://auth.atlassian.com/authorize", "Jira", "Atlassian Jira", "https://www.atlassian.com/favicon.ico", false, "atlassian", "Atlassian", "[\"Jira\"]", "https://auth.atlassian.com/oauth/token" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Integrations",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));
        }
    }
}
