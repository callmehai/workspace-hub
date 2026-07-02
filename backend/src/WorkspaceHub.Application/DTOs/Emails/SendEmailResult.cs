namespace WorkspaceHub.Application.DTOs.Emails;

/// <summary>Kết quả gửi email trực tiếp: messageId Gmail của thư đã gửi + thời điểm gửi (UTC).</summary>
public record SendEmailResult(string MessageId, DateTime SentAt);
