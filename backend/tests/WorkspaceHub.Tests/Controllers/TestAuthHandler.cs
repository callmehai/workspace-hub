using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WorkspaceHub.Tests.Controllers;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string DefaultUserId = "11111111-1111-1111-1111-111111111111";
    public const string User2Id = "22222222-2222-2222-2222-222222222222";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, 
        ILoggerFactory logger, 
        UrlEncoder encoder) 
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            if (authHeader.ToString().StartsWith("Bearer TestToken_User1"))
            {
                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, DefaultUserId) };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Test");

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
            if (authHeader.ToString().StartsWith("Bearer TestToken_User2"))
            {
                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, User2Id) };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Test");

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
        }

        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
