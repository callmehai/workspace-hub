using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>CRUD contact đã lưu (Source=Contact) với write-back Google People API (SCRUM-76/77).</summary>
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

    public async Task<ContactDetailDto> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var contact = await _contacts.GetByIdForUserAsync(id, userId, ct)
            ?? throw new NotFoundException(nameof(GoogleContact), id);

        return BuildDetailFromCache(
            contact,
            readOnly: contact.Source == GoogleContactSource.OtherContact);
    }

    public async Task<IReadOnlyList<ContactSuggestionDto>> SuggestAsync(
        Guid userId,
        Guid connectionId,
        string query,
        int limit = 10,
        CancellationToken ct = default)
    {
        await ValidateGmailConnectionAsync(userId, connectionId, ct);

        var term = query.Trim();
        if (term.Length < 2) return [];

        limit = Math.Clamp(limit, 1, 50);
        var rows = await _contacts.ListForConnectionAsync(connectionId, ct);
        var results = new List<ContactSuggestionDto>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var profile = DeserializeProfile(row);
            var displayName = ContactProfileJson.ComputeDisplayName(profile) ?? row.DisplayName;

            foreach (var labeled in profile.Emails.Where(e => !string.IsNullOrWhiteSpace(e.Value)))
            {
                var email = labeled.Value.Trim().ToLowerInvariant();
                if (!seenEmails.Add(email)) continue;

                if (!email.Contains(term, StringComparison.OrdinalIgnoreCase)
                    && !(displayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue;
                }

                results.Add(new ContactSuggestionDto
                {
                    Email = email,
                    DisplayName = displayName,
                    Source = row.Source,
                });

                if (results.Count >= limit) return results;
            }
        }

        return results;
    }

    public async Task<ContactDto> CreateAsync(Guid userId, CreateContactRequest request, CancellationToken ct = default)
    {
        var connection = await ValidateGmailConnectionAsync(userId, request.ConnectionId, ct);

        var profile = ContactProfileJson.ResolveForCreate(request);
        var email = ContactProfileJson.PrimaryEmail(profile, request.Email.Trim().ToLowerInvariant())
            ?? throw new BusinessRuleException("Contact email is required.");

        if (await _contacts.GetByEmailForConnectionAsync(connection.Id, email, ct) != null)
            throw new ConflictException("A contact with this email already exists for this connection.");

        var created = await _peopleGateway.CreateContactAsync(connection, profile, ct);
        var now = DateTime.UtcNow;

        await _contacts.ApplyDetailToResourceAsync(connection.Id, created.ResourceName, created, now, ct);

        var entity = await _contacts.GetByResourceNameForConnectionAsync(connection.Id, created.ResourceName, ct)
            ?? throw new InvalidOperationException("Contact cache row missing after create.");

        return _mapper.ToDto(entity);
    }

    public async Task<ContactDetailDto> UpdateAsync(Guid userId, Guid id, PatchContactRequest request, CancellationToken ct = default)
    {
        var contact = await _contacts.GetByIdForUserAsync(id, userId, ct)
            ?? throw new NotFoundException(nameof(GoogleContact), id);
        EnsureMutableSource(contact);

        var connection = await ValidateGmailConnectionAsync(userId, contact.ConnectionId, ct);

        if (string.IsNullOrWhiteSpace(contact.ExternalResourceName))
            throw new BusinessRuleException("Contact is not linked to Google. Please sync and try again.");

        _guard.EnsureNoConflict(request.Etag, contact.Etag);

        var cachedProfile = DeserializeProfile(contact);
        var profile = ContactProfileJson.ResolveForPatch(request, cachedProfile);
        if (!ContactProfileJson.HasEmail(profile))
            throw new BusinessRuleException("Contact must have at least one email.");

        var updated = await _peopleGateway.UpdateContactAsync(
            connection, contact.ExternalResourceName, contact.Etag, profile, ct);

        var now = DateTime.UtcNow;
        await _contacts.ApplyDetailToResourceAsync(connection.Id, updated.ResourceName, updated, now, ct);

        var refreshed = await _contacts.GetByIdForUserAsync(id, userId, ct)
            ?? throw new NotFoundException(nameof(GoogleContact), id);

        return _mapper.ToDetailDto(refreshed, ContactProfileJson.Normalize(updated.Profile));
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
        await _contacts.DeleteByResourceNameAsync(connection.Id, contact.ExternalResourceName, ct);
    }

    private static ContactProfileDto DeserializeProfile(GoogleContact contact)
    {
        var profile = ContactProfileJson.Deserialize(contact.MetadataJson);
        if (profile.Emails.Count == 0 && !string.IsNullOrWhiteSpace(contact.Email))
            profile.Emails.Add(new LabeledEmailDto { Value = contact.Email });
        return ContactProfileJson.Normalize(profile);
    }

    private ContactDetailDto BuildDetailFromCache(GoogleContact contact, bool readOnly) =>
        _mapper.ToDetailDto(contact, DeserializeProfile(contact), readOnly);

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
}
