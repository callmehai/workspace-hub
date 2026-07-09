using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class ItemWriteBackServicePatchTicketTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<ICalendarGateway> _calendar = new();
    private readonly Mock<IDriveGateway> _drive = new();
    private readonly Mock<IJiraGateway> _jira = new();
    private readonly JiraItemMapper _jiraMapper = new();
    private readonly WriteBackGuard _guard = new(NullLogger<WriteBackGuard>.Instance); // guard thật để test conflict
    private readonly ItemWriteBackService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();
    private readonly DateTimeOffset _updated = new(2026, 6, 28, 10, 0, 0, TimeSpan.Zero);

    public ItemWriteBackServicePatchTicketTests()
    {
        _service = new ItemWriteBackService(
            _items.Object, _connections.Object, _guard,
            _gmail.Object, _calendar.Object, _drive.Object,
            _jira.Object, _jiraMapper);
    }

    private Connection JiraConn(ConnectionStatus status = ConnectionStatus.Active) =>
        new() { Id = _connId, UserId = _userId, ServiceType = ServiceType.Jira, ProviderAccountId = "cloud-1", Status = status };

    private Item TicketItem(string? etag) =>
        new() { Id = Guid.NewGuid(), UserId = _userId, ConnectionId = _connId, Type = ItemType.Ticket, ExternalId = "SCRUM-1", ETag = etag, Title = "old", Snippet = "old" };

    private JiraIssue LiveIssue(DateTimeOffset? updated, string summary = "old") =>
        new("10001", "SCRUM-1", "SCRUM", "Scrum Project", summary, null, "To Do", null, null, "High", "Task",
            "https://api.atlassian.com/ex/jira/cloud-1/browse/SCRUM-1", updated);

    private void SetupItemAndConn(Item item, Connection conn)
    {
        _items.Setup(m => m.GetByIdAndUserAsync(item.Id, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(item);
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>())).ReturnsAsync(conn);
    }

    [Fact]
    public async Task PatchTicket_UpdateSummary_CallsGatewayAndUpdatesItem()
    {
        var etag = _updated.UtcDateTime.ToString("O");
        var item = TicketItem(etag);
        SetupItemAndConn(item, JiraConn());

        // GetIssueAsync gọi 2 lần: trước (conflict check) + sau (remap). Cả 2 cùng updated → no conflict.
        _jira.Setup(m => m.GetIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LiveIssue(_updated, "new summary"));

        var result = await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(Summary: "new summary"));

        _jira.Verify(m => m.UpdateIssueAsync(It.IsAny<Connection>(), "SCRUM-1",
            It.Is<UpdateJiraIssueRequest>(r => r.Summary == "new summary"), It.IsAny<CancellationToken>()), Times.Once);
        result.Title.Should().Be("new summary");
        _items.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PatchTicket_ProviderNewer_Throws409Conflict()
    {
        var staleEtag = _updated.UtcDateTime.ToString("O");
        var item = TicketItem(staleEtag);
        SetupItemAndConn(item, JiraConn());

        // Provider updated mới hơn ETag đã lưu → conflict.
        var newerUpdated = _updated.AddHours(1);
        _jira.Setup(m => m.GetIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LiveIssue(newerUpdated));

        var act = () => _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(Summary: "x"));

        await act.Should().ThrowAsync<ConflictException>();
        _jira.Verify(m => m.UpdateIssueAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<UpdateJiraIssueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PatchTicket_NullStoredEtag_SkipsConflictCheck()
    {
        var item = TicketItem(null); // chưa có version → skip-check
        SetupItemAndConn(item, JiraConn());
        _jira.Setup(m => m.GetIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LiveIssue(_updated.AddDays(5))); // stored ETag null → updated khác cũng không conflict

        var result = await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(Summary: "y"));

        result.Should().NotBeNull();
        _jira.Verify(m => m.UpdateIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<UpdateJiraIssueRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PatchTicket_Transition_ResolvesNameToIdAndTransitions()
    {
        var item = TicketItem(null);
        SetupItemAndConn(item, JiraConn());
        _jira.Setup(m => m.GetIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LiveIssue(_updated));
        _jira.Setup(m => m.GetTransitionsAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraTransition> { new("31", "Done", "Done"), new("21", "In Progress", "In Progress") });

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(StatusTransition: "Done"));

        _jira.Verify(m => m.TransitionIssueAsync(It.IsAny<Connection>(), "SCRUM-1", "31", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PatchTicket_TransitionNotAvailable_Throws422()
    {
        var item = TicketItem(null);
        SetupItemAndConn(item, JiraConn());
        _jira.Setup(m => m.GetIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LiveIssue(_updated));
        _jira.Setup(m => m.GetTransitionsAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraTransition> { new("21", "In Progress", "In Progress") });

        var act = () => _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(StatusTransition: "Done"));

        await act.Should().ThrowAsync<BusinessRuleException>();
        _jira.Verify(m => m.TransitionIssueAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PatchTicket_Assignee_CallsAssign()
    {
        var item = TicketItem(null);
        SetupItemAndConn(item, JiraConn());
        _jira.Setup(m => m.GetIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LiveIssue(_updated));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(Assignee: "acc-123"));

        _jira.Verify(m => m.AssignIssueAsync(It.IsAny<Connection>(), "SCRUM-1", "acc-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PatchTicket_Comment_CallsAddComment()
    {
        var item = TicketItem(null);
        SetupItemAndConn(item, JiraConn());
        _jira.Setup(m => m.GetIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LiveIssue(_updated));

        await _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(Comment: "looks good"));

        _jira.Verify(m => m.AddCommentAsync(It.IsAny<Connection>(), "SCRUM-1", "looks good", It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PatchTicket_GoogleFields_ThrowsBusinessRule()
    {
        var item = TicketItem(null);
        SetupItemAndConn(item, JiraConn());
        _jira.Setup(m => m.GetIssueAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LiveIssue(_updated));

        var act = () => _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(IsStarred: true));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task PatchTicket_InactiveConnection_ThrowsBusinessRule()
    {
        var item = TicketItem(null);
        SetupItemAndConn(item, JiraConn(ConnectionStatus.Error));

        var act = () => _service.PatchItemAsync(item.Id, _userId, new PatchItemRequest(Summary: "x"));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
