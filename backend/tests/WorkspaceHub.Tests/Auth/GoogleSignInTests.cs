using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs.Auth;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Services;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Auth;

public class GoogleSignInTests
{
    // ── Shared helpers ──────────────────────────────────────────────

    private const string TestGoogleSub = "google-sub-12345";
    private const string TestEmail = "testuser@gmail.com";
    private const string TestName = "Test Google User";
    private const string TestState = "valid-state";
    private const string TestCode = "auth-code-xyz";

    /// <summary>Creates a real MemoryDistributedCache for CSRF state testing.</summary>
    private static IDistributedCache CreateCache()
    {
        var opts = Options.Create(new MemoryDistributedCacheOptions());
        return new MemoryDistributedCache(opts);
    }

    /// <summary>Builds in-memory IConfiguration with JWT + Google settings.</summary>
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
        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    /// <summary>Dummy validators that always pass (register/login not tested here).</summary>
    private static (IValidator<RegisterRequest>, IValidator<LoginRequest>) CreateDummyValidators()
    {
        var regValidator = new Mock<IValidator<RegisterRequest>>();
        var loginValidator = new Mock<IValidator<LoginRequest>>();
        return (regValidator.Object, loginValidator.Object);
    }

    /// <summary>
    /// Builds an AuthService with mocked dependencies.
    /// Returns (service, userRepoMock, tokenClientMock, verifierMock, cache)
    /// so tests can arrange per-scenario behavior.
    /// </summary>
    private static (AuthService Service, Mock<IUserRepository> Users, Mock<IOAuthTokenClient> TokenClient, Mock<IGoogleTokenVerifier> Verifier, IDistributedCache Cache)
        CreateService()
    {
        var users = new Mock<IUserRepository>();
        var tokenClient = new Mock<IOAuthTokenClient>();
        var verifier = new Mock<IGoogleTokenVerifier>();
        var cache = CreateCache();
        var config = CreateConfig();
        var (regVal, loginVal) = CreateDummyValidators();

        var jwtFactory = new WorkspaceHub.Infrastructure.Services.JwtTokenFactory(config);
        var service = new AuthService(
            users.Object,
            config,
            regVal,
            loginVal,
            cache,
            tokenClient.Object,
            verifier.Object,
            jwtFactory);

        return (service, users, tokenClient, verifier, cache);
    }

