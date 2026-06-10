using Microsoft.AspNetCore.DataProtection;
using WorkspaceHub.Infrastructure.Security;
using Xunit;

namespace WorkspaceHub.Tests.Security;

public class TokenProtectorTests
{
    [Fact]
    public void Protect_Then_Unprotect_Returns_Original_Token()
    {
        // Arrange — chuẩn bị
        var provider = DataProtectionProvider.Create("WorkspaceHub.Tests");
        var protector = new DataProtectionTokenProtector(provider);
        var original = "ya29.fake-access-token-123";

        // Act — thực hiện
        var encrypted = protector.Protect(original);
        var decrypted = protector.Unprotect(encrypted);

        // Assert — kiểm chứng
        Assert.NotEqual(original, encrypted); // đã được mã hoá
        Assert.Equal(original, decrypted);    // giải mã ra ĐÚNG gốc
    }
}