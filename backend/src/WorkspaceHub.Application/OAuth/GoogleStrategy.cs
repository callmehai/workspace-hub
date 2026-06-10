using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.OAuth;

/// <summary>Strategy Google — build URL với service scopes (Gmail/GCal/Drive) + include_granted_scopes.</summary>
public class GoogleStrategy : IProviderStrategy
{
    public string ProviderKey => "google";

    public Task<InitiateConnectionResult> BuildAuthUrlAsync(
        ProviderStrategyContext ctx,
        CancellationToken ct = default)
    {
        var scopes = string.Join(' ',
            GoogleScopes.ForService(ServiceType.Gmail),
            GoogleScopes.ForService(ServiceType.GCal),
            GoogleScopes.ForService(ServiceType.Drive));

        var builder = new GoogleAuthUrlBuilder(ctx.ClientId, ctx.RedirectUri);
        var url = builder.BuildForService(scopes, ctx.State);

        return Task.FromResult(new InitiateConnectionResult(url, ctx.State));
    }
}
