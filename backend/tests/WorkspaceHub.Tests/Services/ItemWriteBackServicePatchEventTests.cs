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

public class ItemWriteBackServicePatchEventTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<ICalendarGateway> _calendar = new();
    private readonly Mock<IDriveGateway> _drive = new();
    private readonly Mock<IJiraGateway> _jira = new();
    private readonly WriteBackGuard _guard = new(NullLogger<WriteBackGuard>.Instance);
    private readonly ItemWriteBackService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();
    private readonly string _etag = "\"etag-1\"";

    private static readonly DateTimeOffset TimedStart = new(2026, 7, 6, 0, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TimedEnd = new(2026, 7, 6, 1, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AllDayStart = new(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AllDayEnd = new(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);

    public ItemWriteBackServicePatchEventTests()
    {
        _service = new ItemWriteBackService(
            _items.Object, _connections.Object, _guard,
            _gmail.Object, _calendar.Object, _drive.Object,
            _jira.Object, new JiraItemMapper());
    }

    private Connection GCalConn() =>
        new() { Id = _connId, UserId = _userId, ServiceType = ServiceType.GCal, Status = ConnectionStatus.Active };

    private Item EventItem(string metadataJson) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ConnectionId = _connId,
            Type = ItemType.Event,
            ExternalId = "google-ev-1",
            ETag = _etag,
            Title = "Meeting",
            Snippet = "notes",
            OccurredAt = AllDayStart.UtcDateTime,
            DueAt = AllDayEnd.UtcDateTime,
            MetadataJson = metadataJson,
        };

    private void SetupItemAndConn(Item item)
    {
        _items.Setup(m => m.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>())).ReturnsAsync(GCalConn());
    }

    private void SetupCalendarGetAndUpdate(CalendarEvent returnedFromUpdate)
    {
        _calendar.Setup(m => m.GetEventAsync(It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent("google-ev-1", _etag, "Meeting", "notes", AllDayStart, AllDayEnd, AllDay: true));

        _calendar.Setup(m => m.UpdateEventAsync(
                It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedFromUpdate);
    }

    [Fact]
    public async Task PatchEvent_AllDayToTimed_SendsTimedDtoToGateway()
    {
        var item = EventItem("""{"allDay":true,"start":"2026-07-06","end":"2026-07-07"}""");
        SetupItemAndConn(item);
        SetupCalendarGetAndUpdate(new CalendarEvent(
            "google-ev-1", "\"etag-2\"", "Meeting", "notes", TimedStart, TimedEnd, AllDay: false));

        CalendarEvent? captured = null;
        _calendar.Setup(m => m.UpdateEventAsync(
                It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, string, string, CalendarEvent, CancellationToken>((_, _, _, dto, _) => captured = dto)
            .ReturnsAsync(new CalendarEvent("google-ev-1", "\"etag-2\"", "Meeting", "notes", TimedStart, TimedEnd, AllDay: false));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Start: TimedStart, End: TimedEnd, AllDay: false));

        captured.Should().NotBeNull();
        captured!.AllDay.Should().BeFalse();
        captured.Start.Should().Be(TimedStart);
        captured.End.Should().Be(TimedEnd);
    }

    [Fact]
    public async Task PatchEvent_AllDayToTimed_UpdatesMetadataToIsoTimes()
    {
        var item = EventItem("""{"allDay":true,"start":"2026-07-06","end":"2026-07-07"}""");
        SetupItemAndConn(item);
        SetupCalendarGetAndUpdate(new CalendarEvent(
            "google-ev-1", "\"etag-2\"", "Meeting", "notes", TimedStart, TimedEnd, AllDay: false));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Start: TimedStart, End: TimedEnd, AllDay: false));

        var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.MetadataJson!);
        meta!.ContainsKey("allDay").Should().BeFalse();
        meta["start"].GetString().Should().Contain("T");
        meta["end"].GetString().Should().Contain("T");
    }

    [Fact]
    public async Task PatchEvent_TimedToAllDay_SendsAllDayDtoToGateway()
    {
        var item = EventItem("""
            {"start":"2026-07-06T00:30:00.0000000Z","end":"2026-07-06T01:30:00.0000000Z"}
            """);
        item.OccurredAt = TimedStart.UtcDateTime;
        item.DueAt = TimedEnd.UtcDateTime;
        SetupItemAndConn(item);

        _calendar.Setup(m => m.GetEventAsync(It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent("google-ev-1", _etag, "Meeting", null, TimedStart, TimedEnd, AllDay: false));

        CalendarEvent? captured = null;
        _calendar.Setup(m => m.UpdateEventAsync(
                It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, string, string, CalendarEvent, CancellationToken>((_, _, _, dto, _) => captured = dto)
            .ReturnsAsync(new CalendarEvent("google-ev-1", "\"etag-2\"", "Meeting", null, AllDayStart, AllDayEnd, AllDay: true));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Start: AllDayStart, End: AllDayEnd, AllDay: true));

        captured!.AllDay.Should().BeTrue();
        captured.Start.Should().Be(AllDayStart);
        captured.End.Should().Be(AllDayEnd);
    }

    [Fact]
    public async Task PatchEvent_TimedToAllDay_UpdatesMetadataToDateOnly()
    {
        var item = EventItem("""
            {"start":"2026-07-06T00:30:00.0000000Z","end":"2026-07-06T01:30:00.0000000Z"}
            """);
        item.OccurredAt = TimedStart.UtcDateTime;
        item.DueAt = TimedEnd.UtcDateTime;
        SetupItemAndConn(item);

        _calendar.Setup(m => m.GetEventAsync(It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent("google-ev-1", _etag, "Meeting", null, TimedStart, TimedEnd, AllDay: false));

        _calendar.Setup(m => m.UpdateEventAsync(It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent("google-ev-1", "\"etag-2\"", "Meeting", null, AllDayStart, AllDayEnd, AllDay: true));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Start: AllDayStart, End: AllDayEnd, AllDay: true));

        var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.MetadataJson!);
        meta!["allDay"].GetBoolean().Should().BeTrue();
        meta["start"].GetString().Should().Be("2026-07-06");
        meta["end"].GetString().Should().Be("2026-07-07");
    }

    [Fact]
    public async Task PatchEvent_TimedToTimed_UpdatesTimesWithoutChangingAllDay()
    {
        var item = EventItem("""
            {"start":"2026-07-06T00:30:00.0000000Z","end":"2026-07-06T01:30:00.0000000Z"}
            """);
        item.OccurredAt = TimedStart.UtcDateTime;
        item.DueAt = TimedEnd.UtcDateTime;
        SetupItemAndConn(item);

        var newStart = TimedStart.AddHours(2);
        var newEnd = TimedEnd.AddHours(2);

        _calendar.Setup(m => m.GetEventAsync(It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent("google-ev-1", _etag, "Meeting", null, TimedStart, TimedEnd, AllDay: false));

        CalendarEvent? captured = null;
        _calendar.Setup(m => m.UpdateEventAsync(
                It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, string, string, CalendarEvent, CancellationToken>((_, _, _, dto, _) => captured = dto)
            .ReturnsAsync(new CalendarEvent("google-ev-1", "\"etag-2\"", "Meeting", null, newStart, newEnd, AllDay: false));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Start: newStart, End: newEnd, AllDay: false));

        captured!.AllDay.Should().BeFalse();
        captured.Start.Should().Be(newStart);
        captured.End.Should().Be(newEnd);
    }

    [Fact]
    public async Task PatchEvent_AllDayToAllDay_MovesDates()
    {
        var item = EventItem("""{"allDay":true,"start":"2026-07-06","end":"2026-07-07"}""");
        SetupItemAndConn(item);

        var newStart = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero);
        var newEnd = new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero);

        SetupCalendarGetAndUpdate(new CalendarEvent(
            "google-ev-1", "\"etag-2\"", "Meeting", null, newStart, newEnd, AllDay: true));

        CalendarEvent? captured = null;
        _calendar.Setup(m => m.UpdateEventAsync(
                It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, string, string, CalendarEvent, CancellationToken>((_, _, _, dto, _) => captured = dto)
            .ReturnsAsync(new CalendarEvent("google-ev-1", "\"etag-2\"", "Meeting", null, newStart, newEnd, AllDay: true));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Start: newStart, End: newEnd, AllDay: true));

        captured!.AllDay.Should().BeTrue();
        captured.Start.Should().Be(newStart);
        captured.End.Should().Be(newEnd);
    }

    [Fact]
    public async Task PatchEvent_TitleOnly_DoesNotSendTimeChangeToGateway()
    {
        var item = EventItem("""{"allDay":true,"start":"2026-07-06","end":"2026-07-07"}""");
        SetupItemAndConn(item);
        SetupCalendarGetAndUpdate(new CalendarEvent(
            "google-ev-1", "\"etag-2\"", "New title", "notes", AllDayStart, AllDayEnd, AllDay: true));

        CalendarEvent? captured = null;
        _calendar.Setup(m => m.UpdateEventAsync(
                It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, string, string, CalendarEvent, CancellationToken>((_, _, _, dto, _) => captured = dto)
            .ReturnsAsync(new CalendarEvent("google-ev-1", "\"etag-2\"", "New title", "notes", AllDayStart, AllDayEnd, AllDay: true));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(Title: "New title"));

        captured!.AllDay.Should().BeTrue();
        captured.Start.Should().BeNull();
        captured.End.Should().BeNull();
    }

    [Fact]
    public async Task PatchEvent_AllDayFalse_SendsTimedToGateway()
    {
        var item = EventItem("""{"allDay":true,"start":"2026-07-06","end":"2026-07-07"}""");
        SetupItemAndConn(item);
        SetupCalendarGetAndUpdate(new CalendarEvent(
            "google-ev-1", "\"etag-2\"", "Meeting", null, TimedStart, TimedEnd, AllDay: false));

        CalendarEvent? captured = null;
        _calendar.Setup(m => m.UpdateEventAsync(
                It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, string, string, CalendarEvent, CancellationToken>((_, _, _, dto, _) => captured = dto)
            .ReturnsAsync(new CalendarEvent("google-ev-1", "\"etag-2\"", "Meeting", null, TimedStart, TimedEnd, AllDay: false));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Start: TimedStart, End: TimedEnd, AllDay: false));

        captured!.AllDay.Should().BeFalse();
    }

    [Fact]
    public async Task PatchEvent_RemovingLastAttendee_RemovesStaleAttendeeMetadata()
    {
        var item = EventItem("""{"allDay":true,"start":"2026-07-06","end":"2026-07-07","attendees":["guest@example.com"]}""");
        SetupItemAndConn(item);
        SetupCalendarGetAndUpdate(new CalendarEvent(
            "google-ev-1", "\"etag-2\"", "Meeting", "notes", AllDayStart, AllDayEnd,
            Attendees: null, AllDay: true));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Attendees: []));

        var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.MetadataJson!);
        meta.Should().NotBeNull();
        meta!.Should().NotContainKey("attendees");
    }

    [Fact]
    public async Task PatchEvent_DontSendGuestEmails_PassesPreferenceToGateway()
    {
        var item = EventItem("""{"allDay":true,"start":"2026-07-06","end":"2026-07-07"}""");
        SetupItemAndConn(item);

        CalendarEvent? captured = null;
        _calendar.Setup(m => m.GetEventAsync(It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent("google-ev-1", _etag, "Meeting", "notes", AllDayStart, AllDayEnd, AllDay: true));
        _calendar.Setup(m => m.UpdateEventAsync(
                It.IsAny<Connection>(), "primary", "google-ev-1", It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, string, string, CalendarEvent, CancellationToken>((_, _, _, dto, _) => captured = dto)
            .ReturnsAsync(new CalendarEvent(
                "google-ev-1", "\"etag-2\"", "Meeting", "notes", AllDayStart, AllDayEnd,
                Attendees: ["guest@example.com"], AllDay: true));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(
            Attendees: ["guest@example.com"], SendUpdates: false));

        captured.Should().NotBeNull();
        captured!.SendUpdates.Should().BeFalse();
    }
}
