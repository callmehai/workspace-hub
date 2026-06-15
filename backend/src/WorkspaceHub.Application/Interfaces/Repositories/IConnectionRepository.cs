using WorkspaceHub.Application.Common;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Interfaces.Repositories;

/// <summary>Repository cho Connection (mô hình B) — mỗi service 1 connection riêng.</summary>
public interface IConnectionRepository : IGenericRepository<Connection>
{
    /// <summary>Tra theo unique key (UserId, Provider, ServiceType, ProviderAccountId). Tracked — dùng được cho upsert.</summary>
    Task<Connection?> GetByUniqueKeyAsync(
        Guid userId,
        ProviderType provider,
        ServiceType serviceType,
        string providerAccountId,
        CancellationToken ct = default);

    /// <summary>Lấy tất cả connections của user (AsNoTracking cho read).</summary>
    Task<IReadOnlyList<Connection>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Lấy connection theo ID (tracked, để update/delete).</summary>
    Task<Connection?> GetByIdTrackedAsync(Guid id, CancellationToken ct = default);
}
