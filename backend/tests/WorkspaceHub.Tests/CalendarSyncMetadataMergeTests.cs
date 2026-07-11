using FluentAssertions;
using WorkspaceHub.Application.Mapping;
using Xunit;

namespace WorkspaceHub.Tests;

public class CalendarSyncMetadataMergeTests
{
    [Fact]
    public void MergeForUpdate_PreservesCalendarTypeFromExisting()
    {
        var existing = """{"calendarType":"task","location":"Old"}""";
        var synced = """{"location":"New","driveAttachments":[{"fileId":"f1"}]}""";

        var merged = CalendarSyncMetadataMerge.MergeForUpdate(existing, synced);

        merged.Should().Contain("\"calendarType\":\"task\"");
        merged.Should().Contain("\"location\":\"New\"");
        merged.Should().Contain("driveAttachments");
    }

    [Fact]
    public void MergeForUpdate_UsesSyncedWhenNoExisting()
    {
        var synced = """{"allDay":true,"start":"2026-07-10"}""";

        var merged = CalendarSyncMetadataMerge.MergeForUpdate(null, synced);

        merged.Should().Be(synced);
    }
}
