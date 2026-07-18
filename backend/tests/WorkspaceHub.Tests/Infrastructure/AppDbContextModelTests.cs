using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Tests.Infrastructure;

public class AppDbContextModelTests
{
    [Fact]
    public void CalendarInvitationInviteeItem_UsesNoActionDeleteBehavior()
    {
        // SQL Server cấm SET NULL khi OrganizerItemId đã CASCADE cùng trỏ Items (Msg 1785).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new AppDbContext(options);

        var fk = db.Model.FindEntityType(typeof(CalendarInvitation))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Single().Name == nameof(CalendarInvitation.InviteeItemId));

        fk.DeleteBehavior.Should().Be(DeleteBehavior.NoAction);
    }
}
