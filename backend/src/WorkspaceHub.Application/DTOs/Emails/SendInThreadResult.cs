namespace WorkspaceHub.Application.DTOs.Emails;

/// <summary>Kết quả gửi email trong thread (reply/forward): messageId + threadId + thời điểm gửi.</summary>
public record SendInThreadResult(string MessageId, string ThreadId, DateTime SentAt);
