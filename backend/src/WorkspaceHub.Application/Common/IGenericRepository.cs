namespace WorkspaceHub.Application.Common;

/// <summary>
/// Hợp đồng data-access chung. Interface nằm ở Application; cài đặt ở Infrastructure
/// → Service phụ thuộc abstraction, không phụ thuộc EF Core.
/// </summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default);
}
