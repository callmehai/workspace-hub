using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Avatar upload/xoá qua IFileStorageService (R2) + đổi tên + đổi mật khẩu (SCRUM-75).
/// Điểm dễ sai: so sánh avatar cũ theo KEY (không phải URL, vì URL luôn đổi do cache-bust ?v=).
/// </summary>
public class UserProfileServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IFileStorageService> _storage = new();
    private readonly UserProfileService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public UserProfileServiceTests()
    {
        _service = new UserProfileService(_users.Object, _storage.Object);
    }

    private User MakeUser(string? avatarUrl = null, string? passwordHash = null, AuthProvider provider = AuthProvider.Local)
        => new()
        {
            Id = _userId,
            Email = "user@example.com",
            FullName = "Nguyen Van A",
            AvatarUrl = avatarUrl,
            PasswordHash = passwordHash,
            Role = UserRole.User,
            AuthProvider = provider
        };

    private void SetupUser(User? user)
        => _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

    private static MemoryStream Bytes(int size = 128) => new(new byte[size]);

    // ───────────────────────── UploadAvatarAsync ─────────────────────────

    [Fact]
    public async Task UploadAvatar_NoPreviousAvatar_UploadsAndPersistsUrl()
    {
        var user = MakeUser();
        SetupUser(user);
        var expectedKey = $"avatars/{_userId}.png";
        _storage.Setup(s => s.UploadAsync(expectedKey, It.IsAny<Stream>(), "image/png", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://cdn.example.com/avatars/new.png?v=abc");

        var result = await _service.UploadAvatarAsync(_userId, Bytes(), "image/png", ".png");

        result.AvatarUrl.Should().Be("https://cdn.example.com/avatars/new.png?v=abc");
        user.AvatarUrl.Should().Be("https://cdn.example.com/avatars/new.png?v=abc");
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // Không có avatar cũ → không gọi xoá.
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadAvatar_KeyIsUserIdPlusExtension()
    {
        var user = MakeUser();
        SetupUser(user);
        string? usedKey = null;
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string k, Stream _, string _, CancellationToken _) => usedKey = k)
            .ReturnsAsync("https://cdn.example.com/avatars/x.jpg");

        await _service.UploadAvatarAsync(_userId, Bytes(), "image/jpeg", ".jpg");

        usedKey.Should().Be($"avatars/{_userId}.jpg");
    }

    [Fact]
    public async Task UploadAvatar_PreviousAvatarDifferentExtension_DeletesOldObject()
    {
        // Avatar cũ .png, ảnh mới .jpg → key khác nhau → phải xoá object cũ để không rác trên R2.
        var user = MakeUser(avatarUrl: $"https://cdn.example.com/avatars/{_userId}.png?v=old");
        SetupUser(user);
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync($"https://cdn.example.com/avatars/{_userId}.jpg?v=new");

        await _service.UploadAvatarAsync(_userId, Bytes(), "image/jpeg", ".jpg");

        _storage.Verify(s => s.DeleteAsync($"avatars/{_userId}.png", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAvatar_PreviousAvatarSameKey_DoesNotDelete()
    {
        // Cùng định dạng → upload ghi đè cùng key; URL đổi vì query cache-bust, nhưng KEY giống nhau
        // nên KHÔNG được xoá (xoá sẽ mất luôn ảnh vừa upload).
        var user = MakeUser(avatarUrl: $"https://cdn.example.com/avatars/{_userId}.png?v=old");
        SetupUser(user);
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync($"https://cdn.example.com/avatars/{_userId}.png?v=new");

        await _service.UploadAvatarAsync(_userId, Bytes(), "image/png", ".png");

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    public async Task UploadAvatar_ContentTypeNotAllowed_Throws422(string contentType)
    {
        var act = () => _service.UploadAvatarAsync(_userId, Bytes(), contentType, ".gif");

        await act.Should().ThrowAsync<BusinessRuleException>();
        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("IMAGE/PNG")]
    [InlineData("Image/Jpeg")]
    [InlineData("image/webp")]
    public async Task UploadAvatar_ContentTypeCaseInsensitive_Accepted(string contentType)
    {
        var user = MakeUser();
        SetupUser(user);
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://cdn.example.com/avatars/x.png");

        var result = await _service.UploadAvatarAsync(_userId, Bytes(), contentType, ".png");

        result.AvatarUrl.Should().Be("https://cdn.example.com/avatars/x.png");
    }

    [Fact]
    public async Task UploadAvatar_LargerThan5Mb_Throws422()
    {
        using var tooBig = new MemoryStream(new byte[5 * 1024 * 1024 + 1]);

        var act = () => _service.UploadAvatarAsync(_userId, tooBig, "image/png", ".png");

        await act.Should().ThrowAsync<BusinessRuleException>();
        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadAvatar_UserNotFound_Throws404()
    {
        SetupUser(null);

        var act = () => _service.UploadAvatarAsync(_userId, Bytes(), "image/png", ".png");

        await act.Should().ThrowAsync<NotFoundException>();
        _storage.Verify(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ───────────────────────── DeleteAvatarAsync ─────────────────────────

    [Fact]
    public async Task DeleteAvatar_HasAvatar_RemovesObjectAndClearsUrl()
    {
        var user = MakeUser(avatarUrl: $"https://cdn.example.com/avatars/{_userId}.png?v=old");
        SetupUser(user);

        var result = await _service.DeleteAvatarAsync(_userId);

        _storage.Verify(s => s.DeleteAsync($"avatars/{_userId}.png", It.IsAny<CancellationToken>()), Times.Once);
        user.AvatarUrl.Should().BeNull();
        result.AvatarUrl.Should().BeNull();
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAvatar_NoAvatar_NoStorageCallNoSave()
    {
        var user = MakeUser();
        SetupUser(user);

        var result = await _service.DeleteAvatarAsync(_userId);

        result.AvatarUrl.Should().BeNull();
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAvatar_UserNotFound_Throws404()
    {
        SetupUser(null);

        var act = () => _service.DeleteAvatarAsync(_userId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ───────────────────────── UpdateProfileAsync ─────────────────────────

    [Fact]
    public async Task UpdateProfile_TrimsFullNameAndSaves()
    {
        var user = MakeUser();
        SetupUser(user);

        var result = await _service.UpdateProfileAsync(_userId, "  Trần Việt Hải  ");

        user.FullName.Should().Be("Trần Việt Hải");
        result.FullName.Should().Be("Trần Việt Hải");
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateProfile_MapsDtoFields()
    {
        var user = MakeUser(avatarUrl: "https://cdn.example.com/avatars/a.png", provider: AuthProvider.Google);
        user.Role = UserRole.Admin;
        SetupUser(user);

        var result = await _service.UpdateProfileAsync(_userId, "Admin");

        result.Id.Should().Be(_userId);
        result.Email.Should().Be("user@example.com");
        result.Role.Should().Be("Admin");
        result.AuthProvider.Should().Be("Google");
        result.AvatarUrl.Should().Be("https://cdn.example.com/avatars/a.png");
    }

    [Fact]
    public async Task UpdateProfile_UserNotFound_Throws404()
    {
        SetupUser(null);

        var act = () => _service.UpdateProfileAsync(_userId, "X");

        await act.Should().ThrowAsync<NotFoundException>();
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ───────────────────────── ChangePasswordAsync ─────────────────────────

    [Fact]
    public async Task ChangePassword_CorrectCurrent_HashesNewPasswordAndSaves()
    {
        var oldHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!");
        var user = MakeUser(passwordHash: oldHash);
        SetupUser(user);

        await _service.ChangePasswordAsync(_userId, "OldPass123!", "NewPass456!");

        user.PasswordHash.Should().NotBe(oldHash);
        // Lưu hash BCrypt, KHÔNG lưu plaintext.
        user.PasswordHash.Should().NotBe("NewPass456!");
        BCrypt.Net.BCrypt.Verify("NewPass456!", user.PasswordHash!).Should().BeTrue();
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Throws422AndKeepsOldHash()
    {
        var oldHash = BCrypt.Net.BCrypt.HashPassword("OldPass123!");
        var user = MakeUser(passwordHash: oldHash);
        SetupUser(user);

        var act = () => _service.ChangePasswordAsync(_userId, "SaiMatKhau!", "NewPass456!");

        await act.Should().ThrowAsync<BusinessRuleException>();
        user.PasswordHash.Should().Be(oldHash);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePassword_GoogleAccountWithoutHash_Throws422()
    {
        // Tài khoản đăng nhập qua Google không có PasswordHash → không có gì để đổi.
        var user = MakeUser(passwordHash: null, provider: AuthProvider.Google);
        SetupUser(user);

        var act = () => _service.ChangePasswordAsync(_userId, "any", "NewPass456!");

        await act.Should().ThrowAsync<BusinessRuleException>();
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangePassword_EmptyHash_Throws422()
    {
        var user = MakeUser(passwordHash: string.Empty);
        SetupUser(user);

        var act = () => _service.ChangePasswordAsync(_userId, "any", "NewPass456!");

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task ChangePassword_UserNotFound_Throws404()
    {
        SetupUser(null);

        var act = () => _service.ChangePasswordAsync(_userId, "old", "new");

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
