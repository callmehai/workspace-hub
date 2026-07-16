namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Verifies a Google id_token and extracts the payload. Implementation uses Google.Apis.Auth.</summary>
public interface IGoogleTokenVerifier
{
    /// <summary>
    /// Validates the id_token signature and audience, returns (Sub, Email, Name).
    /// Name is the display name from the Google account (claim "name" in id_token).
    /// Throws BusinessRuleException if invalid.
    /// </summary>
    Task<(string Sub, string Email, string Name)> VerifyAsync(string idToken, CancellationToken ct = default);
}
