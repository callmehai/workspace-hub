using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping;

public interface IDriveItemMapper
{
    Item ToItem(DriveFileDto file, Guid userId, Guid connectionId);
}
