namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>PATCH /api/users/me — hiện chỉ đổi FullName (SCRUM-75 follow-up).</summary>
public record UpdateProfileRequest(string FullName);
