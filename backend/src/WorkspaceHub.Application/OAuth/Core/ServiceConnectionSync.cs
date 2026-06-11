using System.Collections.Generic;
using System.Linq;
using System;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Core;

public static class ServiceConnectionSync
{
    public static void ApplyGrantedScopes(OAuthConnection connection, IReadOnlyList<ServiceType> grantedServices)
    {
        // Đồng bộ 2 chiều: tắt service bị thu hồi, bật service được cấp lại.
        // Xử lý trường hợp user uncheck một số quyền khi Google fine-grained consent.
        foreach (var existing in connection.ServiceConnections)
            existing.IsEnabled = grantedServices.Contains(existing.ServiceType);

        // Thêm mới các service chưa tồn tại trong connection.
        foreach (var service in grantedServices)
        {
            if (!connection.ServiceConnections.Any(sc => sc.ServiceType == service))
                connection.ServiceConnections.Add(new ServiceConnection
                {
                    Id = Guid.NewGuid(),
                    OAuthConnectionId = connection.Id,
                    ServiceType = service,
                    IsEnabled = true,
                    CursorValue = null
                });
        }
    }
}
