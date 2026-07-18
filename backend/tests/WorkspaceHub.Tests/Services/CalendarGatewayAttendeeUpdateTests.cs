using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class CalendarGatewayAttendeeUpdateTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<ICalendarGateway> _calendar = new();
    private readonly Mock<IDriveGateway> _drive = new();
    private readonly Mock<IJiraGateway> _jira = new();
    private readonly ItemWriteBackService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();

    private static readonly DateTimeOffset Start = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    public CalendarGatewayAttendeeUpdateTests()
    {
        _service = new ItemWriteBackService(
            _items.Object,
            _connections.Object,
            new WriteBackGuard(NullLogger<WriteBackGuard>.Instance),
            _gmail.Object,
            _calendar.Object,
            _drive.Object,
            _jira.Object,
            new JiraItemMapper());
    }

    public static IEnumerable<object[]> EditGuestCases()
    {
        yield return new object[]
        {
            "add guest",
            new[] { "old@example.com" },
            new[] { "old@example.com", "new@example.com" }
        };
        yield return new object[]
        {
            "remove one guest",
            new[] { "old@example.com", "new@example.com" },
            new[] { "new@example.com" }
        };
        yield return new object[]
        {
            "replace guests",
            new[] { "old@example.com" },
            new[] { "replacement@example.com" }
        };
        yield return new object[]
        {
            "remove all guests",
            new[] { "old@example.com" },
            Array.Empty<string>()
        };
    }

    [Theory]
    [MemberData(nameof(EditGuestCases))]
    public async Task PatchEvent_GuestListChanges_SendExactListToCalendarGateway(
        string _,
        string[] existingGuests,
        string[] nextGuests)
    {
        var item = EventItem(existingGuests);
        SetupItemAndConnection(item);

        CalendarEvent? captured = null;
        _calendar.Setup(m => m.GetEventAsync(It.IsAny<Connection>(), "primary", item.ExternalId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent(
                item.ExternalId!,
                item.ETag,
                item.Title,
                item.Snippet,
                Start,
                End,
                Attendees: existingGuests,
                AllDay: false,
                OrganizerEmail: "owner@example.com"));

        _calendar.Setup(m => m.UpdateEventAsync(
                It.IsAny<Connection>(),
                "primary",
                item.ExternalId!,
                It.IsAny<CalendarEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<Connection, string, string, CalendarEvent, CancellationToken>((_, _, _, dto, _) => captured = dto)
            .ReturnsAsync((Connection _, string _, string _, CalendarEvent dto, CancellationToken _) => new CalendarEvent(
                item.ExternalId!,
                "\"etag-2\"",
                dto.Summary ?? item.Title,
                dto.Description ?? item.Snippet,
                dto.Start ?? Start,
                dto.End ?? End,
                dto.Location,
                dto.Attendees,
                dto.AllDay,
                OrganizerEmail: "owner@example.com",
                GuestsCanModify: dto.GuestsCanModify,
                GuestsCanInviteOthers: dto.GuestsCanInviteOthers,
                GuestsCanSeeOtherGuests: dto.GuestsCanSeeOtherGuests));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Attendees: nextGuests.ToList(),
            SendUpdates: true));

        captured.Should().NotBeNull();
        captured!.Attendees.Should().Equal(nextGuests);
        captured.SendUpdates.Should().BeTrue();

        var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.MetadataJson!);
        metadata.Should().NotBeNull();
        if (nextGuests.Length == 0)
        {
            metadata!.Should().NotContainKey("attendees");
        }
        else
        {
            metadata!["attendees"].EnumerateArray().Select(x => x.GetString()).Should().Equal(nextGuests);
        }
    }

    [Fact]
    public async Task CreateEvent_WithGuests_SendsGuestListAndSendUpdatesToCalendarGateway()
    {
        SetupConnection();
        Item? addedItem = null;
        CalendarEvent? captured = null;
        var guests = new[] { "first@example.com", "second@example.com" };

        _items.Setup(m => m.AddAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback<Item, CancellationToken>((item, _) => addedItem = item)
            .Returns(Task.CompletedTask);

        _calendar.Setup(m => m.InsertEventAsync(
                It.IsAny<Connection>(),
                "primary",
                It.IsAny<CalendarEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<Connection, string, CalendarEvent, CancellationToken>((_, _, dto, _) => captured = dto)
            .ReturnsAsync((Connection _, string _, CalendarEvent dto, CancellationToken _) => new CalendarEvent(
                "google-event-1",
                "\"etag-created\"",
                dto.Summary,
                dto.Description,
                dto.Start,
                dto.End,
                dto.Location,
                dto.Attendees,
                dto.AllDay,
                OrganizerEmail: "owner@example.com",
                GuestsCanModify: dto.GuestsCanModify,
                GuestsCanInviteOthers: dto.GuestsCanInviteOthers,
                GuestsCanSeeOtherGuests: dto.GuestsCanSeeOtherGuests));

        await _service.CreateEventAsync(_userId, new CreateEventRequest(
            ConnectionId: _connectionId,
            Title: "Guest flow",
            Start: Start,
            End: End,
            Attendees: guests.ToList(),
            SendUpdates: false));

        captured.Should().NotBeNull();
        captured!.Attendees.Should().Equal(guests);
        captured.SendUpdates.Should().BeFalse();

        addedItem.Should().NotBeNull();
        var metadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(addedItem!.MetadataJson!);
        metadata!["attendees"].EnumerateArray().Select(x => x.GetString()).Should().Equal(guests);
    }

    private Item EventItem(IReadOnlyCollection<string> attendees)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["start"] = Start.UtcDateTime.ToString("o"),
            ["end"] = End.UtcDateTime.ToString("o"),
            ["organizerEmail"] = "owner@example.com",
            ["guestsCanModify"] = false,
            ["guestsCanInviteOthers"] = true,
            ["guestsCanSeeOtherGuests"] = true,
        };

        if (attendees.Count > 0)
            metadata["attendees"] = attendees;

        return new Item
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ConnectionId = _connectionId,
            Type = ItemType.Event,
            ExternalId = "google-event-1",
            ETag = "\"etag-1\"",
            Title = "Guest flow",
            Snippet = "",
            OccurredAt = Start.UtcDateTime,
            DueAt = End.UtcDateTime,
            MetadataJson = JsonSerializer.Serialize(metadata),
        };
    }

    private void SetupItemAndConnection(Item item)
    {
        SetupConnection();
        _items.Setup(m => m.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
    }

    private void SetupConnection()
    {
        _connections.Setup(m => m.GetByIdAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = _connectionId,
                UserId = _userId,
                ServiceType = ServiceType.GCal,
                Status = ConnectionStatus.Active,
                ProviderAccountId = "owner@example.com"
            });
    }
}
