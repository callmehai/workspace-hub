using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceEventReminderTypeWithChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO [EventReminders] (Id, EventItemId, ReminderType, OffsetValue, OffsetUnit, TimeOfDay, IsSent, CreatedAt)
                SELECT NEWID(), EventItemId, 'InApp', OffsetValue, OffsetUnit, TimeOfDay, 0, CreatedAt
                FROM [EventReminders]
                WHERE [ReminderType] = 'Both'
                """);

            migrationBuilder.Sql("""
                UPDATE [EventReminders]
                SET [ReminderType] = CASE [ReminderType]
                    WHEN 'Email' THEN 'GoogleEmail'
                    WHEN 'Both' THEN 'GooglePopup'
                    WHEN 'Notification' THEN 'InApp'
                    ELSE 'InApp'
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [EventReminders]
                SET [ReminderType] = CASE [ReminderType]
                    WHEN 'GoogleEmail' THEN 'Email'
                    WHEN 'GooglePopup' THEN 'Notification'
                    WHEN 'InApp' THEN 'Notification'
                    ELSE 'Notification'
                END
                """);
        }
    }
}
