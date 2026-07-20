using FluentAssertions;
using WorkspaceHub.Application.DTOs.Auth;
using WorkspaceHub.Application.Validators;

namespace WorkspaceHub.Tests.Validators;

/// <summary>
/// Unit test cho nhóm validator Auth: Register / Login / ChangePassword / UpdateProfile.
/// Chỉ test đúng rule khai báo trong từng validator (không suy diễn thêm).
/// </summary>
public class AuthValidatorsTests
{
    // ── RegisterRequestValidator ──────────────────────────────────────────

    private readonly RegisterRequestValidator _register = new();

    /// <summary>Request hợp lệ: email đúng format, password ≥ 8, fullName không rỗng.</summary>
    private static RegisterRequest ValidRegister() =>
        new("user@example.com", "matkhau123", "Nguyễn Văn A");

    [Fact]
    public void Validate_RegisterValidRequest_ShouldPass()
    {
        var result = _register.Validate(ValidRegister());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RegisterEmptyEmail_ShouldFail()
    {
        var request = ValidRegister() with { Email = "" };
        var result = _register.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_RegisterMalformedEmail_ShouldFail()
    {
        var request = ValidRegister() with { Email = "khong-phai-email" };
        var result = _register.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_RegisterPasswordShorterThan8_ShouldFail()
    {
        var request = ValidRegister() with { Password = "1234567" };
        var result = _register.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void Validate_RegisterEmptyFullName_ShouldFail()
    {
        var request = ValidRegister() with { FullName = "" };
        var result = _register.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FullName");
    }

    [Fact]
    public void Validate_RegisterFullNameOver200Chars_ShouldFail()
    {
        var request = ValidRegister() with { FullName = new string('a', 201) };
        var result = _register.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FullName");
    }

    [Fact]
    public void Validate_RegisterWithInviteToken_ShouldPass()
    {
        // InviteToken không có rule nào — luôn hợp lệ (kể cả null).
        var request = ValidRegister() with { InviteToken = "abc-token" };
        _register.Validate(request).IsValid.Should().BeTrue();
    }

    // ── LoginRequestValidator ─────────────────────────────────────────────

    private readonly LoginRequestValidator _login = new();

    [Fact]
    public void Validate_LoginValidRequest_ShouldPass()
    {
        var result = _login.Validate(new LoginRequest("user@example.com", "matkhau123"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_LoginEmptyEmail_ShouldFail()
    {
        var result = _login.Validate(new LoginRequest("", "matkhau123"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_LoginMalformedEmail_ShouldFail()
    {
        var result = _login.Validate(new LoginRequest("abc.example.com", "matkhau123"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_LoginEmptyPassword_ShouldFail()
    {
        var result = _login.Validate(new LoginRequest("user@example.com", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void Validate_LoginShortPassword_ShouldPass()
    {
        // Login KHÔNG có rule MinimumLength (khác Register) — mật khẩu ngắn vẫn qua validator.
        var result = _login.Validate(new LoginRequest("user@example.com", "123"));
        result.IsValid.Should().BeTrue();
    }

    // ── ChangePasswordRequestValidator ────────────────────────────────────

    private readonly ChangePasswordRequestValidator _changePassword = new();

    [Fact]
    public void Validate_ChangePasswordValidRequest_ShouldPass()
    {
        var result = _changePassword.Validate(new ChangePasswordRequest("matkhaucu1", "matkhaumoi1"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ChangePasswordEmptyCurrentPassword_ShouldFail()
    {
        var result = _changePassword.Validate(new ChangePasswordRequest("", "matkhaumoi1"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CurrentPassword");
    }

    [Fact]
    public void Validate_ChangePasswordEmptyNewPassword_ShouldFail()
    {
        var result = _changePassword.Validate(new ChangePasswordRequest("matkhaucu1", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewPassword");
    }

    [Fact]
    public void Validate_ChangePasswordNewPasswordShorterThan8_ShouldFail()
    {
        var result = _changePassword.Validate(new ChangePasswordRequest("matkhaucu1", "1234567"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewPassword");
    }

    // ── UpdateProfileRequestValidator ─────────────────────────────────────

    private readonly UpdateProfileRequestValidator _updateProfile = new();

    [Fact]
    public void Validate_UpdateProfileValidRequest_ShouldPass()
    {
        var result = _updateProfile.Validate(new UpdateProfileRequest("Trần Thị B"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UpdateProfileEmptyFullName_ShouldFail()
    {
        var result = _updateProfile.Validate(new UpdateProfileRequest(""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FullName");
    }

    [Fact]
    public void Validate_UpdateProfileFullNameOver200Chars_ShouldFail()
    {
        var result = _updateProfile.Validate(new UpdateProfileRequest(new string('x', 201)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FullName");
    }

    [Fact]
    public void Validate_UpdateProfileFullNameExactly200Chars_ShouldPass()
    {
        var result = _updateProfile.Validate(new UpdateProfileRequest(new string('x', 200)));
        result.IsValid.Should().BeTrue();
    }
}
