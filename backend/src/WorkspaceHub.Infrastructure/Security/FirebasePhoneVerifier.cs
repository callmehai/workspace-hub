using FirebaseAdmin.Auth;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Services;

namespace WorkspaceHub.Infrastructure.Security;

/// <summary>
/// Verifies Firebase id_token using FirebaseAdmin.Auth.
/// </summary>
public class FirebasePhoneVerifier : IFirebasePhoneVerifier
{
    public async Task<string> VerifyPhoneTokenAsync(string firebaseToken, CancellationToken ct = default)
    {
        try
        {
            var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(firebaseToken, ct);
            if (decodedToken.Claims.TryGetValue("phone_number", out var phoneNumberObj) && phoneNumberObj is string phoneNumber)
            {
                return phoneNumber;
            }
            throw new BusinessRuleException("Firebase token không chứa số điện thoại.");
        }
        catch (FirebaseAuthException)
        {
            throw new BusinessRuleException("Mã OTP không hợp lệ hoặc đã hết hạn.");
        }
        catch (ArgumentException)
        {
            throw new BusinessRuleException("Firebase token không hợp lệ.");
        }
    }
}
