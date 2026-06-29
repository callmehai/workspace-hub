using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class JiraMetadataServiceTests
{
    private readonly Mock<IJiraGateway> _gateway = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly JiraMetadataService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connId = Guid.NewGuid();

    public JiraMetadataServiceTests()
    {
        _service = new JiraMetadataService(_gateway.Object, _connections.Object, _items.Object, _cache);
    }

    private void SetupConn(ServiceType type = ServiceType.Jira, ConnectionStatus status = ConnectionStatus.Active, Guid? owner = null) =>
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection { Id = _connId, UserId = owner ?? _userId, ServiceType = type, ProviderAccountId = "cloud-1", Status = status });

    [Fact]
    public async Task GetProjects_ReturnsFromGateway()
    {
        SetupConn();
        _gateway.Setup(m => m.GetProjectsAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraProject> { new("1", "SCRUM", "Scrum Project") });

        var result = await _service.GetProjectsAsync(_connId, _userId);

        result.Should().HaveCount(1);
        result[0].Key.Should().Be("SCRUM");
    }

    [Fact]
    public async Task GetProjects_CachesResult_GatewayCalledOnce()
    {
        SetupConn();
        _gateway.Setup(m => m.GetProjectsAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraProject> { new("1", "SCRUM", "Scrum") });

        await _service.GetProjectsAsync(_connId, _userId);
        await _service.GetProjectsAsync(_connId, _userId);

        _gateway.Verify(m => m.GetProjectsAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectionNotFound_Throws404()
    {
        _connections.Setup(m => m.GetByIdAsync(_connId, It.IsAny<CancellationToken>())).ReturnsAsync((Connection?)null);

        var act = () => _service.GetProjectsAsync(_connId, _userId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ConnectionOtherUser_Throws404()
    {
        SetupConn(owner: Guid.NewGuid());

        var act = () => _service.GetProjectsAsync(_connId, _userId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task NonJiraConnection_ThrowsBusinessRule()
    {
        SetupConn(type: ServiceType.Gmail);

        var act = () => _service.GetProjectsAsync(_connId, _userId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task InactiveConnection_ThrowsBusinessRule()
    {
        SetupConn(status: ConnectionStatus.Error);

        var act = () => _service.GetProjectsAsync(_connId, _userId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetIssueTypes_EmptyProjectKey_ThrowsBusinessRule()
    {
        SetupConn();
        var act = () => _service.GetIssueTypesAsync(_connId, _userId, "  ");
        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetAssignableUsers_EmptyProjectKey_ThrowsBusinessRule()
    {
        SetupConn();
        var act = () => _service.GetAssignableUsersAsync(_connId, _userId, "", null);
        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetPriorities_NotCachedAcrossDifferentConnections()
    {
        SetupConn();
        _gateway.Setup(m => m.GetPrioritiesAsync(It.IsAny<Connection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraPriority> { new("1", "High") });

        var result = await _service.GetPrioritiesAsync(_connId, _userId);

        result.Should().ContainSingle(p => p.Name == "High");
    }

    [Fact]
    public async Task GetTransitions_ValidTicket_ReturnsFromGateway()
    {
        SetupConn();
        var itemId = Guid.NewGuid();
        _items.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item { Id = itemId, UserId = _userId, ConnectionId = _connId, Type = ItemType.Ticket, ExternalId = "SCRUM-1" });
        _gateway.Setup(m => m.GetTransitionsAsync(It.IsAny<Connection>(), "SCRUM-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<JiraTransition> { new("31", "Done", "Done") });

        var result = await _service.GetTransitionsAsync(_connId, _userId, itemId);

        result.Should().ContainSingle(t => t.Id == "31");
    }

    [Fact]
    public async Task GetTransitions_ItemNotTicket_ThrowsBusinessRule()
    {
        SetupConn();
        var itemId = Guid.NewGuid();
        _items.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item { Id = itemId, UserId = _userId, ConnectionId = _connId, Type = ItemType.Email, ExternalId = "x" });

        var act = () => _service.GetTransitionsAsync(_connId, _userId, itemId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetTransitions_ItemNotFound_Throws404()
    {
        SetupConn();
        var itemId = Guid.NewGuid();
        _items.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync((Item?)null);

        var act = () => _service.GetTransitionsAsync(_connId, _userId, itemId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetTransitions_ItemWrongConnection_ThrowsBusinessRule()
    {
        SetupConn();
        var itemId = Guid.NewGuid();
        _items.Setup(m => m.GetByIdAndUserAsync(itemId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Item { Id = itemId, UserId = _userId, ConnectionId = Guid.NewGuid(), Type = ItemType.Ticket, ExternalId = "SCRUM-1" });

        var act = () => _service.GetTransitionsAsync(_connId, _userId, itemId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
