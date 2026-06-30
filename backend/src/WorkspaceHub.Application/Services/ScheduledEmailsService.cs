using System.Text.Json;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

public class ScheduledEmailsService : IScheduledEmailsService
{
    private readonly IScheduledEmailRepository _scheduledEmails;
    private readonly IConnectionRepository _connections;

    public ScheduledEmailsService(
        IScheduledEmailRepository scheduledEmails,
        IConnectionRepository connections)
    {
        _scheduledEmails = scheduledEmails;
        _connections = connections;
    }

    public async Task<ScheduledEmailDto> CreateAsync(Guid userId, CreateScheduledEmailRequest request, CancellationToken ct = default)
    {
        if (request.SendAt <= DateTime.UtcNow)
        {
            throw new BusinessRuleException("SendAt must be in the future.");
        }

        var connection = await _connections.GetByIdTrackedAsync(request.ConnectionId, ct)
            ?? throw new NotFoundException("Connection", request.ConnectionId);

        if (connection.UserId != userId)
        {
            throw new NotFoundException("Connection", request.ConnectionId);
        }

        if (connection.ServiceType != ServiceType.Gmail)
        {
            throw new BusinessRuleException("Only Gmail connections can be used to send emails.");
        }

        var email = new ScheduledEmail
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConnectionId = request.ConnectionId,
            ToJson = JsonSerializer.Serialize(request.To),
            CcJson = JsonSerializer.Serialize(request.Cc),
            BccJson = JsonSerializer.Serialize(request.Bcc),
            Subject = request.Subject,
            BodyHtml = request.BodyHtml,
            SendAt = request.SendAt,
            Status = ScheduledEmailStatus.Pending,
            RetryCount = 0
        };

        await _scheduledEmails.AddAsync(email, ct);
        await _scheduledEmails.SaveChangesAsync(ct);

        return MapToDto(email);
    }

    public async Task<IReadOnlyList<ScheduledEmailDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await _scheduledEmails.GetByUserIdAsync(userId, ct);
        return items.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<ScheduledEmailDto> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var email = await _scheduledEmails.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("ScheduledEmail", id);

        if (email.UserId != userId)
        {
            throw new NotFoundException("ScheduledEmail", id);
        }

        return MapToDto(email);
    }

    public async Task<ScheduledEmailDto> CancelAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var email = await _scheduledEmails.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("ScheduledEmail", id);

        if (email.UserId != userId)
        {
            throw new NotFoundException("ScheduledEmail", id);
        }

        if (email.Status is ScheduledEmailStatus.Sent or ScheduledEmailStatus.Failed)
        {
            throw new BusinessRuleException($"Cannot cancel an email with status '{email.Status}'.");
        }

        if (email.Status == ScheduledEmailStatus.Cancelled)
        {
            return MapToDto(email);
        }

        email.Status = ScheduledEmailStatus.Cancelled;
        _scheduledEmails.Update(email);
        await _scheduledEmails.SaveChangesAsync(ct);

        return MapToDto(email);
    }

    private static ScheduledEmailDto MapToDto(ScheduledEmail email)
    {
        return new ScheduledEmailDto
        {
            Id = email.Id,
            ConnectionId = email.ConnectionId,
            To = string.IsNullOrEmpty(email.ToJson) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(email.ToJson)!,
            Cc = string.IsNullOrEmpty(email.CcJson) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(email.CcJson)!,
            Bcc = string.IsNullOrEmpty(email.BccJson) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(email.BccJson)!,
            Subject = email.Subject,
            BodyHtml = email.BodyHtml,
            SendAt = email.SendAt,
            Status = email.Status.ToString(),
            RetryCount = email.RetryCount,
            LastError = email.LastError,
            SentAt = email.SentAt,
            CreatedAt = email.CreatedAt
        };
    }
}
