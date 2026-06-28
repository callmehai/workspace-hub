using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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

public class ItemWriteBackServiceCreateTicketTests
{
    private readonly Mock<IItemRepository> _items = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IWriteBackGuard> _guard = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<ICalendarGateway> _calendar = new();
    private readonly Mock<IDriveGateway> _drive = new();
    private readonly Mock<IJiraGateway> _jira = new();
    private readonly JiraItemMapper _jiraMapper = new(); // dùng mapper thật để verify mapping
    private readonly ItemWriteBackService _service;

    private readonly Guid _userId = Guid.NewGuid();

    public ItemWriteBackServiceCreateTicketTests()
    {
        _service = new ItemWriteBackService(
            _items.Object, _connections.Object, _guard.Object,
            _gmail.Object, _calendar.Object, _drive.Object,
            _jira.Object, _jiraMapper);
    }

    private Connection JiraConn(ConnectionStatus status = ConnectionStatus.Active) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            ServiceType = ServiceType.Jira,
            ProviderAccountId = "cloud-1",
            Status = status
        };

    private static CreateTicketRequest Request(Guid connId) =>
        new(connId, "SCRUM", "Task", "Fix the bug", "Some description");

    private static JiraIssue CreatedIssue(string id, string key) =>
        new(id, key, "SCRUM", "Fix the bug", null, "To Do", null, "High", "Task",
            $"https://api.atlassian.com/ex/jira/cloud-1/browse/{key}",
            new DateTimeOffset(2026, 6, 28, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CreateTicket_HappyPath_CreatesItemTypeTicket()
    {
        var conn = JiraConn();
        _connections.Setup(m => m.GetByIdAsync(conn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conn);
        _jira.Setup(m => m.CreateIssueAsync(conn, It.IsAny<CreateJiraIssueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraCreatedIssue("10001", "SCRUM-42"));
        _jira.Setup(m => m.GetIssueAsync(conn, "SCRUM-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatedIssue("10001", "SCRUM-42"));

        Item? added = null;
        _items.Setup(m => m.AddAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()))
            .Callback((Item i, CancellationToken _) => added = i)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateTicketAsync(_userId, Request(conn.Id));

        result.Type.Should().Be(ItemType.Ticket);
        result.Title.Should().Be("Fix the bug");
        result.ExternalId.Should().Be("10001");

        added.Should().NotBeNull();
        added!.UserId.Should().Be(_userId);
        added.ConnectionId.Should().Be(conn.Id);
        added.MetadataJson.Should().Contain("SCRUM-42");

        _items.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTicket_PassesPayloadToGateway()
    {
        var conn = JiraConn();
        _connections.Setup(m => m.GetByIdAsync(conn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conn);

        CreateJiraIssueRequest? captured = null;
        _jira.Setup(m => m.CreateIssueAsync(conn, It.IsAny<CreateJiraIssueRequest>(), It.IsAny<CancellationToken>()))
            .Callback((Connection _, CreateJiraIssueRequest r, CancellationToken _) => captured = r)
            .ReturnsAsync(new JiraCreatedIssue("1", "SCRUM-1"));
        _jira.Setup(m => m.GetIssueAsync(conn, "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatedIssue("1", "SCRUM-1"));

        await _service.CreateTicketAsync(_userId, Request(conn.Id));

        captured.Should().NotBeNull();
        captured!.ProjectKey.Should().Be("SCRUM");
        captured.IssueType.Should().Be("Task");
        captured.Summary.Should().Be("Fix the bug");
        captured.Description.Should().Be("Some description");
    }

    [Fact]
    public async Task CreateTicket_ConnectionNotFound_Throws404()
    {
        var connId = Guid.NewGuid();
        _connections.Setup(m => m.GetByIdAsync(connId, It.IsAny<CancellationToken>())).ReturnsAsync((Connection?)null);

        var act = () => _service.CreateTicketAsync(_userId, Request(connId));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateTicket_OtherUsersConnection_Throws403()
    {
        var conn = JiraConn();
        conn.UserId = Guid.NewGuid(); // không phải _userId
        _connections.Setup(m => m.GetByIdAsync(conn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conn);

        var act = () => _service.CreateTicketAsync(_userId, Request(conn.Id));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task CreateTicket_NonJiraConnection_ThrowsBusinessRule()
    {
        var conn = JiraConn();
        conn.ServiceType = ServiceType.Gmail;
        _connections.Setup(m => m.GetByIdAsync(conn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conn);

        var act = () => _service.CreateTicketAsync(_userId, Request(conn.Id));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task CreateTicket_InactiveConnection_ThrowsBusinessRule()
    {
        var conn = JiraConn(ConnectionStatus.Error);
        _connections.Setup(m => m.GetByIdAsync(conn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conn);

        var act = () => _service.CreateTicketAsync(_userId, Request(conn.Id));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task CreateTicket_ProviderRejects_PropagatesException()
    {
        var conn = JiraConn();
        _connections.Setup(m => m.GetByIdAsync(conn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conn);
        _jira.Setup(m => m.CreateIssueAsync(conn, It.IsAny<CreateJiraIssueRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("Jira từ chối tạo issue"));

        var act = () => _service.CreateTicketAsync(_userId, Request(conn.Id));

        await act.Should().ThrowAsync<BusinessRuleException>();
        _items.Verify(m => m.AddAsync(It.IsAny<Item>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
