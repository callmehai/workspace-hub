using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping;

public interface IGoogleContactMapper
{
    GoogleContact ToEntity(PeopleContactRow row, Guid connectionId, DateTime syncedAt);

    ContactSuggestionDto ToSuggestion(GoogleContact entity);
}
