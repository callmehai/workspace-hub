using FluentAssertions;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests;

public class CalendarItemMapperTests
{
    private readonly CalendarItemMapper _mapper = new();

    [Fact]
    public void ToItem_MapsStartEndEtagAndAllDay()
    {
        var start = new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);
        var dto = new CalendarEventDto
        {
            Id = "ev-1",
            ETag = "\"etag-abc\"",
            Title = "Team sync",
            Snippet = "Agenda notes",
            Start = start,
            End = end,
            AllDay = true,
            Location = "Room A",
            Attendees = ["a@test.com"],
            HtmlLink = "https://calendar.google.com/event?eid=1",
        };

        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        var item = _mapper.ToItem(dto, userId, connId);

        item.Type.Should().Be(ItemType.Event);
        item.ExternalId.Should().Be("ev-1");
        item.ETag.Should().Be("\"etag-abc\"");
        item.OccurredAt.Should().Be(start.UtcDateTime);
        item.DueAt.Should().Be(end.UtcDateTime);
        item.MetadataJson.Should().Contain("\"start\":\"2026-07-05\"");
        item.MetadataJson.Should().Contain("\"end\":\"2026-07-06\"");
        item.MetadataJson.Should().Contain("\"allDay\":true");
        item.MetadataJson.Should().Contain("\"location\":\"Room A\"");
    }

    [Fact]
    public void ToItem_MapsDriveAttachmentsAndMatchedDriveItemIds()
    {
        var driveItemId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var dto = new CalendarEventDto
        {
            Id = "ev-2",
            Title = "With files",
            DriveAttachments =
            [
                new CalendarDriveAttachment("google-file-1", "doc.pdf", "application/pdf", "https://drive.google.com/file/d/google-file-1"),
                new CalendarDriveAttachment("unknown-file", "other.pdf", "application/pdf", null),
            ],
        };

        var lookup = new Dictionary<string, Guid> { ["google-file-1"] = driveItemId };
        var item = _mapper.ToItem(dto, Guid.NewGuid(), Guid.NewGuid(), lookup);

        item.MetadataJson.Should().Contain("driveAttachments");
        item.MetadataJson.Should().Contain("google-file-1");
        item.MetadataJson.Should().Contain("doc.pdf");
        item.MetadataJson.Should().Contain("\"driveItemIds\":[\"" + driveItemId + "\"]");
        item.MetadataJson.Should().NotContain("\"driveItemIds\":[]");
    }

    [Fact]
    public void ToItem_DoesNotWriteDriveItemIdsWhenLookupMissing()
    {
        var dto = new CalendarEventDto
        {
            Id = "ev-3",
            Title = "Attach only",
            DriveAttachments =
            [
                new CalendarDriveAttachment("google-file-1", "doc.pdf", "application/pdf", null),
            ],
        };

        var item = _mapper.ToItem(dto, Guid.NewGuid(), Guid.NewGuid());

        item.MetadataJson.Should().Contain("driveAttachments");
        item.MetadataJson.Should().NotContain("driveItemIds");
    }
}
