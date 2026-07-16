namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Gửi email TRANSACTIONAL từ SENDER HỆ THỐNG (SCRUM-64) — vd OTP đăng ký.
///
/// ⚠️ KHÔNG nhầm với <c>IGmailGateway</c>: đó là luồng gửi mail NGHIỆP VỤ qua Gmail
/// CỦA USER (SendEmail / ScheduledEmails / mail mời kết bạn), cần một Connection OAuth.
/// Sender này là địa chỉ hệ thống (Resend), không phụ thuộc user đã đăng nhập/connect hay chưa —
/// OTP xảy ra TRƯỚC khi user có Connection nên bắt buộc phải là sender hệ thống.
///
/// Impl: <c>ResendEmailSender</c> (prod/thật) hoặc <c>LogEmailSender</c> (dev — ghi OTP ra log).
/// </summary>
public interface ISystemEmailSender
{
    /// <summary>
    /// Gửi email tới <paramref name="toEmail"/>. Cung cấp cả <paramref name="htmlBody"/> và
    /// <paramref name="textBody"/> (plain text) — text giúp deliverability tốt hơn và để
    /// dev đọc OTP dễ trong log. Lỗi provider → throw <c>ProviderException</c> (caller xử lý).
    /// </summary>
    Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default);
}
