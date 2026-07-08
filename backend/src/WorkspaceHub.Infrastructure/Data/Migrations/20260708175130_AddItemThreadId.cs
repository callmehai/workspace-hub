using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddItemThreadId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThreadId",
                table: "Items",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_User_ThreadId",
                table: "Items",
                columns: new[] { "UserId", "ThreadId" },
                filter: "[ThreadId] IS NOT NULL");

            // Backfill: rút threadId từ MetadataJson cho các Email item đã sync trước đây.
            migrationBuilder.Sql(
                "UPDATE Items SET ThreadId = JSON_VALUE(MetadataJson, '$.threadId') " +
                "WHERE Type = 'Email' AND ThreadId IS NULL " +
                "AND ISJSON(MetadataJson) = 1 AND JSON_VALUE(MetadataJson, '$.threadId') IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_User_ThreadId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ThreadId",
                table: "Items");
        }
    }
}
