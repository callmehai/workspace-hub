using WorkspaceHub.Application.DTOs.Contacts;
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
            Etag = row.Etag,
            SyncedAt = syncedAt
        };

    public ContactDto ToDto(GoogleContact entity) =>
        new()
        {
            Id = entity.Id,
            ConnectionId = entity.ConnectionId,
            Email = entity.Email,
            DisplayName = entity.DisplayName,
            Source = entity.Source,
            Etag = entity.Etag,
            SyncedAt = entity.SyncedAt,
            UpdatedAt = entity.UpdatedAt
        };
}

