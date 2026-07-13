using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizerItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InviteeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InviteeItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InviteeEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    GoogleEventId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ICalUid = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GoogleSyncPending = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarInvitations_Items_InviteeItemId",
                        column: x => x.InviteeItemId,
                        principalTable: "Items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CalendarInvitations_Items_OrganizerItemId",
                        column: x => x.OrganizerItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalendarInvitations_Users_InviteeUserId",
                        column: x => x.InviteeUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CalendarInvitations_Users_OrganizerUserId",
                        column: x => x.OrganizerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarInvitations_ICalUid_InviteeEmail",
                table: "CalendarInvitations",
                columns: new[] { "ICalUid", "InviteeEmail" },
                filter: "[ICalUid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarInvitations_InviteeItemId",
                table: "CalendarInvitations",
                column: "InviteeItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarInvitations_InviteeUserId_Status_UpdatedAt",
                table: "CalendarInvitations",
                columns: new[] { "InviteeUserId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarInvitations_OrganizerItemId_InviteeEmail",
                table: "CalendarInvitations",
                columns: new[] { "OrganizerItemId", "InviteeEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarInvitations_OrganizerUserId",
                table: "CalendarInvitations",
                column: "OrganizerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarInvitations");
        }
    }
}
