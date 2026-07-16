using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkspaceHub.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePhoneVerifiedToEmailVerified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SCRUM-64: OTP đăng ký chuyển từ SMS/phone sang email.
            // ⚠️ QUAN TRỌNG (review): PHẢI là RenameColumn (đã verify EF sinh đúng ở đây) —
            // KHÔNG được để thành DropColumn("PhoneVerified") + AddColumn("EmailVerified", default true),
            // vì như vậy mọi user đang PhoneVerified=false (đăng ký chưa verify) sẽ bị set thành true
            // → bypass thẳng cổng verify. RenameColumn giữ nguyên giá trị verify hiện có.
            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "PhoneVerified",
                table: "Users",
                newName: "EmailVerified");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EmailVerified",
                table: "Users",
                newName: "PhoneVerified");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Users",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
