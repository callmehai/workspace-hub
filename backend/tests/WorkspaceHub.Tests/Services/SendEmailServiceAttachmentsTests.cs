using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Tests.Services;

/// <summary>Kiểm tra Send giải mã base64 attachment và truyền đúng binary xuống Gmail gateway.</summary>
public class SendEmailServiceAttachmentsTests
{
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<IGoogleContactRepository> _googleContacts = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly GoogleContactMapper _mapper = new();
    private readonly Mock<ILogger<SendEmailService>> _logger = new();
    private readonly SendEmailService _service;

    public SendEmailServiceAttachmentsTests()
    {
        _service = new SendEmailService(_connections.Object, _gmail.Object, _googleContacts.Object, _mapper, _items.Object, _logger.Object);
    }

    private void SetupGmail(Guid userId, Guid connId)
    {
        _connections.Setup(m => m.GetByIdTrackedAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = connId,
                UserId = userId,
                ProviderAccountId = "me@gmail.com",
                ServiceType = ServiceType.Gmail,
                Status = ConnectionStatus.Active
            });
    }

    [Fact]
    public async Task SendAsync_DecodesBase64Attachment_AndPassesBinaryToGateway()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmail(userId, connId);

        var raw = Encoding.UTF8.GetBytes("hello world");
        IReadOnlyList<GmailAttachmentData>? captured = null;
        _gmail.Setup(g => g.SendMessageAsync(
                It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, string, string, IReadOnlyList<GmailAttachmentData>?, CancellationToken>(
                (_, _, _, _, _, _, atts, _) => captured = atts)
            .ReturnsAsync("msg-1");

        var request = new SendEmailRequest
        {
            ConnectionId = connId,
            To = new List<string> { "bob@gmail.com" },
            Subject = "hi",
            BodyHtml = "<p>hi</p>",
            Attachments = new List<AttachmentUpload>
            {
                new() { Filename = "note.txt", MimeType = "text/plain", ContentBase64 = Convert.ToBase64String(raw) }
            }
        };

        var result = await _service.SendAsync(userId, request);

        result.MessageId.Should().Be("msg-1");
        captured.Should().NotBeNull();
        captured!.Should().HaveCount(1);
        captured![0].Filename.Should().Be("note.txt");
        captured[0].MimeType.Should().Be("text/plain");
        captured[0].Data.Should().BeEquivalentTo(raw);
    }

    [Fact]
    public async Task SendAsync_NoAttachments_PassesNullToGateway()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmail(userId, connId);

        IReadOnlyList<GmailAttachmentData>? captured = new List<GmailAttachmentData>();
        _gmail.Setup(g => g.SendMessageAsync(
                It.IsAny<Connection>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<GmailAttachmentData>?>(), It.IsAny<CancellationToken>()))
            .Callback<Connection, IReadOnlyList<string>, IReadOnlyList<string>, IReadOnlyList<string>, string, string, IReadOnlyList<GmailAttachmentData>?, CancellationToken>(
                (_, _, _, _, _, _, atts, _) => captured = atts)
            .ReturnsAsync("msg-2");

        var request = new SendEmailRequest
        {
            ConnectionId = connId,
            To = new List<string> { "bob@gmail.com" },
            Subject = "hi",
            BodyHtml = "<p>hi</p>"
        };

        await _service.SendAsync(userId, request);

        captured.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_InvalidBase64_ThrowsBusinessRule()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmail(userId, connId);

        var request = new SendEmailRequest
        {
            ConnectionId = connId,
            To = new List<string> { "bob@gmail.com" },
            Subject = "hi",
            BodyHtml = "<p>hi</p>",
            Attachments = new List<AttachmentUpload>
            {
                new() { Filename = "bad.bin", MimeType = "application/octet-stream", ContentBase64 = "!!!not-base64!!!" }
            }
        };

        var act = () => _service.SendAsync(userId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
