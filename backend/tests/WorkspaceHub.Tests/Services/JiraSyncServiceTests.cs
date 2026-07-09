using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class JiraSyncServiceTests
{
    private readonly Mock<IJiraGateway> _gatewayMock = new();
    private readonly Mock<IJiraItemMapper> _mapperMock = new();
    private readonly Mock<IItemRepository> _itemsMock = new();
    private readonly Mock<IConnectionRepository> _connectionsMock = new();
    private readonly JiraSyncService _service;

    public JiraSyncServiceTests()
    {
        _service = new JiraSyncService(
            _gatewayMock.Object, _mapperMock.Object, _itemsMock.Object, _connectionsMock.Object);

        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item>());

        _mapperMock.Setup(m => m.ToItem(It.IsAny<JiraIssue>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>()))
            .Returns((JiraIssue i, Guid u, Guid c, string? _) => new Item
            {
                ExternalId = i.Id, ConnectionId = c, UserId = u,
                Title = i.Summary ?? "", Snippet = "", MetadataJson = "{}"
            });

        _gatewayMock.Setup(m => m.GetSiteUrlAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    private static Connection JiraConnection(string? cursor = null, CursorType? cursorType = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ServiceType = ServiceType.Jira,
            ProviderAccountId = "cloud-1",
            CursorValue = cursor,
            CursorType = cursorType
        };

    private static JiraIssue Issue(string id, DateTimeOffset? updated = null) =>
        new(id, $"K-{id}", "K", "Scrum Project", $"Summary {id}", null, "To Do", null, null, null, "Task", null, updated);

    [Fact]
    public async Task NonJiraConnection_Throws()
    {
        var conn = JiraConnection();
        conn.ServiceType = ServiceType.Gmail;

        var act = () => _service.SyncConnectionAsync(conn);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task FirstSync_CreatesAllItems_AndSetsCursorToMaxUpdated()
    {
        var conn = JiraConnection();
        var older = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);

        _gatewayMock.Setup(m => m.SearchIssuesAsync(conn, It.IsAny<string>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraSearchResult(
                new List<JiraIssue> { Issue("1", older), Issue("2", newer) }, null, true));

        var result = await _service.SyncConnectionAsync(conn);

        result.Created.Should().Be(2);
        result.Skipped.Should().Be(0);
        result.Scanned.Should().Be(2);

        conn.CursorType.Should().Be(CursorType.JqlUpdated);
        conn.CursorValue.Should().Be(newer.UtcDateTime.ToString("O"));
        conn.Status.Should().Be(ConnectionStatus.Active);
        conn.LastSyncedAt.Should().NotBeNull();
        conn.LastError.Should().BeNull();

        _itemsMock.Verify(m => m.AddRangeAsync(It.Is<IEnumerable<Item>>(x => true), It.IsAny<CancellationToken>()), Times.Once);
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReSync_ExistingIssue_UpdatedNotRecreated()
    {
        var conn = JiraConnection();
        // Issue "1" đã tồn tại local (tracked) → re-sync cập nhật, không tạo trùng.
        var existing1 = new Item { ExternalId = "1", ConnectionId = conn.Id, UserId = conn.UserId, Title = "old title", Snippet = "", MetadataJson = "{}" };
        _itemsMock.Setup(m => m.GetTrackedByConnectionIdAsync(conn.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, Item> { ["1"] = existing1 });

        _gatewayMock.Setup(m => m.SearchIssuesAsync(conn, It.IsAny<string>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraSearchResult(
                new List<JiraIssue> { Issue("1", null) with { Summary = "new title" }, Issue("2") }, null, true));

        var result = await _service.SyncConnectionAsync(conn);

        result.Created.Should().Be(1);   // chỉ issue "2" là mới
        result.Skipped.Should().Be(1);   // issue "1" không tạo mới (đã update)
        // Item cũ được cập nhật field từ provider (mapper trả Title = Summary).
        existing1.Title.Should().Be("new title");
    }

    [Fact]
    public async Task Pagination_FollowsNextPageTokenUntilLast()
    {
        var conn = JiraConnection();

        _gatewayMock.Setup(m => m.SearchIssuesAsync(conn, It.IsAny<string>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraSearchResult(new List<JiraIssue> { Issue("1") }, "tok", false));
        _gatewayMock.Setup(m => m.SearchIssuesAsync(conn, It.IsAny<string>(), "tok", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraSearchResult(new List<JiraIssue> { Issue("2") }, null, true));

        var result = await _service.SyncConnectionAsync(conn);

        result.Created.Should().Be(2);
        _gatewayMock.Verify(m => m.SearchIssuesAsync(conn, It.IsAny<string>(), "tok", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IncrementalSync_WithCursor_BuildsJqlWithUpdatedFilter()
    {
        var cursor = new DateTimeOffset(2026, 6, 20, 8, 30, 0, TimeSpan.Zero);
        var conn = JiraConnection(cursor.UtcDateTime.ToString("O"), CursorType.JqlUpdated);

        string? capturedJql = null;
        _gatewayMock.Setup(m => m.SearchIssuesAsync(conn, It.IsAny<string>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((Connection _, string? jql, string? _, int _, CancellationToken _) => capturedJql = jql)
            .ReturnsAsync(new JiraSearchResult(new List<JiraIssue>(), null, true));

        await _service.SyncConnectionAsync(conn);

        capturedJql.Should().NotBeNull();
        capturedJql.Should().Contain("updated >=");
        capturedJql.Should().Contain("ORDER BY updated ASC");
    }

    [Fact]
    public async Task FirstSync_NoCursor_UsesFloorAndNoCurrentUserRestriction()
    {
        var conn = JiraConnection();

        string? capturedJql = null;
        _gatewayMock.Setup(m => m.SearchIssuesAsync(conn, It.IsAny<string>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((Connection _, string? jql, string? _, int _, CancellationToken _) => capturedJql = jql)
            .ReturnsAsync(new JiraSearchResult(new List<JiraIssue>(), null, true));

        await _service.SyncConnectionAsync(conn);

        // Full sync kéo TOÀN BỘ board: có mốc sàn (JQL không được unbounded) nhưng KHÔNG giới hạn currentUser.
        capturedJql.Should().Contain("updated >=");
        capturedJql.Should().Contain("ORDER BY updated ASC");
        capturedJql.Should().NotContain("currentUser");
    }

    [Fact]
    public async Task EmptyResult_DoesNotAddRange_ButUpdatesConnection()
    {
        var conn = JiraConnection();
        _gatewayMock.Setup(m => m.SearchIssuesAsync(conn, It.IsAny<string>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraSearchResult(new List<JiraIssue>(), null, true));

        var result = await _service.SyncConnectionAsync(conn);

        result.Created.Should().Be(0);
        result.Scanned.Should().Be(0);
        _itemsMock.Verify(m => m.AddRangeAsync(It.IsAny<IEnumerable<Item>>(), It.IsAny<CancellationToken>()), Times.Never);
        conn.LastSyncedAt.Should().NotBeNull();
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyResult_KeepsPreviousCursor()
    {
        var prevCursor = new DateTimeOffset(2026, 6, 20, 8, 30, 0, TimeSpan.Zero).UtcDateTime.ToString("O");
        var conn = JiraConnection(prevCursor, CursorType.JqlUpdated);

        _gatewayMock.Setup(m => m.SearchIssuesAsync(conn, It.IsAny<string>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JiraSearchResult(new List<JiraIssue>(), null, true));

        await _service.SyncConnectionAsync(conn);

        conn.CursorValue.Should().Be(prevCursor);
    }
}
