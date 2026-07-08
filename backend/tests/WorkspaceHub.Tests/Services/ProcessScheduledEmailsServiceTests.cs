using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class ProcessScheduledEmailsServiceTests
{
    private readonly Mock<IScheduledEmailRepository> _scheduledEmails = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly ProcessScheduledEmailsService _service;

    public ProcessScheduledEmailsServiceTests()
    {
        _service = new ProcessScheduledEmailsService(
            _scheduledEmails.Object,
            _connections.Object,
            _gmail.Object,
            NullLogger<ProcessScheduledEmailsService>.Instance);
    }

    private static ScheduledEmail MakeEmail(Guid connectionId)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ConnectionId = connectionId,
            ToJson = JsonSerializer.Serialize(new List<string> { "to@example.com" }),
            CcJson = "[]",
            BccJson = "[]",
            Subject = "Hello",
            BodyHtml = "<p>Hi</p>",
            SendAt = DateTime.UtcNow.AddMinutes(-1),
            Status = ScheduledEmailStatus.Pending
        };

    private static Connection MakeGmailConnection(Guid id)
        => new()
        {
            Id = id,
            ServiceType = ServiceType.Gmail,
            Status = ConnectionStatus.Active,
            ProviderAccountId = "me@example.com"
        };

    private void SetupDue(params ScheduledEmail[] emails)
        => _scheduledEmails
            .Setup(r => r.GetPendingDueEmailsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emails);

    [Fact]
    public async Task NoDueEmails_DoesNothing()
    {
        SetupDue();

        var result = await _service.ProcessDueEmailsAsync();

        result.Total.Should().Be(0);
        result.Sent.Should().Be(0);
        result.Failed.Should().Be(0);
        _gmail.Verify(g => g.SendMessageAsync(It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()), Times.Never);
        _scheduledEmails.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DueEmail_SentSuccessfully_MarksSent()
    {
        var connId = Guid.NewGuid();
        var email = MakeEmail(connId);
        SetupDue(email);
        _connections.Setup(c => c.GetByIdTrackedAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeGmailConnection(connId));
        _gmail.Setup(g => g.SendMessageAsync(It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("gmail-msg-id");

        var result = await _service.ProcessDueEmailsAsync();

        result.Total.Should().Be(1);
        result.Sent.Should().Be(1);
        result.Failed.Should().Be(0);
        email.Status.Should().Be(ScheduledEmailStatus.Sent);
        email.SentAt.Should().NotBeNull();
        email.LastError.Should().BeNull();
        _scheduledEmails.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GatewayThrows_MarksFailed_AndIncrementsRetry()
    {
        var connId = Guid.NewGuid();
        var email = MakeEmail(connId);
        SetupDue(email);
        _connections.Setup(c => c.GetByIdTrackedAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeGmailConnection(connId));
        _gmail.Setup(g => g.SendMessageAsync(It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("gmail down"));

        var result = await _service.ProcessDueEmailsAsync();

        result.Failed.Should().Be(1);
        result.Sent.Should().Be(0);
        email.Status.Should().Be(ScheduledEmailStatus.Failed);
        email.RetryCount.Should().Be(1);
        email.LastError.Should().Be("gmail down");
        _scheduledEmails.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectionMissing_MarksFailed_WithoutSending()
    {
        var connId = Guid.NewGuid();
        var email = MakeEmail(connId);
        SetupDue(email);
        _connections.Setup(c => c.GetByIdTrackedAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        var result = await _service.ProcessDueEmailsAsync();

        result.Failed.Should().Be(1);
        email.Status.Should().Be(ScheduledEmailStatus.Failed);
        _gmail.Verify(g => g.SendMessageAsync(It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NonGmailConnection_MarksFailed_WithoutSending()
    {
        var connId = Guid.NewGuid();
        var email = MakeEmail(connId);
        SetupDue(email);
        _connections.Setup(c => c.GetByIdTrackedAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection { Id = connId, ServiceType = ServiceType.GCal, Status = ConnectionStatus.Active });

        var result = await _service.ProcessDueEmailsAsync();

        result.Failed.Should().Be(1);
        email.Status.Should().Be(ScheduledEmailStatus.Failed);
        email.LastError.Should().Contain("not a Gmail");
        _gmail.Verify(g => g.SendMessageAsync(It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InactiveConnection_MarksFailed_WithoutSending()
    {
        var connId = Guid.NewGuid();
        var email = MakeEmail(connId);
        SetupDue(email);
        _connections.Setup(c => c.GetByIdTrackedAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection { Id = connId, ServiceType = ServiceType.Gmail, Status = ConnectionStatus.Error });

        var result = await _service.ProcessDueEmailsAsync();

        result.Failed.Should().Be(1);
        email.Status.Should().Be(ScheduledEmailStatus.Failed);
        email.LastError.Should().Contain("not active");
    }
}
