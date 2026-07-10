using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping;

public interface IGoogleContactMapper
{
    GoogleContact ToEntity(PeopleContactRow row, Guid connectionId, DateTime syncedAt);

    ContactDto ToDto(GoogleContact entity);

    ContactDetailDto ToDetailDto(GoogleContact entity, ContactProfileDto profile, bool readOnly = false);
}

