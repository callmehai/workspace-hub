using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Infrastructure.Security;

/// <summary>
/// Verifies Google id_token using Google.Apis.Auth (signature + audience check).
/// ClientId read from config: OAuth:google:ClientId (primary) or Google:ClientId (fallback).
/// </summary>
public class GoogleTokenVerifier : IGoogleTokenVerifier
{
    private readonly string _clientId;

    public GoogleTokenVerifier(IConfiguration config)
    {
        _clientId = config["OAuth:google:ClientId"]
                    ?? config["Google:ClientId"]
                    ?? throw new InvalidOperationException("Google ClientId is not configured.");
    }

    public async Task<(string Sub, string Email)> VerifyAsync(string idToken, CancellationToken ct = default)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _clientId }
            };
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return (payload.Subject, payload.Email);
        }
        catch (InvalidJwtException ex)
        {
            throw new BusinessRuleException($"Invalid Google id_token: {ex.Message}");
        }
    }
}
