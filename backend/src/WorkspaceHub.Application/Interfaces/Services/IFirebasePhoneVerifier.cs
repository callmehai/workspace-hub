namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Verifies a Firebase id_token and extracts the phone_number payload.</summary>
public interface IFirebasePhoneVerifier
{
    /// <summary>
    /// Validates the id_token signature and audience, returns the normalized phone number.
    /// Throws BusinessRuleException if invalid.
    /// </summary>
    Task<string> VerifyPhoneTokenAsync(string firebaseToken, CancellationToken ct = default);
}
