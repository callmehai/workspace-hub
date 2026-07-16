using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CalendarInvitationInviteeItemSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarInvitations_Items_InviteeItemId",
                table: "CalendarInvitations");

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarInvitations_Items_InviteeItemId",
                table: "CalendarInvitations",
                column: "InviteeItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarInvitations_Items_InviteeItemId",
                table: "CalendarInvitations");

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarInvitations_Items_InviteeItemId",
                table: "CalendarInvitations",
                column: "InviteeItemId",
                principalTable: "Items",
                principalColumn: "Id");
        }
    }
}
