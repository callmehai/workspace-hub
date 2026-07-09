using System.Text.Json;
using WorkspaceHub.Application.DTOs.ScheduledEmails;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Application.Mapping;

public static class ScheduledEmailMapper
{
    public static ScheduledEmailDto ToDto(ScheduledEmail email) => new()
    {
        Id = email.Id,
        ConnectionId = email.ConnectionId,
        To = DeserializeList(email.ToJson),
        Cc = DeserializeList(email.CcJson),
        Bcc = DeserializeList(email.BccJson),
        Subject = email.Subject,
        BodyHtml = email.BodyHtml,
        SendAt = email.SendAt,
        Status = email.Status.ToString(),
        RetryCount = email.RetryCount,
        LastError = email.LastError,
        SentAt = email.SentAt,
        CreatedAt = email.CreatedAt,
    };

    private static List<string> DeserializeList(string? json) =>
        string.IsNullOrEmpty(json) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(json)!;
}
