using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledEmailAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentsJson",
                table: "ScheduledEmails",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentsJson",
                table: "ScheduledEmails");
        }
    }
}
