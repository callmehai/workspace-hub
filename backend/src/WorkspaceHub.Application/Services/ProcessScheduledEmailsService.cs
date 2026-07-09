using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.DTOs.Emails;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Cron processor cho email hẹn giờ (SCRUM-31). Quét batch Pending tới hạn → gửi qua Gmail.
/// Idempotent theo Status: chỉ pick Pending, set Sent/Failed nên chạy lại không gửi trùng.
/// </summary>
public class ProcessScheduledEmailsService : IProcessScheduledEmailsService
{
    private readonly IScheduledEmailRepository _scheduledEmails;
    private readonly IConnectionRepository _connections;
    private readonly IGmailGateway _gmail;
    private readonly ILogger<ProcessScheduledEmailsService> _logger;

    public ProcessScheduledEmailsService(
        IScheduledEmailRepository scheduledEmails,
        IConnectionRepository connections,
        IGmailGateway gmail,
        ILogger<ProcessScheduledEmailsService> logger)
    {
        _scheduledEmails = scheduledEmails;
        _connections = connections;
        _gmail = gmail;
        _logger = logger;
    }

    public async Task<ProcessScheduledResult> ProcessDueEmailsAsync(int maxBatch = 50, CancellationToken ct = default)
    {
        var due = await _scheduledEmails.GetPendingDueEmailsAsync(DateTime.UtcNow, maxBatch, ct);
        if (due.Count == 0)
        {
            return new ProcessScheduledResult(0, 0, 0);
        }

        int sent = 0, failed = 0;

        // Cache connection theo Id trong 1 lượt — nhiều email có thể chung 1 connection.
        var connectionCache = new Dictionary<Guid, Connection?>();

        foreach (var email in due)
        {
            try
            {
                if (!connectionCache.TryGetValue(email.ConnectionId, out var connection))
                {
                    connection = await _connections.GetByIdTrackedAsync(email.ConnectionId, ct);
                    connectionCache[email.ConnectionId] = connection;
                }

                if (connection is null)
                {
                    MarkFailed(email, "Connection not found.");
                    failed++;
                    continue;
                }

                if (connection.ServiceType != ServiceType.Gmail)
                {
                    MarkFailed(email, "Connection is not a Gmail connection.");
                    failed++;
                    continue;
                }

                if (connection.Status != ConnectionStatus.Active)
                {
                    MarkFailed(email, $"Connection is not active (status: {connection.Status}).");
                    failed++;
                    continue;
                }

                var attachments = DecodeAttachments(email.AttachmentsJson);

                await _gmail.SendMessageAsync(
                    connection,
                    Deserialize(email.ToJson),
                    Deserialize(email.CcJson),
                    Deserialize(email.BccJson),
                    email.Subject,
                    email.BodyHtml,
                    attachments.Count > 0 ? attachments : null,
                    ct);

                email.Status = ScheduledEmailStatus.Sent;
                email.SentAt = DateTime.UtcNow;
                email.LastError = null;
                sent++;
            }
            catch (Exception ex)
            {
                MarkFailed(email, ex.Message);
                failed++;
                _logger.LogWarning(ex, "Failed to send scheduled email {EmailId}", email.Id);
            }
        }

        await _scheduledEmails.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Processed {Total} scheduled emails: {Sent} sent, {Failed} failed.",
            due.Count, sent, failed);

        return new ProcessScheduledResult(due.Count, sent, failed);
    }

    private static void MarkFailed(ScheduledEmail email, string error)
    {
        email.Status = ScheduledEmailStatus.Failed;
        email.RetryCount++;
        email.LastError = error;
    }

    private static IReadOnlyList<string> Deserialize(string? json)
        => string.IsNullOrEmpty(json)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

    /// <summary>Decode JSON base64 attachments (đã lưu lúc tạo scheduled) → binary để gắn vào MIME.</summary>
    private static IReadOnlyList<GmailAttachmentData> DecodeAttachments(string? json)
    {
        var list = new List<GmailAttachmentData>();
        if (string.IsNullOrEmpty(json)) return list;

        var uploads = JsonSerializer.Deserialize<List<AttachmentUpload>>(json);
        if (uploads == null) return list;

        foreach (var u in uploads)
        {
            byte[] data;
            try { data = Convert.FromBase64String(u.ContentBase64); }
            catch (FormatException) { continue; } // bỏ qua file hỏng, vẫn gửi phần còn lại

            var mime = string.IsNullOrWhiteSpace(u.MimeType) ? "application/octet-stream" : u.MimeType;
            list.Add(new GmailAttachmentData(data, u.Filename, mime, data.Length));
        }
        return list;
    }
}
