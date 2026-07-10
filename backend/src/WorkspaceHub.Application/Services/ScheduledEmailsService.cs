using System.Text.Json;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
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

    public IQueryable<ScheduledEmailDto> GetByUserId(Guid userId) =>
        _scheduledEmails.GetByUserId(userId);

    public async Task<ScheduledEmailDto> CreateAsync(Guid userId, CreateScheduledEmailRequest request, CancellationToken ct = default)
    {
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
            AttachmentsJson = JsonSerializer.Serialize(request.Attachments),
            SendAt = request.SendAt,
            Status = ScheduledEmailStatus.Pending,
            RetryCount = 0
        };

        await _scheduledEmails.AddAsync(email, ct);
        await _scheduledEmails.SaveChangesAsync(ct);

        return ScheduledEmailMapper.ToDto(email);
    }

    public async Task<ScheduledEmailDto> GetByIdAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var email = await _scheduledEmails.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("ScheduledEmail", id);

        if (email.UserId != userId)
        {
            throw new NotFoundException("ScheduledEmail", id);
        }

        return ScheduledEmailMapper.ToDto(email);
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
            return ScheduledEmailMapper.ToDto(email);
        }

        email.Status = ScheduledEmailStatus.Cancelled;
        _scheduledEmails.Update(email);
        await _scheduledEmails.SaveChangesAsync(ct);

        return ScheduledEmailMapper.ToDto(email);
    }
}
