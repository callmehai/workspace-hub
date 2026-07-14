using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Friends;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class FriendServiceTests
{
    private readonly Mock<IFriendshipRepository> _friendships = new();
    private readonly Mock<IFriendInviteRepository> _invites = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<ISendEmailService> _sendEmail = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ILogger<FriendService>> _logger = new();
    private readonly FriendService _service;

    private static readonly User Me = new()
    {
        Id = Guid.NewGuid(), Email = "me@test.com", FullName = "Me", CreatedAt = DateTime.UtcNow
    };
    private static readonly User Other = new()
    {
        Id = Guid.NewGuid(), Email = "other@test.com", FullName = "Other", CreatedAt = DateTime.UtcNow
    };

    public FriendServiceTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:FrontendBaseUrl"] = "https://app.test"
        }).Build();

        _users.Setup(u => u.GetByIdAsync(Me.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Me);
        _users.Setup(u => u.GetByEmailAsync(Other.Email, It.IsAny<CancellationToken>())).ReturnsAsync(Other);

        _service = new FriendService(
            _friendships.Object, _invites.Object, _users.Object, _connections.Object,
            _sendEmail.Object, _notifications.Object, config, _logger.Object);
    }

    // ── SendRequestAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SendRequest_ExistingUser_CreatesPendingAndNotifies()
    {
        _friendships.Setup(f => f.GetBetweenAsync(Me.Id, Other.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Friendship?)null);

        var result = await _service.SendRequestAsync(Me.Id, new SendFriendRequestRequest(Other.Email));

        result.Outcome.Should().Be("RequestSent");
        result.Friend!.Status.Should().Be("Pending");
        result.Friend.IsIncoming.Should().BeFalse();
        result.Friend.Email.Should().Be(Other.Email);
        _friendships.Verify(f => f.AddAsync(
            It.Is<Friendship>(x => x.RequesterId == Me.Id && x.AddresseeId == Other.Id && x.Status == FriendshipStatus.Pending),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAndSendAsync(
            Other.Id, NotificationType.FriendRequest, "notifications.friendRequest",
            It.IsAny<string>(), "/friends", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendRequest_ReversePendingExists_AutoAccepts()
    {
        var reverse = new Friendship
        {
            Id = Guid.NewGuid(), RequesterId = Other.Id, AddresseeId = Me.Id,
            Status = FriendshipStatus.Pending, Requester = Other, Addressee = Me, CreatedAt = DateTime.UtcNow
        };
        _friendships.Setup(f => f.GetBetweenAsync(Me.Id, Other.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reverse);
        _friendships.Setup(f => f.GetByIdWithUsersAsync(reverse.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reverse);

        var result = await _service.SendRequestAsync(Me.Id, new SendFriendRequestRequest(Other.Email));

        result.Outcome.Should().Be("AutoAccepted");
        reverse.Status.Should().Be(FriendshipStatus.Accepted);
        reverse.RespondedAt.Should().NotBeNull();
        _notifications.Verify(n => n.CreateAndSendAsync(
            Other.Id, NotificationType.FriendAccepted, "notifications.friendAccepted",
            It.IsAny<string>(), "/friends", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendRequest_ToSelf_ThrowsBusinessRule()
    {
        var act = () => _service.SendRequestAsync(Me.Id, new SendFriendRequestRequest(Me.Email));
        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendRequest_AlreadyFriends_ThrowsConflict()
    {
        _friendships.Setup(f => f.GetBetweenAsync(Me.Id, Other.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Friendship { Status = FriendshipStatus.Accepted });

        var act = () => _service.SendRequestAsync(Me.Id, new SendFriendRequestRequest(Other.Email));
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task SendRequest_UnknownEmail_CreatesInviteAndSendsMailViaGmail()
    {
        var gmailConn = new Connection
        {
            Id = Guid.NewGuid(), UserId = Me.Id, ServiceType = ServiceType.Gmail,
            Status = ConnectionStatus.Active, ProviderAccountId = Me.Email
        };
        _connections.Setup(c => c.GetByUserIdAsync(Me.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection> { gmailConn });
        _invites.Setup(i => i.GetActiveAsync(Me.Id, "newbie@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FriendInvite?)null);

        var result = await _service.SendRequestAsync(Me.Id, new SendFriendRequestRequest("Newbie@Test.com"));

        result.Outcome.Should().Be("InviteCreated");
        result.EmailSent.Should().BeTrue();
        result.Invite!.Email.Should().Be("newbie@test.com");
        result.Invite.InviteLink.Should().StartWith("https://app.test/register?inviteToken=");
        _invites.Verify(i => i.AddAsync(
            It.Is<FriendInvite>(x => x.Email == "newbie@test.com" && x.InviterUserId == Me.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        _sendEmail.Verify(s => s.SendAsync(Me.Id,
            It.Is<WorkspaceHub.Application.DTOs.Emails.SendEmailRequest>(r =>
                r.ConnectionId == gmailConn.Id && r.To.Contains("newbie@test.com")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendRequest_UnknownEmail_NoGmailConnection_StillReturnsLink()
    {
        _connections.Setup(c => c.GetByUserIdAsync(Me.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Connection>());
        _invites.Setup(i => i.GetActiveAsync(Me.Id, "newbie@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FriendInvite?)null);

        var result = await _service.SendRequestAsync(Me.Id, new SendFriendRequestRequest("newbie@test.com"));

        result.Outcome.Should().Be("InviteCreated");
        result.EmailSent.Should().BeFalse();
        result.Invite!.InviteLink.Should().Contain("inviteToken=");
    }

    // ── AcceptAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task Accept_ByAddressee_AcceptsAndNotifiesRequester()
    {
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(), RequesterId = Other.Id, AddresseeId = Me.Id,
            Status = FriendshipStatus.Pending, Requester = Other, Addressee = Me, CreatedAt = DateTime.UtcNow
        };
        _friendships.Setup(f => f.GetByIdWithUsersAsync(friendship.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(friendship);

        var dto = await _service.AcceptAsync(Me.Id, friendship.Id);

        dto.Status.Should().Be("Accepted");
        friendship.Status.Should().Be(FriendshipStatus.Accepted);
        _notifications.Verify(n => n.CreateAndSendAsync(
            Other.Id, NotificationType.FriendAccepted, It.IsAny<string>(),
            It.IsAny<string>(), "/friends", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Accept_ByRequester_ThrowsForbidden()
    {
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(), RequesterId = Me.Id, AddresseeId = Other.Id,
            Status = FriendshipStatus.Pending, Requester = Me, Addressee = Other, CreatedAt = DateTime.UtcNow
        };
        _friendships.Setup(f => f.GetByIdWithUsersAsync(friendship.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(friendship);

        var act = () => _service.AcceptAsync(Me.Id, friendship.Id);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Accept_ByOutsider_ThrowsNotFound()
    {
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(), RequesterId = Other.Id, AddresseeId = Guid.NewGuid(),
            Status = FriendshipStatus.Pending, Requester = Other, Addressee = Other, CreatedAt = DateTime.UtcNow
        };
        _friendships.Setup(f => f.GetByIdWithUsersAsync(friendship.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(friendship);

        var act = () => _service.AcceptAsync(Me.Id, friendship.Id);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── SetTierAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SetTier_OnlyAffectsCallerSide()
    {
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(), RequesterId = Me.Id, AddresseeId = Other.Id,
            Status = FriendshipStatus.Accepted, Requester = Me, Addressee = Other, CreatedAt = DateTime.UtcNow
        };
        _friendships.Setup(f => f.GetByIdWithUsersAsync(friendship.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(friendship);

        var dto = await _service.SetTierAsync(Me.Id, friendship.Id, new UpdateFriendTierRequest("CloseFriend"));

        dto.MyTier.Should().Be("CloseFriend");
        friendship.RequesterTier.Should().Be(FriendTier.CloseFriend);
        friendship.AddresseeTier.Should().Be(FriendTier.Friend);
    }

    // ── ConsumeInvitesOnRegistrationAsync ─────────────────────────────

    [Fact]
    public async Task ConsumeInvites_TokenMatch_CreatesAcceptedFriendship()
    {
        var newUserId = Guid.NewGuid();
        var invite = new FriendInvite
        {
            Id = Guid.NewGuid(), InviterUserId = Other.Id, Email = "new@test.com",
            Token = "tok123", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7), Inviter = Other
        };
        _invites.Setup(i => i.GetPendingByEmailAsync("new@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FriendInvite> { invite });
        _users.Setup(u => u.GetByIdAsync(newUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = newUserId, Email = "new@test.com", FullName = "New" });
        _friendships.Setup(f => f.GetBetweenAsync(Other.Id, newUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Friendship?)null);

        await _service.ConsumeInvitesOnRegistrationAsync(newUserId, "new@test.com", "tok123");

        invite.ConsumedAt.Should().NotBeNull();
        _friendships.Verify(f => f.AddAsync(
            It.Is<Friendship>(x => x.RequesterId == Other.Id && x.AddresseeId == newUserId && x.Status == FriendshipStatus.Accepted),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAndSendAsync(
            Other.Id, NotificationType.FriendAccepted, It.IsAny<string>(),
            It.IsAny<string>(), "/friends", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsumeInvites_NoToken_CreatesPendingFriendship()
    {
        var newUserId = Guid.NewGuid();
        var invite = new FriendInvite
        {
            Id = Guid.NewGuid(), InviterUserId = Other.Id, Email = "new@test.com",
            Token = "tok123", CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(7), Inviter = Other
        };
        _invites.Setup(i => i.GetPendingByEmailAsync("new@test.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FriendInvite> { invite });
        _friendships.Setup(f => f.GetBetweenAsync(Other.Id, newUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Friendship?)null);

        await _service.ConsumeInvitesOnRegistrationAsync(newUserId, "new@test.com", inviteToken: null);

        _friendships.Verify(f => f.AddAsync(
            It.Is<Friendship>(x => x.Status == FriendshipStatus.Pending && x.AddresseeId == newUserId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsumeInvites_RepositoryThrows_DoesNotPropagate()
    {
        _invites.Setup(i => i.GetPendingByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var act = () => _service.ConsumeInvitesOnRegistrationAsync(Guid.NewGuid(), "x@test.com", null);
        await act.Should().NotThrowAsync();
    }
}
