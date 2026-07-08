using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Tests.Services;

public class SendEmailServiceSuggestContactsTests
{
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IGmailGateway> _gmail = new();
    private readonly Mock<IGoogleContactRepository> _googleContacts = new();
    private readonly Mock<IItemRepository> _items = new();
    private readonly GoogleContactMapper _mapper = new();
    private readonly SendEmailService _service;

    public SendEmailServiceSuggestContactsTests()
    {
        _service = new SendEmailService(_connections.Object, _gmail.Object, _googleContacts.Object, _mapper, _items.Object);
    }

    [Fact]
    public async Task GetContactSuggestions_ReturnsContactAndOtherContact()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmailConnection(userId, connId);

        _googleContacts.Setup(m => m.GetByConnectionId(connId))
            .Returns(new List<ContactSuggestionDto>
            {
                new() { Email = "alice@example.com", DisplayName = "Alice", Source = "Contact" },
                new() { Email = "alex@example.com", DisplayName = "Alex", Source = "OtherContact" },
            }.AsQueryable());

        var query = await _service.GetContactSuggestionsAsync(userId, connId);
        var result = query.ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(x => x.Email == "alice@example.com" && x.Source == "Contact");
        result.Should().Contain(x => x.Email == "alex@example.com" && x.Source == "OtherContact");
    }

    [Fact]
    public async Task GetContactSuggestions_DedupesByEmail()
    {
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();
        SetupGmailConnection(userId, connId);

        _googleContacts.Setup(m => m.GetByConnectionId(connId))
            .Returns(new List<ContactSuggestionDto>
            {
                new() { Email = "alice@example.com", DisplayName = "Alice", Source = "Contact" },
            }.AsQueryable());

        var query = await _service.GetContactSuggestionsAsync(userId, connId);
        var result = query.ToList();

        result.Should().HaveCount(1);
        result[0].Email.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task GetContactSuggestions_WrongOwner_ThrowsNotFound()
    {
        var connId = Guid.NewGuid();
        _connections.Setup(m => m.GetByIdAsync(connId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Connection { Id = connId, UserId = Guid.NewGuid(), ServiceType = ServiceType.Gmail, Status = ConnectionStatus.Active });

        var act = () => _service.GetContactSuggestionsAsync(Guid.NewGuid(), connId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetContactSuggestions_NonGmail_ThrowsBusinessRule()
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

        var act = () => _service.GetContactSuggestionsAsync(userId, connId);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Gmail*");
    }

    [Fact]
    public async Task GetContactSuggestions_Inactive_ThrowsBusinessRule()
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

        var act = () => _service.GetContactSuggestionsAsync(userId, connId);

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
