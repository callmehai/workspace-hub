namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>
/// Kết quả đăng ký (SCRUM-64). KHÔNG phát token ngay — user phải verify OTP trước.
/// FE dùng email + cooldown để mở màn nhập OTP.
/// </summary>
public record RegisterResult(string Email, bool RequiresEmailVerification, int ResendCooldownSeconds);

/// <summary>Yêu cầu gửi lại OTP (theo email — FE luôn có sẵn từ form).</summary>
public record SendOtpRequest(string Email);

/// <summary>Yêu cầu verify OTP.</summary>
public record VerifyOtpRequest(string Email, string Code);
