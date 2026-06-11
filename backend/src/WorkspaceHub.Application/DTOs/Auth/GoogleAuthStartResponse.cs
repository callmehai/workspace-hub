namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>Authorization URL + state returned by POST /api/auth/google/start.</summary>
public record GoogleAuthStartResponse(string AuthorizationUrl, string State);
