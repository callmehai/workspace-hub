using System.IdentityModel.Tokens.Jwt;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Application.OAuth.Core;

/// <summary>
/// Decode JWT id_token để lấy email/sub.
/// KHÔNG verify signature — chỉ dùng để extract ProviderAccountId.
/// </summary>
public static class IdTokenParser
{
    public static string ExtractProviderAccountId(string? idToken, string providerName = "Google")
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new BusinessRuleException($"Không thể xác định tài khoản {providerName}");

        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(idToken))
                throw new BusinessRuleException($"Không thể xác định tài khoản {providerName}");

            var jwtToken = handler.ReadJwtToken(idToken);

            var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
            if (!string.IsNullOrWhiteSpace(email))
                return email;

            var sub = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            if (!string.IsNullOrWhiteSpace(sub))
                return sub;

            throw new BusinessRuleException($"Không thể xác định tài khoản {providerName}");
        }
        catch (BusinessRuleException)
        {
            throw;
        }
        catch
        {
            throw new BusinessRuleException($"Không thể xác định tài khoản {providerName}");
        }
    }
}
