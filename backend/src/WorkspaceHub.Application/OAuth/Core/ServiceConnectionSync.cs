using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth.Core;

public static class ServiceConnectionSync
{
    public static void ApplyGrantedScopes(
        OAuthConnection connection,
        IReadOnlyList<ServiceType> grantedServices)
    {
        // Tắt service bị thu hồi, bật lại service được cấp.
        foreach (var existing in connection.ServiceConnections)
            existing.IsEnabled = grantedServices.Contains(existing.ServiceType);

        // Thêm mới các service chưa tồn tại.
        foreach (var service in grantedServices)
        {
            if (!connection.ServiceConnections.Any(sc => sc.ServiceType == service))
                connection.ServiceConnections.Add(new ServiceConnection
                {
                    Id = Guid.NewGuid(),
                    OAuthConnectionId = connection.Id,
                    ServiceType = service,
                    IsEnabled = true
                });
        }
    }
}
