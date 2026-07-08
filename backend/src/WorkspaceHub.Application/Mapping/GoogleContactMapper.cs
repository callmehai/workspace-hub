using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping;

public class GoogleContactMapper : IGoogleContactMapper
{
    public GoogleContact ToEntity(PeopleContactRow row, Guid connectionId, DateTime syncedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            Email = row.Email,
            DisplayName = row.DisplayName,
            Source = row.Source,
            ExternalResourceName = row.ExternalResourceName,
            SyncedAt = syncedAt
        };

    public ContactSuggestionDto ToSuggestion(GoogleContact entity) =>
        new()
        {
            Email = entity.Email,
            DisplayName = entity.DisplayName,
            Source = entity.Source.ToString()
        };
}
