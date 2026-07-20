using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Auth;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Application.Validators;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Auth;

/// <summary>
/// Unit test cho <see cref="AuthService"/> — phần local auth (SCRUM-29):
/// RegisterAsync / LoginAsync / SendOtpAsync / VerifyOtpAsync / GetMeAsync.
/// Luồng Google Sign-In (GoogleStartAsync / GoogleCallbackAsync) đã có ở <c>GoogleSignInTests</c>.
/// </summary>
public class AuthServiceTests
{
    // ── Hằng dùng chung ──────────────────────────────────────────────

    private const string TestEmail = "testuser@gmail.com";
    private const string TestPassword = "Password123!";
    private const string TestFullName = "Test User";

    /// <summary>Cooldown giả AuthService trả khi không gửi OTP thật (chống user enumeration).</summary>
    private const int DefaultCooldownSeconds = 60;

    // ── Helpers dựng service ─────────────────────────────────────────

    /// <summary>MemoryDistributedCache thật — Google flow cần, local auth không dùng tới.</summary>
    private static IDistributedCache CreateCache()
        => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    /// <summary>IConfiguration in-memory với JWT + Google settings (giống GoogleSignInTests).</summary>
    private static IConfiguration CreateConfig()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "workspace-hub-super-secret-key-change-in-production-32chars",
            ["Jwt:Issuer"] = "WorkspaceHub",
            ["Jwt:Audience"] = "WorkspaceHub",
            ["Jwt:ExpiresIn"] = "3600",
            ["OAuth:google:ClientId"] = "test-client-id.apps.googleusercontent.com",
            ["OAuth:google:ClientSecret"] = "test-client-secret",
            ["Google:RedirectUri"] = "http://localhost:5173/auth/google/callback"
        };
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    /// <summary>Validator mock luôn PASS (trả ValidationResult rỗng) — test nào cần fail thì truyền validator thật.</summary>
    private static IValidator<T> PassingValidator<T>()
    {
        var mock = new Mock<IValidator<T>>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<ValidationContext<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        return mock.Object;
    }

    /// <summary>
    /// Dựng AuthService với 11 dependency (cùng cách dựng như GoogleSignInTests).
    /// Trả kèm các mock mà test local auth cần arrange/verify.
    /// </summary>
    private static (AuthService Service, Mock<IUserRepository> Users, Mock<IOtpService> Otp, Mock<IFriendService> Friends)
        CreateService(
            IValidator<RegisterRequest>? registerValidator = null,
            IValidator<LoginRequest>? loginValidator = null)
    {
        var users = new Mock<IUserRepository>();
        var otp = new Mock<IOtpService>();
        var friends = new Mock<IFriendService>();
        var tokenClient = new Mock<IOAuthTokenClient>();
        var verifier = new Mock<IGoogleTokenVerifier>();
        var config = CreateConfig();
        var jwtFactory = new WorkspaceHub.Infrastructure.Services.JwtTokenFactory(config);

        users.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var service = new AuthService(
            users.Object,
            config,
            registerValidator ?? PassingValidator<RegisterRequest>(),
            loginValidator ?? PassingValidator<LoginRequest>(),
            CreateCache(),
            tokenClient.Object,
            verifier.Object,
            jwtFactory,
            otp.Object,
            friends.Object,
            NullLogger<AuthService>.Instance);

        return (service, users, otp, friends);
    }

    /// <summary>User local đã verify email + đang hoạt động, password = <see cref="TestPassword"/>.</summary>
    private static User CreateLocalUser(
        bool emailVerified = true,
        bool isActive = true,
        string? passwordHash = null,
        string email = TestEmail)
        => new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = TestFullName,
            PasswordHash = passwordHash ?? BCrypt.Net.BCrypt.HashPassword(TestPassword),
            EmailVerified = emailVerified,
            IsActive = isActive,
            AuthProvider = AuthProvider.Local,
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow
        };

    // ── RegisterAsync ────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_NewEmail_CreatesUnverifiedUserWithHashedPasswordAndSendsOtp()
    {
        // Arrange
        var (service, users, otp, _) = CreateService();
        users.Setup(u => u.EmailExistsAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        User? created = null;
        users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => created = u)
            .Returns(Task.CompletedTask);

        otp.Setup(o => o.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(45);

        // Email có hoa + khoảng trắng → service phải normalize về lowercase/trim
        var request = new RegisterRequest("  TestUser@Gmail.com  ", TestPassword, $"  {TestFullName}  ");

        // Act
        var result = await service.RegisterAsync(request);

        // Assert — user được tạo đúng trạng thái
        created.Should().NotBeNull();
        created!.Email.Should().Be(TestEmail);
        created.FullName.Should().Be(TestFullName);
        created.EmailVerified.Should().BeFalse();     // SCRUM-64: phải verify OTP mới login được
        created.IsActive.Should().BeTrue();
        created.Role.Should().Be(UserRole.User);
        created.Id.Should().NotBe(Guid.Empty);

        // Password được hash (BCrypt), không lưu plaintext
        created.PasswordHash.Should().NotBeNullOrEmpty();
        created.PasswordHash.Should().NotBe(TestPassword);
        BCrypt.Net.BCrypt.Verify(TestPassword, created.PasswordHash).Should().BeTrue();

        // Đã lưu DB + gửi OTP tới đúng user/email
        users.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        otp.Verify(o => o.SendAsync(created.Id, TestEmail, It.IsAny<CancellationToken>()), Times.Once);

        // Assert — RegisterResult KHÔNG chứa token, chỉ email + cooldown
        result.Email.Should().Be(TestEmail);
        result.RequiresEmailVerification.Should().BeTrue();
        result.ResendCooldownSeconds.Should().Be(45);
    }

    [Fact]
    public async Task RegisterAsync_EmailAlreadyExists_ThrowsConflictException()
    {
        // Arrange
        var (service, users, otp, _) = CreateService();
        users.Setup(u => u.EmailExistsAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act & Assert
        var act = () => service.RegisterAsync(new RegisterRequest(TestEmail, TestPassword, TestFullName));
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Email already registered");

        // Không tạo user, không gửi OTP
        users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        otp.Verify(o => o.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_InvalidInput_ThrowsValidationException()
    {
        // Arrange — dùng validator THẬT để chắc chắn ValidateAndThrowAsync chặn trước khi chạm DB
        var (service, users, _, _) = CreateService(registerValidator: new RegisterRequestValidator());
        var request = new RegisterRequest("not-an-email", "123", ""); // sai email + pass < 8 + thiếu tên

        // Act & Assert
        var act = () => service.RegisterAsync(request);
        await act.Should().ThrowAsync<ValidationException>();

        users.Verify(u => u.EmailExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WithInviteToken_ConsumesFriendInvite()
    {
        // Arrange
        var (service, users, otp, friends) = CreateService();
        users.Setup(u => u.EmailExistsAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        User? created = null;
        users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => created = u)
            .Returns(Task.CompletedTask);
        otp.Setup(o => o.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultCooldownSeconds);

        const string inviteToken = "invite-token-abc";

        // Act
        await service.RegisterAsync(new RegisterRequest(TestEmail, TestPassword, TestFullName, inviteToken));

        // Assert — invite kết bạn được consume với đúng userId/email/token
        created.Should().NotBeNull();
        friends.Verify(f => f.ConsumeInvitesOnRegistrationAsync(
            created!.Id, TestEmail, inviteToken, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_OtpSendFails_StillCreatesUserAndReturnsDefaultCooldown()
    {
        // Arrange — provider mail lỗi KHÔNG được làm hỏng đăng ký (user vẫn tạo, FE dùng nút resend)
        var (service, users, otp, _) = CreateService();
        users.Setup(u => u.EmailExistsAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        otp.Setup(o => o.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Resend down"));

        // Act
        var result = await service.RegisterAsync(new RegisterRequest(TestEmail, TestPassword, TestFullName));

        // Assert
        result.RequiresEmailVerification.Should().BeTrue();
        result.ResendCooldownSeconds.Should().Be(DefaultCooldownSeconds);
        users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── LoginAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsAuthResponseWithToken()
    {
        // Arrange
        var (service, users, _, _) = CreateService();
        var user = CreateLocalUser();
        // Mock khớp email đã normalize → cũng chứng minh service trim + lowercase input
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        // Act
        var result = await service.LoginAsync(new LoginRequest("  TestUser@Gmail.com  ", TestPassword));

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.ExpiresIn.Should().Be(3600);
        result.User.Id.Should().Be(user.Id);
        result.User.Email.Should().Be(TestEmail);
        result.User.Role.Should().Be(nameof(UserRole.User));

        // Đăng nhập thành công phải cập nhật LastLoginAt và lưu lại
        user.LastLoginAt.Should().NotBeNull();
        users.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsUnauthorizedException()
    {
        // Arrange
        var (service, users, _, _) = CreateService();
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLocalUser());

        // Act & Assert
        var act = () => service.LoginAsync(new LoginRequest(TestEmail, "WrongPassword!"));
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid credentials");
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_ThrowsUnauthorizedException()
    {
        // Arrange
        var (service, users, _, _) = CreateService();
        users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert — message giống hệt case sai mật khẩu (không lộ email có tồn tại hay không)
        var act = () => service.LoginAsync(new LoginRequest("nobody@gmail.com", TestPassword));
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid credentials");
    }

    [Fact]
    public async Task LoginAsync_GoogleOnlyUserWithoutPassword_ThrowsUnauthorizedException()
    {
        // Arrange — user đăng nhập bằng Google, PasswordHash null → không cho login local
        var (service, users, _, _) = CreateService();
        var googleUser = CreateLocalUser();
        googleUser.PasswordHash = null;
        googleUser.AuthProvider = AuthProvider.Google;
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(googleUser);

        // Act & Assert
        var act = () => service.LoginAsync(new LoginRequest(TestEmail, TestPassword));
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid credentials");
    }

    [Fact]
    public async Task LoginAsync_EmailNotVerified_ThrowsForbiddenException()
    {
        // Arrange — SCRUM-64: mật khẩu đúng nhưng chưa verify OTP
        var (service, users, _, _) = CreateService();
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLocalUser(emailVerified: false));

        // Act & Assert — FE bắt mã EMAIL_NOT_VERIFIED để mở màn nhập OTP
        var act = () => service.LoginAsync(new LoginRequest(TestEmail, TestPassword));
        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("EMAIL_NOT_VERIFIED");
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ThrowsUnauthorizedAccountLocked()
    {
        // Arrange — account bị admin khoá
        var (service, users, _, _) = CreateService();
        var locked = CreateLocalUser(isActive: false);
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(locked);

        // Act & Assert
        var act = () => service.LoginAsync(new LoginRequest(TestEmail, TestPassword));
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Account is locked");

        // Không cập nhật LastLoginAt khi bị chặn
        locked.LastLoginAt.Should().BeNull();
        users.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── SendOtpAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task SendOtpAsync_EmailNotFound_ReturnsDefaultCooldownWithoutSending()
    {
        // Arrange — chống user enumeration: email lạ vẫn trả cooldown như bình thường
        var (service, users, otp, _) = CreateService();
        users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var cooldown = await service.SendOtpAsync("nobody@gmail.com");

        // Assert
        cooldown.Should().Be(DefaultCooldownSeconds);
        otp.Verify(o => o.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendOtpAsync_UnverifiedUser_CallsOtpServiceAndReturnsItsCooldown()
    {
        // Arrange
        var (service, users, otp, _) = CreateService();
        var user = CreateLocalUser(emailVerified: false);
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        otp.Setup(o => o.SendAsync(user.Id, TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(30);

        // Act — email viết hoa vẫn phải normalize trước khi tra DB
        var cooldown = await service.SendOtpAsync("  TestUser@Gmail.com  ");

        // Assert
        cooldown.Should().Be(30);
        otp.Verify(o => o.SendAsync(user.Id, TestEmail, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendOtpAsync_AlreadyVerifiedUser_ReturnsDefaultCooldownWithoutSending()
    {
        // Arrange — user đã verify thì không gửi OTP nữa, nhưng cũng không tiết lộ điều đó
        var (service, users, otp, _) = CreateService();
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLocalUser(emailVerified: true));

        // Act
        var cooldown = await service.SendOtpAsync(TestEmail);

        // Assert
        cooldown.Should().Be(DefaultCooldownSeconds);
        otp.Verify(o => o.SendAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── VerifyOtpAsync ───────────────────────────────────────────────

    [Fact]
    public async Task VerifyOtpAsync_ValidCode_SetsEmailVerifiedAndReturnsToken()
    {
        // Arrange
        var (service, users, otp, _) = CreateService();
        var user = CreateLocalUser(emailVerified: false);
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        otp.Setup(o => o.VerifyAsync(user.Id, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await service.VerifyOtpAsync("  TestUser@Gmail.com  ", "123456");

        // Assert — verify xong thì đăng nhập luôn
        user.EmailVerified.Should().BeTrue();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.ExpiresIn.Should().Be(3600);
        result.User.Id.Should().Be(user.Id);
        user.LastLoginAt.Should().NotBeNull();
        users.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeast(1));
    }

    [Fact]
    public async Task VerifyOtpAsync_WrongCode_ThrowsBusinessRuleException()
    {
        // Arrange
        var (service, users, otp, _) = CreateService();
        var user = CreateLocalUser(emailVerified: false);
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        otp.Setup(o => o.VerifyAsync(user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act & Assert — 422
        var act = () => service.VerifyOtpAsync(TestEmail, "000000");
        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("Mã OTP không đúng hoặc đã hết hạn.");

        user.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyOtpAsync_ExpiredCode_PropagatesBusinessRuleExceptionFromOtpService()
    {
        // Arrange — OtpService throw 422 khi mã hết hạn / không tồn tại
        var (service, users, otp, _) = CreateService();
        var user = CreateLocalUser(emailVerified: false);
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        otp.Setup(o => o.VerifyAsync(user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("Mã OTP đã hết hạn."));

        // Act & Assert
        var act = () => service.VerifyOtpAsync(TestEmail, "123456");
        await act.Should().ThrowAsync<BusinessRuleException>();

        user.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyOtpAsync_UserNotFound_ThrowsBusinessRuleException()
    {
        // Arrange — trả 422 giống hệt case mã sai để không lộ email có tồn tại hay không
        var (service, users, otp, _) = CreateService();
        users.Setup(u => u.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        var act = () => service.VerifyOtpAsync("nobody@gmail.com", "123456");
        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("Mã OTP không đúng hoặc đã hết hạn.");

        otp.Verify(o => o.VerifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifyOtpAsync_AlreadyVerifiedUser_ThrowsBusinessRuleException()
    {
        // Arrange
        var (service, users, otp, _) = CreateService();
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLocalUser(emailVerified: true));

        // Act & Assert
        var act = () => service.VerifyOtpAsync(TestEmail, "123456");
        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("Mã OTP không đúng hoặc đã hết hạn.");

        otp.Verify(o => o.VerifyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── GetMeAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetMeAsync_ExistingUser_ReturnsUserDto()
    {
        // Arrange
        var (service, users, _, _) = CreateService();
        var user = CreateLocalUser();
        user.AvatarUrl = "https://cdn.example.com/avatar.png";
        user.Role = UserRole.Admin;
        users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        // Act
        var dto = await service.GetMeAsync(user.Id);

        // Assert — DTO không chứa PasswordHash (record UserDto không có field đó)
        dto.Id.Should().Be(user.Id);
        dto.Email.Should().Be(TestEmail);
        dto.FullName.Should().Be(TestFullName);
        dto.Role.Should().Be(nameof(UserRole.Admin));
        dto.AvatarUrl.Should().Be("https://cdn.example.com/avatar.png");
        dto.AuthProvider.Should().Be(nameof(AuthProvider.Local));
    }

    [Fact]
    public async Task GetMeAsync_UserNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var (service, users, _, _) = CreateService();
        var missingId = Guid.NewGuid();
        users.Setup(u => u.GetByIdAsync(missingId, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        // Act & Assert
        var act = () => service.GetMeAsync(missingId);
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"User {missingId} not found");
    }
}
