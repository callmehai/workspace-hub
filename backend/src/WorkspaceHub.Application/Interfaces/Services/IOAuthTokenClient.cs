namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>Abstraction cho HTTP token exchange — implement ở Infrastructure.</summary>
public interface IOAuthTokenClient
{
    /// <summary>POST form-encoded data lên tokenEndpoint, trả về raw JSON string.</summary>
    Task<string> PostFormAsync(string tokenEndpoint, Dictionary<string, string> formData, CancellationToken ct = default);
}
