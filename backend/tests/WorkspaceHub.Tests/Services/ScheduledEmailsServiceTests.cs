using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// CRUD email hẹn giờ (SCRUM-30). Trọng tâm: connection phải thuộc user + phải là Gmail,
/// To/Cc/Bcc/Attachments lưu dạng JSON, và quy tắc huỷ theo Status.
/// (Việc gửi thật do ProcessScheduledEmailsService lo — xem ProcessScheduledEmailsServiceTests.)
/// </summary>
public class ScheduledEmailsServiceTests
{
    private readonly Mock<IScheduledEmailRepository> _scheduledEmails = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly ScheduledEmailsService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();

    public ScheduledEmailsServiceTests()
    {
        _service = new ScheduledEmailsService(_scheduledEmails.Object, _connections.Object);
    }

    private Connection MakeConnection(Guid? ownerId = null, ServiceType serviceType = ServiceType.Gmail)
        => new()
        {
            Id = _connectionId,
            UserId = ownerId ?? _userId,
            Provider = ProviderType.Google,
            ServiceType = serviceType,
            ProviderAccountId = "me@example.com",
            Status = ConnectionStatus.Active
        };

    private CreateScheduledEmailRequest MakeRequest(DateTime? sendAt = null) => new()
    {
        ConnectionId = _connectionId,
        To = new List<string> { "a@example.com", "b@example.com" },
        Cc = new List<string> { "cc@example.com" },
        Bcc = new List<string> { "bcc@example.com" },
        Subject = "Báo cáo tuần",
        BodyHtml = "<p>Xin chào</p>",
        Attachments = new List<AttachmentUpload>
        {
            new() { Filename = "report.pdf", MimeType = "application/pdf", ContentBase64 = "AAAA" }
        },
        SendAt = sendAt ?? DateTime.UtcNow.AddHours(2)
    };

    private ScheduledEmail MakeEmail(Guid? ownerId = null, ScheduledEmailStatus status = ScheduledEmailStatus.Pending)
        => new()
        {
            Id = Guid.NewGuid(),
            UserId = ownerId ?? _userId,
            ConnectionId = _connectionId,
            ToJson = JsonSerializer.Serialize(new List<string> { "a@example.com" }),
            CcJson = "[]",
            BccJson = "[]",
            Subject = "Hi",
            BodyHtml = "<p>Hi</p>",
            SendAt = DateTime.UtcNow.AddHours(1),
            Status = status
        };

    // ───────────────────────── CreateAsync ─────────────────────────

