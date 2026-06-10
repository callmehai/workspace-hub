using Microsoft.AspNetCore.DataProtection;
using WorkspaceHub.Application.Security;

namespace WorkspaceHub.Infrastructure.Security;

public class DataProtectionTokenProtector : ITokenProtector
{
    private readonly IDataProtector _protector;

    // Purpose string CỐ ĐỊNH — không được đổi sau khi đã có token trong DB
    private const string Purpose = "WorkspaceHub.OAuthTokens.v1";

    public DataProtectionTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plainToken)
        => _protector.Protect(plainToken);

    public string Unprotect(string protectedToken)
        => _protector.Unprotect(protectedToken);
}