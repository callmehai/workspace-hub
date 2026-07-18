using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// No-op: tên migration nói SetNull nhưng FK InviteeItemId vẫn NoAction.
    /// SQL Server Msg 1785 cấm SET NULL khi OrganizerItemId đã CASCADE cùng trỏ Items.
    /// Null hoá InviteeItemId ở service/repo trước khi xoá Item — xem CHANGELOG [2026-07-18].
    /// </remarks>
    public partial class CalendarInvitationInviteeItemSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop + Add lại FK không truyền onDelete (= NoAction mặc định). Schema không đổi.
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
