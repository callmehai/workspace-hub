namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>
/// Body trả về sau login/register/google (SCRUM-62). KHÔNG còn chứa access token —
/// token được set vào HttpOnly cookie <c>wh_access</c>. Client chỉ cần biết thông tin
/// user + thời hạn (để chủ động refresh trước khi hết hạn).
/// </summary>
public record AuthResultDto(int ExpiresIn, UserDto User);
