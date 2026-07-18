using FluentAssertions;
using WorkspaceHub.Infrastructure.Services;
using Xunit;

namespace WorkspaceHub.Tests;

public class CalendarSyncRequestOptionsTests
{
    [Fact]
    public void InitialSync_OnlyRequestsDefaultEventsAndRecentWindow()
    {
        var now = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

        var options = CalendarSyncRequestOptions.ForInitialSync(now);

        options.SingleEvents.Should().BeTrue();
        options.EventTypes.Should().Equal(["default"]);
        options.TimeMin.Should().Be(now.AddMonths(-3));
    }

    [Fact]
    public void IncrementalSync_KeepsDefaultEventTypeFilterWithoutTimeWindow()
    {
        var options = CalendarSyncRequestOptions.ForIncrementalSync("sync-token");

        options.SyncToken.Should().Be("sync-token");
        options.SingleEvents.Should().BeTrue();
        options.EventTypes.Should().Equal(["default"]);
        options.TimeMin.Should().BeNull();
    }
}