    /// <summary>Seeds the CSRF state into the cache (simulates GoogleStartAsync having been called).</summary>
    private static async Task SeedStateAsync(IDistributedCache cache, string state)
    {
        await cache.SetStringAsync(
            $"oauth:signin:state:{state}",
            "signin",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
    }

    /// <summary>Arranges the mock token client to return a JSON payload with the given id_token.</summary>
    private static void ArrangeTokenExchange(Mock<IOAuthTokenClient> tokenClient, string idToken = "fake-id-token")
    {
        var json = $"{{\"id_token\":\"{idToken}\",\"access_token\":\"at\",\"expires_in\":3600}}";
        tokenClient
            .Setup(tc => tc.PostFormAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
    }

    // ── Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task GoogleStartAsync_ReturnsAuthorizationUrlAndState()
    {
        // Arrange
        var (service, _, _, _, _) = CreateService();

        // Act
        var result = await service.GoogleStartAsync();

        // Assert
        result.AuthorizationUrl.Should().Contain("accounts.google.com");
        result.AuthorizationUrl.Should().Contain("openid");
        result.AuthorizationUrl.Should().Contain("email");
        result.AuthorizationUrl.Should().Contain("profile");
        result.State.Should().NotBeNullOrEmpty();
        result.State.Should().HaveLength(32); // Guid without hyphens
    }

    [Fact]
    public async Task GoogleCallbackAsync_NewUser_CreatesUserWithGoogleProvider()
    {
        // Arrange
        var (service, users, tokenClient, verifier, cache) = CreateService();
        await SeedStateAsync(cache, TestState);

        ArrangeTokenExchange(tokenClient);
        verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestGoogleSub, TestEmail, TestName));

        users.Setup(u => u.GetByGoogleSubAsync(TestGoogleSub, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        User? createdUser = null;
        users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => createdUser = u)
            .Returns(Task.CompletedTask);
        users.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        var result = await service.GoogleCallbackAsync(TestCode, TestState);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.ExpiresIn.Should().Be(3600);
        result.User.Email.Should().Be(TestEmail);

        createdUser.Should().NotBeNull();
        createdUser!.AuthProvider.Should().Be(AuthProvider.Google);
        createdUser.PasswordHash.Should().BeNull();
        createdUser.GoogleSub.Should().Be(TestGoogleSub);
        createdUser.Email.Should().Be(TestEmail);
        createdUser.FullName.Should().Be(TestName); // FullName phải là Google display name, không phải email prefix
    }

    [Fact]
    public async Task GoogleCallbackAsync_ExistingEmailUser_LinksGoogleSub()
    {
        // Arrange
        var (service, users, tokenClient, verifier, cache) = CreateService();
        await SeedStateAsync(cache, TestState);

        ArrangeTokenExchange(tokenClient);
        verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestGoogleSub, TestEmail, TestName));

        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = TestEmail,
            FullName = "Test User",
            PasswordHash = "hashed-pw",
            AuthProvider = AuthProvider.Local,
            IsActive = true,
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow
        };

        users.Setup(u => u.GetByGoogleSubAsync(TestGoogleSub, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);
        users.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        var result = await service.GoogleCallbackAsync(TestCode, TestState);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();

        existingUser.GoogleSub.Should().Be(TestGoogleSub);
        existingUser.AuthProvider.Should().Be(AuthProvider.Both);
    }

    [Fact]
    public async Task GoogleCallbackAsync_ExistingGoogleSubUser_ReturnsJwt()
    {
        // Arrange
        var (service, users, tokenClient, verifier, cache) = CreateService();
        await SeedStateAsync(cache, TestState);

        ArrangeTokenExchange(tokenClient);
        verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestGoogleSub, TestEmail, TestName));

        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            Email = TestEmail,
            FullName = "Google User",
            PasswordHash = null,
            AuthProvider = AuthProvider.Google,
            GoogleSub = TestGoogleSub,
            IsActive = true,
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow
        };

        users.Setup(u => u.GetByGoogleSubAsync(TestGoogleSub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);
        users.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        var result = await service.GoogleCallbackAsync(TestCode, TestState);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.User.Email.Should().Be(TestEmail);

        // Verify no new user was created
        users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GoogleCallbackAsync_LockedUser_FoundByGoogleSub_Throws401()
    {
        // Arrange
        var (service, users, tokenClient, verifier, cache) = CreateService();
        await SeedStateAsync(cache, TestState);

        ArrangeTokenExchange(tokenClient);
        verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestGoogleSub, TestEmail, TestName));

        var lockedUser = new User
        {
            Id = Guid.NewGuid(),
            Email = TestEmail,
            FullName = "Locked User",
            AuthProvider = AuthProvider.Google,
            GoogleSub = TestGoogleSub,
            IsActive = false,
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow
        };

        users.Setup(u => u.GetByGoogleSubAsync(TestGoogleSub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockedUser);

        // Act & Assert
        var act = () => service.GoogleCallbackAsync(TestCode, TestState);
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Account is locked");
    }

    [Fact]
    public async Task GoogleCallbackAsync_LockedUser_FoundByEmail_Throws401()
    {
        // Arrange
        var (service, users, tokenClient, verifier, cache) = CreateService();
        await SeedStateAsync(cache, TestState);

        ArrangeTokenExchange(tokenClient);
        verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestGoogleSub, TestEmail, TestName));

        var lockedUser = new User
        {
            Id = Guid.NewGuid(),
            Email = TestEmail,
            FullName = "Locked User",
            PasswordHash = "hashed-pw",
            AuthProvider = AuthProvider.Local,
            IsActive = false,
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow
        };

        users.Setup(u => u.GetByGoogleSubAsync(TestGoogleSub, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        users.Setup(u => u.GetByEmailAsync(TestEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockedUser);

        // Act & Assert
        var act = () => service.GoogleCallbackAsync(TestCode, TestState);
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Account is locked");
    }

    [Fact]
    public async Task GoogleCallbackAsync_InvalidState_ThrowsCsrfException()
    {
        // Arrange
        var (service, _, _, _, _) = CreateService();
        // State NOT seeded in cache — simulates expired or invalid state

        // Act & Assert
        var act = () => service.GoogleCallbackAsync(TestCode, "bogus-state");
        await act.Should().ThrowAsync<CsrfException>();
    }

    [Fact]
    public async Task GoogleCallbackAsync_InvalidIdToken_ThrowsBusinessRuleException()
    {
        // Arrange
        var (service, users, tokenClient, verifier, cache) = CreateService();
        await SeedStateAsync(cache, TestState);

        ArrangeTokenExchange(tokenClient);
        verifier.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessRuleException("Invalid Google id_token: token expired"));

        // Act & Assert
        var act = () => service.GoogleCallbackAsync(TestCode, TestState);
        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Invalid Google id_token*");
    }
}
