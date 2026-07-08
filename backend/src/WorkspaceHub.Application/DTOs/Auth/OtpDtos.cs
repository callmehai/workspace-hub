namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>
/// Kết quả đăng ký (SCRUM-64). KHÔNG phát token ngay — user phải verify OTP trước.
/// FE dùng email + cooldown để mở màn nhập OTP.
/// </summary>
public record RegisterResult(string Email, bool RequiresPhoneVerification, int ResendCooldownSeconds);

/// <summary>Yêu cầu verify số điện thoại qua Firebase ID Token.</summary>
public record VerifyPhoneRequest(string Email, string FirebaseToken);
