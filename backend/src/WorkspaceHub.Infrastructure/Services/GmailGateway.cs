using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

public class GmailGateway : IGmailGateway
{
    private readonly ITokenService _tokenService;

    public GmailGateway(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public async Task<GmailProfile> GetProfileAsync(Connection connection, CancellationToken ct = default)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);

        var credential = GoogleCredential.FromAccessToken(accessToken);
        var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WorkspaceHub"
        });

        var profile = await gmail.Users.GetProfile("me").ExecuteAsync(ct);

        return new GmailProfile(
            profile.EmailAddress,
            profile.HistoryId,
            profile.MessagesTotal,
            profile.ThreadsTotal);
    }
}
