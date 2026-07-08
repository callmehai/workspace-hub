using WorkspaceHub.Application.DTOs.ScheduledEmails;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IScheduledEmailsService
{
    /// <summary>OData list — IQueryable in-memory sau scope userId.</summary>
    IQueryable<ScheduledEmailDto> GetByUserId(Guid userId);

    Task<ScheduledEmailDto> CreateAsync(Guid userId, CreateScheduledEmailRequest request, CancellationToken ct = default);

    Task<ScheduledEmailDto> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default);

    Task<ScheduledEmailDto> CancelAsync(Guid userId, Guid id, CancellationToken ct = default);
}
