using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleContactMetadataJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "GoogleContacts",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "GoogleContacts");
        }
    }
}
