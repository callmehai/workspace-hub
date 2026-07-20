using System.Text;
using FluentAssertions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.OAuth.Core;
using Xunit;

namespace WorkspaceHub.Tests.OAuth;

/// <summary>
/// Tests cho <see cref="IdTokenParser"/> — decode id_token (KHÔNG verify chữ ký) để lấy
/// ProviderAccountId. Thứ tự ưu tiên theo source: claim <c>email</c> trước, không có mới lấy
/// <c>sub</c>; mọi trường hợp không xác định được đều ném <see cref="BusinessRuleException"/>.
/// </summary>
public class IdTokenParserTests
{
    /// <summary>Encode base64url (bỏ '=' padding) đúng như JWT thật.</summary>
    private static string Base64Url(string raw) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>Ghép JWT giả: header.payload.signature — signature không được verify nên để chuỗi bất kỳ.</summary>
    private static string MakeJwt(string payloadJson)
    {
        var header = Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        return $"{header}.{Base64Url(payloadJson)}.ZmFrZS1zaWduYXR1cmU";
    }

    [Fact]
    public void ExtractProviderAccountId_TokenCoEmail_TraVeEmail()
    {
        // email được ưu tiên hơn sub (theo source) vì dễ đối chiếu tài khoản hơn.
        var token = MakeJwt("{\"sub\":\"1234567890\",\"email\":\"user@gmail.com\"}");

        IdTokenParser.ExtractProviderAccountId(token).Should().Be("user@gmail.com");
    }

    [Fact]
    public void ExtractProviderAccountId_TokenChiCoSub_TraVeSub()
    {
        var token = MakeJwt("{\"sub\":\"1234567890\",\"iss\":\"https://accounts.google.com\"}");

        IdTokenParser.ExtractProviderAccountId(token).Should().Be("1234567890");
    }

    [Fact]
    public void ExtractProviderAccountId_EmailRong_FallbackSangSub()
    {
        // Claim email tồn tại nhưng rỗng → coi như không có, rơi xuống sub.
        var token = MakeJwt("{\"email\":\"\",\"sub\":\"sub-fallback\"}");

        IdTokenParser.ExtractProviderAccountId(token).Should().Be("sub-fallback");
    }

    [Fact]
    public void ExtractProviderAccountId_PayloadCanPadding_VanParseDuoc()
    {
        // Payload có độ dài khiến base64 phải bỏ padding '=' → parser phải tự bù lại.
        const string payload = "{\"sub\":\"pad-me\"}";
        var encoded = Base64Url(payload);
        encoded.Length.Should().NotBe(0);
        (encoded.Length % 4).Should().NotBe(0, "test này chỉ có ý nghĩa khi segment thiếu padding");

        IdTokenParser.ExtractProviderAccountId(MakeJwt(payload)).Should().Be("pad-me");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractProviderAccountId_TokenRong_NemBusinessRuleException(string? idToken)
    {
        var act = () => IdTokenParser.ExtractProviderAccountId(idToken);

        act.Should().Throw<BusinessRuleException>()
            .WithMessage("Không thể xác định tài khoản Google");
    }

    [Theory]
    [InlineData("not-a-jwt")]                 // không có dấu chấm nào
    [InlineData("header.payload")]            // chỉ 2 phần
    [InlineData("a.b.c.d")]                   // 4 phần (không phải JWS/JWE hợp lệ)
    public void ExtractProviderAccountId_SaiDinhDangJwt_NemBusinessRuleException(string idToken)
    {
        var act = () => IdTokenParser.ExtractProviderAccountId(idToken);

        act.Should().Throw<BusinessRuleException>()
            .WithMessage("Không thể xác định tài khoản Google");
    }

    [Fact]
    public void ExtractProviderAccountId_PayloadKhongPhaiJson_NemBusinessRuleException()
    {
        // Đủ 3 segment base64url nhưng nội dung không decode ra JSON → catch-all trong source.
        var act = () => IdTokenParser.ExtractProviderAccountId("aaaa.bbbb.cccc");

        act.Should().Throw<BusinessRuleException>()
            .WithMessage("Không thể xác định tài khoản Google");
    }

    [Fact]
    public void ExtractProviderAccountId_PayloadThieuEmailVaSub_NemBusinessRuleException()
    {
        var token = MakeJwt("{\"iss\":\"https://accounts.google.com\",\"aud\":\"client-1\"}");

        var act = () => IdTokenParser.ExtractProviderAccountId(token);

        act.Should().Throw<BusinessRuleException>()
            .WithMessage("Không thể xác định tài khoản Google");
    }

    [Fact]
    public void ExtractProviderAccountId_ProviderNameTuyChon_HienTrongMessageLoi()
    {
        var act = () => IdTokenParser.ExtractProviderAccountId(null, "Atlassian");

        act.Should().Throw<BusinessRuleException>()
            .WithMessage("Không thể xác định tài khoản Atlassian");
    }
}
