using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class GoogleContactServiceTests
{
    private readonly Mock<IGoogleContactRepository> _contacts = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IPeopleGateway> _people = new();
    private readonly IWriteBackGuard _guard = new WriteBackGuard(Microsoft.Extensions.Logging.Abstractions.NullLogger<WriteBackGuard>.Instance);
    private readonly GoogleContactMapper _mapper = new();
    private readonly GoogleContactService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _connectionId = Guid.NewGuid();

    public GoogleContactServiceTests()
    {
        _service = new GoogleContactService(
            _contacts.Object, _connections.Object, _people.Object, _guard, _mapper);
    }

    private Connection ActiveGmail() => new()
    {
        Id = _connectionId,
        UserId = _userId,
        ServiceType = ServiceType.Gmail,
        Status = ConnectionStatus.Active,
        Provider = ProviderType.Google
    };

    [Fact]
    public async Task Create_CallsGatewayAndUpserts()
    {
        _connections.Setup(c => c.GetByIdAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveGmail());
        _contacts.Setup(r => r.GetByEmailForConnectionAsync(_connectionId, "alice@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleContact?)null);

        _people.Setup(p => p.CreateContactAsync(It.IsAny<Connection>(), "alice@example.com", "Alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PeopleContactDetail
            {
                Email = "alice@example.com",
                DisplayName = "Alice",
                Etag = "etag-1",
                ResourceName = "people/c123"
            });

        GoogleContact? upserted = null;
        _contacts.Setup(r => r.UpsertAsync(It.IsAny<GoogleContact>(), It.IsAny<CancellationToken>()))
            .Callback((GoogleContact c, CancellationToken _) => upserted = c)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateAsync(_userId, new CreateContactRequest
        {
            ConnectionId = _connectionId,
            Email = "Alice@example.com",
            DisplayName = "Alice"
        });

        result.Email.Should().Be("alice@example.com");
        result.Source.Should().Be(GoogleContactSource.Contact);
        upserted.Should().NotBeNull();
        upserted!.ExternalResourceName.Should().Be("people/c123");
        upserted.Etag.Should().Be("etag-1");
        _people.Verify(p => p.CreateContactAsync(It.IsAny<Connection>(), "alice@example.com", "Alice", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_EtagMismatch_Throws409()
    {
        var contactId = Guid.NewGuid();
        var contact = new GoogleContact
        {
            Id = contactId,
            ConnectionId = _connectionId,
            Email = "alice@example.com",
            DisplayName = "Alice",
            Source = GoogleContactSource.Contact,
            ExternalResourceName = "people/c123",
            Etag = "stored-etag",
            SyncedAt = DateTime.UtcNow
        };

        _contacts.Setup(r => r.GetByIdForUserAsync(contactId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contact);
        _connections.Setup(c => c.GetByIdAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveGmail());
        _people.Setup(p => p.GetContactAsync(It.IsAny<Connection>(), "people/c123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PeopleContactDetail
            {
                Email = "alice@example.com",
                DisplayName = "Alice",
                Etag = "live-etag",
                ResourceName = "people/c123"
            });

        var act = () => _service.UpdateAsync(_userId, contactId, new PatchContactRequest
        {
            Etag = "client-etag",
            DisplayName = "Alice Updated"
        });

        await act.Should().ThrowAsync<ConflictException>();
        _people.Verify(p => p.UpdateContactAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_OtherContact_Throws422()
    {
        var contactId = Guid.NewGuid();
        _contacts.Setup(r => r.GetByIdForUserAsync(contactId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleContact
            {
                Id = contactId,
                ConnectionId = _connectionId,
                Email = "bob@example.com",
                Source = GoogleContactSource.OtherContact,
                ExternalResourceName = "people/c999",
                SyncedAt = DateTime.UtcNow
            });

        var act = () => _service.UpdateAsync(_userId, contactId, new PatchContactRequest
        {
            Etag = "etag",
            DisplayName = "Bob"
        });

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Delete_OtherContact_Throws422()
    {
        var contactId = Guid.NewGuid();
        _contacts.Setup(r => r.GetByIdForUserAsync(contactId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleContact
            {
                Id = contactId,
                ConnectionId = _connectionId,
                Email = "bob@example.com",
                Source = GoogleContactSource.OtherContact,
                ExternalResourceName = "people/c999",
                SyncedAt = DateTime.UtcNow
            });

        var act = () => _service.DeleteAsync(_userId, contactId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Create_DuplicateEmail_Throws409WithoutCallingGoogle()
    {
        _connections.Setup(c => c.GetByIdAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveGmail());
        _contacts.Setup(r => r.GetByEmailForConnectionAsync(_connectionId, "alice@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleContact
            {
                Id = Guid.NewGuid(),
                ConnectionId = _connectionId,
                Email = "alice@example.com",
                Source = GoogleContactSource.Contact,
                SyncedAt = DateTime.UtcNow
            });

        var act = () => _service.CreateAsync(_userId, new CreateContactRequest
        {
            ConnectionId = _connectionId,
            Email = "alice@example.com",
            DisplayName = "Alice"
        });

        await act.Should().ThrowAsync<ConflictException>();
        _people.Verify(p => p.CreateContactAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_MissingWriteScope_Throws403()
    {
        _connections.Setup(c => c.GetByIdAsync(_connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveGmail());
        _contacts.Setup(r => r.GetByEmailForConnectionAsync(_connectionId, "alice@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleContact?)null);
        _people.Setup(p => p.CreateContactAsync(It.IsAny<Connection>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Reconnect Gmail to allow editing contacts."));

        var act = () => _service.CreateAsync(_userId, new CreateContactRequest
        {
            ConnectionId = _connectionId,
            Email = "alice@example.com",
            DisplayName = "Alice"
        });

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Update_WrongOwner_Throws404()
    {
        var contactId = Guid.NewGuid();
        _contacts.Setup(r => r.GetByIdForUserAsync(contactId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleContact?)null);

        var act = () => _service.UpdateAsync(_userId, contactId, new PatchContactRequest
        {
            Etag = "etag",
            DisplayName = "X"
        });

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
