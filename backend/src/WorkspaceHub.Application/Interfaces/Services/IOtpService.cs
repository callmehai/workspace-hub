namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// OTP xác minh SĐT (SCRUM-64). Mã 6 số, lưu HASH ở Redis (key otp:{userId}), TTL ngắn,
/// có cooldown gửi lại + giới hạn số lần verify sai. Đi qua ISmsSender để gửi.
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Sinh + gửi OTP tới SĐT của user. Throw BusinessRuleException nếu đang trong cooldown
    /// (gửi lại quá nhanh). Trả số giây phải đợi trước lần gửi tiếp theo (để FE đếm ngược).
    /// </summary>
    Task<int> SendAsync(Guid userId, string phoneE164, CancellationToken ct = default);

    /// <summary>
    /// Verify mã user nhập. Đúng → xoá OTP, trả true. Sai → tăng đếm, trả false; quá số lần
    /// cho phép → xoá OTP (phải gửi lại). Hết hạn/không có → BusinessRuleException.
    /// </summary>
    Task<bool> VerifyAsync(Guid userId, string code, CancellationToken ct = default);
}
