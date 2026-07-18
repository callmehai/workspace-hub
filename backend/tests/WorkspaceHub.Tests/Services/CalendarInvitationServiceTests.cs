using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class CalendarInvitationServiceTests
{
    private readonly Mock<ICalendarInvitationRepository> _invitations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<ICalendarGateway> _calendar = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly CalendarInvitationService _sut;

    private readonly Guid _inviteeUserId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();
    private readonly Guid _invitationId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();

    public CalendarInvitationServiceTests()
    {
        _sut = new CalendarInvitationService(
            _invitations.Object,
            _users.Object,
            _connections.Object,
            _calendar.Object,
            _notifications.Object);
    }

    [Fact]
    public async Task ReconcileSyncedEvent_WhenRsvpPushFails_KeepsGoogleSyncPendingAndLocalStatus()
    {
        var invitation = new CalendarInvitation
        {
            Id = _invitationId,
            InviteeUserId = _inviteeUserId,
            InviteeEmail = "guest@example.com",
            ICalUid = "uid-1",
            GoogleEventId = "g-ev-1",
            Status = CalendarInvitationStatus.Accepted,
            GoogleSyncPending = true,
            InviteeItemId = null
        };

        _invitations.Setup(m => m.GetByICalUidAndEmailAsync("uid-1", "guest@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _calendar.Setup(m => m.RsvpEventAsync(
                It.IsAny<Connection>(), "primary", "gcal-ev-local",
                "accepted", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("Google Calendar temporary failure"));

        var connection = new Connection
        {
            Id = _connId,
            UserId = _inviteeUserId,
            ServiceType = ServiceType.GCal,
            Status = ConnectionStatus.Active,
            ProviderAccountId = "guest@example.com"
        };

        var localItem = new Item
        {
            Id = _itemId,
            UserId = _inviteeUserId,
            Type = ItemType.Event,
            ExternalId = "gcal-ev-local",
            ConnectionId = _connId,
            Title = "Meeting"
        };

        var googleEvent = new CalendarEventDto
        {
            Id = "gcal-ev-local",
            ICalUid = "uid-1",
            OrganizerEmail = "organizer@example.com",
            SelfResponseStatus = "needsAction",
            Title = "Meeting"
        };

        await _sut.ReconcileSyncedEventAsync(connection, localItem, googleEvent);

        invitation.GoogleSyncPending.Should().BeTrue();
        invitation.Status.Should().Be(CalendarInvitationStatus.Accepted);
        invitation.InviteeItemId.Should().Be(_itemId);
        _invitations.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // Must not fall through to overwrite Status from Google needsAction
        invitation.Status.Should().NotBe(CalendarInvitationStatus.NeedsAction);
    }

    [Fact]
    public async Task ReconcileOrganizerEvent_WhenGoogleStillReturnsNeedsActionAfterLocalRsvp_KeepsLocalResponse()
    {
        var organizerItem = new Item
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = ItemType.Event,
            ExternalId = "org-event-1",
            Title = "Meeting"
        };
        var invitation = new CalendarInvitation
        {
            Id = _invitationId,
            OrganizerItemId = organizerItem.Id,
            OrganizerUserId = organizerItem.UserId,
            InviteeUserId = _inviteeUserId,
            InviteeEmail = "guest@example.com",
            GoogleEventId = "org-event-1",
            Status = CalendarInvitationStatus.Accepted,
            GoogleSyncPending = false,
            RespondedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _invitations.Setup(m => m.GetByOrganizerItemAsync(organizerItem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInvitation> { invitation });
        _users.Setup(m => m.GetByEmailsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>
            {
                new()
                {
                    Id = _inviteeUserId,
                    Email = "guest@example.com",
                    IsActive = true
                }
            });

        var googleEvent = new CalendarEvent(
            Id: "org-event-1",
            ETag: null,
            Summary: "Meeting",
            Description: null,
            Start: DateTimeOffset.UtcNow,
            End: DateTimeOffset.UtcNow.AddHours(1),
            Attendees: null,
            FullAttendees:
            [
                new CalendarEventAttendee("guest@example.com", null, "needsAction", null, false)
            ]);

        await _sut.ReconcileOrganizerEventAsync(organizerItem, googleEvent);

        invitation.Status.Should().Be(CalendarInvitationStatus.Accepted);
        invitation.GoogleSyncPending.Should().BeFalse();
        invitation.RespondedAt.Should().NotBeNull();
    }
}
