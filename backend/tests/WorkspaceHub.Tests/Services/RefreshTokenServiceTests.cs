using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using WorkspaceHub.Infrastructure.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// SCRUM-63 — RefreshTokenService: issue / rotate / revoke / reuse-detection.
/// Dùng MemoryDistributedCache thật (thay Redis) + mock IUserRepository.
/// </summary>
public class RefreshTokenServiceTests
{
    private static IDistributedCache CreateCache()
        => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static IConfiguration CreateConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "workspace-hub-super-secret-key-change-in-production-32chars",
            ["Jwt:Issuer"] = "WorkspaceHub",
            ["Jwt:Audience"] = "WorkspaceHub",
            ["Jwt:ExpiresIn"] = "900",
            ["Jwt:RefreshExpiresIn"] = "604800"
        })
        .Build();

    private static User ActiveUser(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Email = "user@test.com",
        FullName = "Test User",
        Role = UserRole.User,
        IsActive = true
    };

    private static (RefreshTokenService Service, Mock<IUserRepository> Users, IDistributedCache Cache)
        Create(User? user = null)
    {
        user ??= ActiveUser();
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var cache = CreateCache();
        var service = new RefreshTokenService(CreateConfig(), cache, users.Object, NullLogger<RefreshTokenService>.Instance);
        return (service, users, cache);
    }

    [Fact]
    public async Task IssueAsync_returns_token_and_expiry()
    {
        var user = ActiveUser();
        var (service, _, _) = Create(user);

        var issued = await service.IssueAsync(user.Id);

        issued.RefreshToken.Should().NotBeNullOrEmpty();
        issued.ExpiresInSeconds.Should().Be(604800);
    }

    [Fact]
    public async Task ValidateAndRotate_valid_token_returns_new_tokens()
    {
        var user = ActiveUser();
        var (service, _, _) = Create(user);
        var issued = await service.IssueAsync(user.Id);

        var rotated = await service.ValidateAndRotateAsync(issued.RefreshToken);

        rotated.AccessToken.Should().NotBeNullOrEmpty();
        rotated.RefreshToken.Should().NotBeNullOrEmpty();
        rotated.RefreshToken.Should().NotBe(issued.RefreshToken); // đã xoay
        rotated.User.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task Old_token_rejected_after_rotation()
    {
        var user = ActiveUser();
        var (service, _, _) = Create(user);
        var issued = await service.IssueAsync(user.Id);
        await service.ValidateAndRotateAsync(issued.RefreshToken); // jti cũ bị xoá

        var act = () => service.ValidateAndRotateAsync(issued.RefreshToken);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Reuse_old_token_revokes_whole_family()
    {
        var user = ActiveUser();
        var (service, _, _) = Create(user);
        var issued = await service.IssueAsync(user.Id);
        var rotated = await service.ValidateAndRotateAsync(issued.RefreshToken);

        // Attacker dùng lại token cũ (đã xoay) → reuse → revoke family.
        var reuse = () => service.ValidateAndRotateAsync(issued.RefreshToken);
        await reuse.Should().ThrowAsync<UnauthorizedException>();

        // Token mới (cùng family) giờ cũng vô hiệu.
        var legitNext = () => service.ValidateAndRotateAsync(rotated.RefreshToken);
        await legitNext.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Revoked_token_cannot_be_used()
    {
        var user = ActiveUser();
        var (service, _, _) = Create(user);
        var issued = await service.IssueAsync(user.Id);

        await service.RevokeAsync(issued.RefreshToken); // logout

        var act = () => service.ValidateAndRotateAsync(issued.RefreshToken);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Inactive_user_rejected_on_rotate()
    {
        var user = ActiveUser();
        var users = new Mock<IUserRepository>();
        // Issue khi còn active...
        var activeSnapshot = ActiveUser(user.Id);
        users.SetupSequence(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = user.Id, Email = "u@t.com", FullName = "U", Role = UserRole.User, IsActive = false });
        var cache = CreateCache();
        var service = new RefreshTokenService(CreateConfig(), cache, users.Object, NullLogger<RefreshTokenService>.Instance);

        var issued = await service.IssueAsync(user.Id);

        var act = () => service.ValidateAndRotateAsync(issued.RefreshToken);
        await act.Should().ThrowAsync<UnauthorizedException>();
        _ = activeSnapshot;
    }

    [Fact]
    public async Task Garbage_token_rejected()
    {
        var (service, _, _) = Create();

        var act = () => service.ValidateAndRotateAsync("not-a-jwt");
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task RevokeAsync_ignores_null_or_garbage()
    {
        var (service, _, _) = Create();

        // Không throw.
        await service.RevokeAsync(null);
        await service.RevokeAsync("garbage");
    }
}