    [Fact]
    public async Task Create_ValidGmailConnection_PersistsPendingWithJsonFields()
    {
        _connections.Setup(c => c.GetByIdTrackedAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeConnection());
        ScheduledEmail? added = null;
        _scheduledEmails.Setup(r => r.AddAsync(It.IsAny<ScheduledEmail>(), It.IsAny<CancellationToken>()))
            .Callback((ScheduledEmail e, CancellationToken _) => added = e)
            .Returns(Task.CompletedTask);
        var request = MakeRequest();

        var result = await _service.CreateAsync(_userId, request);

        added.Should().NotBeNull();
        added!.UserId.Should().Be(_userId);
        added.ConnectionId.Should().Be(_connectionId);
        added.Status.Should().Be(ScheduledEmailStatus.Pending);
        added.RetryCount.Should().Be(0);
        added.Subject.Should().Be("Báo cáo tuần");
        added.SendAt.Should().Be(request.SendAt);
        // To/Cc/Bcc/Attachments lưu JSON (SQL Server nvarchar(max)).
        JsonSerializer.Deserialize<List<string>>(added.ToJson)!.Should().Equal("a@example.com", "b@example.com");
        JsonSerializer.Deserialize<List<string>>(added.CcJson)!.Should().ContainSingle().Which.Should().Be("cc@example.com");
        JsonSerializer.Deserialize<List<string>>(added.BccJson)!.Should().ContainSingle().Which.Should().Be("bcc@example.com");
        added.AttachmentsJson.Should().Contain("report.pdf");

        result.Status.Should().Be("Pending");
        result.To.Should().Equal("a@example.com", "b@example.com");
        result.Subject.Should().Be("Báo cáo tuần");
        _scheduledEmails.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_EmptyCcBcc_SerializesToEmptyJsonArray()
    {
        _connections.Setup(c => c.GetByIdTrackedAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeConnection());
        ScheduledEmail? added = null;
        _scheduledEmails.Setup(r => r.AddAsync(It.IsAny<ScheduledEmail>(), It.IsAny<CancellationToken>()))
            .Callback((ScheduledEmail e, CancellationToken _) => added = e)
            .Returns(Task.CompletedTask);
        var request = MakeRequest();
        request.Cc = new List<string>();
        request.Bcc = new List<string>();
        request.Attachments = new List<AttachmentUpload>();

        var result = await _service.CreateAsync(_userId, request);

        added!.CcJson.Should().Be("[]");
        added.BccJson.Should().Be("[]");
        added.AttachmentsJson.Should().Be("[]");
        result.Cc.Should().BeEmpty();
        result.Bcc.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ConnectionNotFound_Throws404()
    {
        _connections.Setup(c => c.GetByIdTrackedAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Connection?)null);

        var act = () => _service.CreateAsync(_userId, MakeRequest());

        await act.Should().ThrowAsync<NotFoundException>();
        _scheduledEmails.Verify(r => r.AddAsync(It.IsAny<ScheduledEmail>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_ConnectionOfAnotherUser_Throws404()
    {
        // Không lộ sự tồn tại của connection người khác → 404 chứ không 403.
        _connections.Setup(c => c.GetByIdTrackedAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeConnection(ownerId: Guid.NewGuid()));

        var act = () => _service.CreateAsync(_userId, MakeRequest());

        await act.Should().ThrowAsync<NotFoundException>();
        _scheduledEmails.Verify(r => r.AddAsync(It.IsAny<ScheduledEmail>(), It.IsAny<CancellationToken>()), Times.Never);
        _scheduledEmails.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ServiceType.GCal)]
    [InlineData(ServiceType.Drive)]
    [InlineData(ServiceType.Jira)]
    public async Task Create_NonGmailConnection_Throws422(ServiceType serviceType)
    {
        _connections.Setup(c => c.GetByIdTrackedAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeConnection(serviceType: serviceType));

        var act = () => _service.CreateAsync(_userId, MakeRequest());

        await act.Should().ThrowAsync<BusinessRuleException>();
        _scheduledEmails.Verify(r => r.AddAsync(It.IsAny<ScheduledEmail>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_SendAtInPast_StillCreated_ValidatorOwnsThatRule()
    {
        // Service KHÔNG chặn SendAt quá khứ — quy tắc "sau hiện tại ít nhất 1 phút" nằm ở
        // CreateScheduledEmailRequestValidator (FluentValidation, trả 400 trước khi vào service).
        _connections.Setup(c => c.GetByIdTrackedAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeConnection());
        var pastSendAt = DateTime.UtcNow.AddHours(-1);

        var result = await _service.CreateAsync(_userId, MakeRequest(sendAt: pastSendAt));

        result.SendAt.Should().Be(pastSendAt);
        result.Status.Should().Be("Pending");
    }

    // ───────────────────────── GetByIdAsync ─────────────────────────

    [Fact]
    public async Task GetById_Owned_ReturnsDto()
    {
        var email = MakeEmail();
        _scheduledEmails.Setup(r => r.GetByIdAsync(email.Id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var result = await _service.GetByIdAsync(_userId, email.Id);

        result.Id.Should().Be(email.Id);
        result.Subject.Should().Be("Hi");
        result.Status.Should().Be("Pending");
        result.To.Should().ContainSingle().Which.Should().Be("a@example.com");
    }

    [Fact]
    public async Task GetById_NotFound_Throws404()
    {
        var id = Guid.NewGuid();
        _scheduledEmails.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((ScheduledEmail?)null);

        var act = () => _service.GetByIdAsync(_userId, id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetById_OwnedByAnotherUser_Throws404()
    {
        var email = MakeEmail(ownerId: Guid.NewGuid());
        _scheduledEmails.Setup(r => r.GetByIdAsync(email.Id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var act = () => _service.GetByIdAsync(_userId, email.Id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ───────────────────────── CancelAsync ─────────────────────────

    [Fact]
    public async Task Cancel_Pending_SetsCancelledAndSaves()
    {
        var email = MakeEmail();
        _scheduledEmails.Setup(r => r.GetByIdAsync(email.Id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var result = await _service.CancelAsync(_userId, email.Id);

        email.Status.Should().Be(ScheduledEmailStatus.Cancelled);
        result.Status.Should().Be("Cancelled");
        _scheduledEmails.Verify(r => r.Update(email), Times.Once);
        _scheduledEmails.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_AlreadySent_Throws422()
    {
        // Đã gửi lên Gmail rồi thì không rút lại được.
        var email = MakeEmail(status: ScheduledEmailStatus.Sent);
        _scheduledEmails.Setup(r => r.GetByIdAsync(email.Id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var act = () => _service.CancelAsync(_userId, email.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
        email.Status.Should().Be(ScheduledEmailStatus.Sent);
        _scheduledEmails.Verify(r => r.Update(It.IsAny<ScheduledEmail>()), Times.Never);
        _scheduledEmails.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Failed_Throws422()
    {
        var email = MakeEmail(status: ScheduledEmailStatus.Failed);
        _scheduledEmails.Setup(r => r.GetByIdAsync(email.Id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var act = () => _service.CancelAsync(_userId, email.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
        _scheduledEmails.Verify(r => r.Update(It.IsAny<ScheduledEmail>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_IsIdempotent()
    {
        // Huỷ 2 lần không lỗi, cũng không ghi DB thêm lần nữa.
        var email = MakeEmail(status: ScheduledEmailStatus.Cancelled);
        _scheduledEmails.Setup(r => r.GetByIdAsync(email.Id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var result = await _service.CancelAsync(_userId, email.Id);

        result.Status.Should().Be("Cancelled");
        _scheduledEmails.Verify(r => r.Update(It.IsAny<ScheduledEmail>()), Times.Never);
        _scheduledEmails.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_NotFound_Throws404()
    {
        var id = Guid.NewGuid();
        _scheduledEmails.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((ScheduledEmail?)null);

        var act = () => _service.CancelAsync(_userId, id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Cancel_OwnedByAnotherUser_Throws404()
    {
        var email = MakeEmail(ownerId: Guid.NewGuid());
        _scheduledEmails.Setup(r => r.GetByIdAsync(email.Id, It.IsAny<CancellationToken>())).ReturnsAsync(email);

        var act = () => _service.CancelAsync(_userId, email.Id);

        await act.Should().ThrowAsync<NotFoundException>();
        email.Status.Should().Be(ScheduledEmailStatus.Pending);
        _scheduledEmails.Verify(r => r.Update(It.IsAny<ScheduledEmail>()), Times.Never);
    }

    // ───────────────────────── GetByUserId ─────────────────────────

    [Fact]
    public void GetByUserId_ScopesQueryToCurrentUser()
    {
        var mine = new List<ScheduledEmailDto>
        {
            new() { Id = Guid.NewGuid(), Subject = "Của tôi", Status = "Pending", BodyHtml = "<p>x</p>" }
        }.AsQueryable();
        _scheduledEmails.Setup(r => r.GetByUserId(_userId)).Returns(mine);

        var result = _service.GetByUserId(_userId);

        // Trả thẳng IQueryable của repo (không materialize) để OData $filter/$top chạy được ở DB.
        ReferenceEquals(result, mine).Should().BeTrue();
        result.Should().ContainSingle().Which.Subject.Should().Be("Của tôi");
        // Đúng userId được truyền xuống repository (scope server-side).
        _scheduledEmails.Verify(r => r.GetByUserId(_userId), Times.Once);
        _scheduledEmails.Verify(r => r.GetByUserId(It.Is<Guid>(g => g != _userId)), Times.Never);
    }
}
