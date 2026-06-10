using System.Text;
using System.Text.Json;
using WorkspaceHub.Application.Common;

namespace WorkspaceHub.Application.OAuth;

/// <summary>
/// Decode JWT id_token (Base64Url middle segment) để lấy email/sub.
/// KHÔNG verify signature — chỉ dùng để extract ProviderAccountId.
/// </summary>
public static class IdTokenParser
{
    public static string ExtractProviderAccountId(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new BusinessRuleException("Không thể xác định tài khoản Google");

        try
        {
            var segments = idToken.Split('.');
            if (segments.Length < 2)
                throw new BusinessRuleException("Không thể xác định tài khoản Google");

            // Pad Base64Url về Base64 chuẩn trước khi decode.
            var payload = segments[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var claims = JsonDocument.Parse(json).RootElement;

            if (claims.TryGetProperty("email", out var email) &&
                email.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(email.GetString()))
                return email.GetString()!;

            if (claims.TryGetProperty("sub", out var sub) &&
                sub.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(sub.GetString()))
                return sub.GetString()!;

            throw new BusinessRuleException("Không thể xác định tài khoản Google");
        }
        catch (BusinessRuleException)
        {
            throw;
        }
        catch
        {
            throw new BusinessRuleException("Không thể xác định tài khoản Google");
        }
    }
}
