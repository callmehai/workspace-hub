namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Gửi SMS (SCRUM-64). Impl: TwilioSmsSender (prod/trial) hoặc LogSmsSender (dev — ghi log).
/// Tách interface ở Application để service không phụ thuộc provider cụ thể.
/// </summary>
public interface ISmsSender
{
    /// <summary>Gửi tin nhắn tới số E.164. Lỗi provider → throw (caller xử lý).</summary>
    Task SendAsync(string toPhoneE164, string message, CancellationToken ct = default);
}
