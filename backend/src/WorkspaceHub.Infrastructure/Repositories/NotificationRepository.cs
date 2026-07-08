using Microsoft.EntityFrameworkCore;
using WorkspaceHub.Application.DTOs.Notifications;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Data;

namespace WorkspaceHub.Infrastructure.Repositories;

public class NotificationRepository : GenericRepository<Notification>, INotificationRepository
{
    public NotificationRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<Notification>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await Set.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);
    }

    public IQueryable<NotificationDto> QueryByUserId(Guid userId) =>
        Set
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                Body = n.Body,
                LinkUrl = n.LinkUrl,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            });

    public Task<Notification?> GetByIdForUserAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        return Set.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);
    }

    public async Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
    {
        await Set
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    public async Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct = default)
        => await Set.AddRangeAsync(notifications, ct);
}
