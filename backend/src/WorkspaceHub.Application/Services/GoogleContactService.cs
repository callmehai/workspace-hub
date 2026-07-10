using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>CRUD contact đã lưu (Source=Contact) với write-back Google People API (SCRUM-76).</summary>
public class GoogleContactService : IGoogleContactService
{
    private readonly IGoogleContactRepository _contacts;
    private readonly IConnectionRepository _connections;
    private readonly IPeopleGateway _peopleGateway;
    private readonly IWriteBackGuard _guard;
    private readonly IGoogleContactMapper _mapper;

    public GoogleContactService(
        IGoogleContactRepository contacts,
        IConnectionRepository connections,
        IPeopleGateway peopleGateway,
        IWriteBackGuard guard,
        IGoogleContactMapper mapper)
    {
        _contacts = contacts;
        _connections = connections;
        _peopleGateway = peopleGateway;
        _guard = guard;
        _mapper = mapper;
    }

    public async Task<IQueryable<ContactDto>> GetQueryableAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken ct = default)
    {
        await ValidateGmailConnectionAsync(userId, connectionId, ct);
        return _contacts.GetQueryableByConnectionId(connectionId);
    }

    public async Task<ContactDto> CreateAsync(Guid userId, CreateContactRequest request, CancellationToken ct = default)
    {
        var connection = await ValidateGmailConnectionAsync(userId, request.ConnectionId, ct);

        var email = request.Email.Trim().ToLowerInvariant();
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();

        if (await _contacts.GetByEmailForConnectionAsync(connection.Id, email, ct) != null)
            throw new ConflictException("A contact with this email already exists for this connection.");

        var created = await _peopleGateway.CreateContactAsync(connection, email, displayName, ct);
        var now = DateTime.UtcNow;

        var entity = new GoogleContact
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            Email = created.Email,
            DisplayName = created.DisplayName,
            Source = GoogleContactSource.Contact,
            ExternalResourceName = created.ResourceName,
            Etag = created.Etag,
            SyncedAt = now,
            UpdatedAt = now
        };

        await UpsertOrThrowConflictAsync(entity, ct);
        return _mapper.ToDto(entity);
    }

    public async Task<ContactDto> UpdateAsync(Guid userId, Guid id, PatchContactRequest request, CancellationToken ct = default)
    {
        var contact = await _contacts.GetByIdForUserAsync(id, userId, ct)
            ?? throw new NotFoundException(nameof(GoogleContact), id);
        EnsureMutableSource(contact);

        var connection = await ValidateGmailConnectionAsync(userId, contact.ConnectionId, ct);

        if (string.IsNullOrWhiteSpace(contact.ExternalResourceName))
            throw new BusinessRuleException("Contact is not linked to Google. Please sync and try again.");

        var live = await _peopleGateway.GetContactAsync(connection, contact.ExternalResourceName, ct);
        _guard.EnsureNoConflict(request.Etag, live.Etag);

        var email = string.IsNullOrWhiteSpace(request.Email)
            ? contact.Email
            : request.Email.Trim().ToLowerInvariant();
        var displayName = request.DisplayName != null
            ? (string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim())
            : contact.DisplayName;

        var updated = await _peopleGateway.UpdateContactAsync(
            connection, contact.ExternalResourceName, live.Etag, email, displayName, ct);

        var now = DateTime.UtcNow;
        contact.Email = updated.Email;
        contact.DisplayName = updated.DisplayName;
        contact.Etag = updated.Etag;
        contact.UpdatedAt = now;

        await UpsertOrThrowConflictAsync(contact, ct);
        return _mapper.ToDto(contact);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var contact = await _contacts.GetByIdForUserAsync(id, userId, ct)
            ?? throw new NotFoundException(nameof(GoogleContact), id);
        EnsureMutableSource(contact);

        var connection = await ValidateGmailConnectionAsync(userId, contact.ConnectionId, ct);

        if (string.IsNullOrWhiteSpace(contact.ExternalResourceName))
            throw new BusinessRuleException("Contact is not linked to Google. Please sync and try again.");

        await _peopleGateway.DeleteContactAsync(connection, contact.ExternalResourceName, ct);
        await _contacts.DeleteAsync(contact, ct);
    }

    private static void EnsureMutableSource(GoogleContact contact)
    {
        if (contact.Source != GoogleContactSource.Contact)
            throw new BusinessRuleException("Only saved contacts can be edited or deleted.");
    }

    private async Task<Connection> ValidateGmailConnectionAsync(Guid userId, Guid connectionId, CancellationToken ct)
    {
        var connection = await _connections.GetByIdAsync(connectionId, ct)
            ?? throw new NotFoundException("Connection", connectionId);

        if (connection.UserId != userId)
            throw new NotFoundException("Connection", connectionId);

        if (connection.ServiceType != ServiceType.Gmail)
            throw new BusinessRuleException("Only Gmail connections can be used for contacts.");

        if (connection.Status != ConnectionStatus.Active)
            throw new BusinessRuleException($"Connection is not active (status: {connection.Status}).");

        return connection;
    }

    private async Task UpsertOrThrowConflictAsync(GoogleContact contact, CancellationToken ct)
    {
        try
        {
            await _contacts.UpsertAsync(contact, ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("A contact with this email already exists for this connection.");
        }
    }
}
