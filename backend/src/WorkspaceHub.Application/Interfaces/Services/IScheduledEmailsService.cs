using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Interfaces.Services;

public interface IScheduledEmailsService
{
    Task<ScheduledEmailDto> CreateAsync(Guid userId, CreateScheduledEmailRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduledEmailDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<ScheduledEmailDto> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<ScheduledEmailDto> CancelAsync(Guid userId, Guid id, CancellationToken ct = default);
}
