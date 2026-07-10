using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Tests.Services;

public class GoogleContactServiceQueryableTests
{
    private readonly Mock<IGoogleContactRepository> _contacts = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<WorkspaceHub.Application.Abstractions.IPeopleGateway> _people = new();
    private readonly WriteBackGuard _guard = new(Microsoft.Extensions.Logging.Abstractions.NullLogger<WriteBackGuard>.Instance);
    private readonly GoogleContactMapper _mapper = new();
    private readonly GoogleContactService _service;

    public GoogleContactServiceQueryableTests()
    {
        _service = new GoogleContactService(
            _contacts.Object, _connections.Object, _people.Object, _guard, _mapper);
    }

    [Fact]
    public async Task GetQueryable_ReturnsRepositoryQuery()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmailConnection(userId, connId);

        var data = new List<ContactDto>
        {
            new() { Id = Guid.NewGuid(), ConnectionId = connId, Email = "alice@example.com", DisplayName = "Alice", Source = GoogleContactSource.Contact, SyncedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), ConnectionId = connId, Email = "alex@example.com", DisplayName = "Alex", Source = GoogleContactSource.OtherContact, SyncedAt = DateTime.UtcNow },
        }.AsQueryable();

        _contacts.Setup(m => m.GetQueryableByConnectionId(connId)).Returns(data);

        var query = await _service.GetQueryableAsync(userId, connId);
        var result = query.ToList();

        result.Should().HaveCount(2);
        _contacts.Verify(m => m.GetQueryableByConnectionId(connId), Times.Once);
    }

    [Fact]
    public async Task GetQueryable_WrongOwner_ThrowsNotFound()
    {
        var connId = Guid.NewGuid();
        _connections.Setup(m => m.GetByIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection { Id = connId, UserId = Guid.NewGuid(), ServiceType = ServiceType.Gmail, Status = ConnectionStatus.Active });

        var act = () => _service.GetQueryableAsync(Guid.NewGuid(), connId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetQueryable_NonGmail_ThrowsBusinessRule()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        _connections.Setup(m => m.GetByIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = connId,
                UserId = userId,
                ServiceType = ServiceType.GCal,
                Status = ConnectionStatus.Active
            });

        var act = () => _service.GetQueryableAsync(userId, connId);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Gmail*");
    }

    [Fact]
    public async Task GetQueryable_Inactive_ThrowsBusinessRule()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        _connections.Setup(m => m.GetByIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = connId,
                UserId = userId,
                ServiceType = ServiceType.Gmail,
                Status = ConnectionStatus.Disconnected
            });

        var act = () => _service.GetQueryableAsync(userId, connId);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*not active*");
    }

    private void SetupGmailConnection(Guid userId, Guid connId)
    {
        _connections.Setup(m => m.GetByIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection
            {
                Id = connId,
                UserId = userId,
                ServiceType = ServiceType.Gmail,
                Status = ConnectionStatus.Active
            });
    }
}
