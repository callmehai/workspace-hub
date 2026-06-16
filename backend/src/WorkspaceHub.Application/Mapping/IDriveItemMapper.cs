using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping
{
    public interface IDriveItemMapper
    {
        Item ToItem(DriveFileDto file, Guid userId, Guid connectionId);
    }
}
