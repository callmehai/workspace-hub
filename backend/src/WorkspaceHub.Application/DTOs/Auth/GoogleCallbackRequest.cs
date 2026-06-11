namespace WorkspaceHub.Application.DTOs.Auth;

/// <summary>Code + state from Google OAuth callback.</summary>
public record GoogleCallbackRequest(string Code, string State);
