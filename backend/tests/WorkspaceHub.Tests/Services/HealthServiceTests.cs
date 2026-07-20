using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// HealthService chỉ có 2 nhánh: repo trả về count (DB reachable) hoặc repo ném exception
/// (DB chưa migrate / không reachable) → vẫn trả DTO chứ không throw ra ngoài.
/// </summary>
public class HealthServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly HealthService _service;

    public HealthServiceTests()
    {
        _service = new HealthService(_users.Object);
    }

    [Fact]
    public async Task CheckAsync_DbReachable_ReturnsHealthyWithUserCount()
    {
        _users.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var result = await _service.CheckAsync();

        result.Status.Should().Be("Healthy");
        result.Database.Should().Be("Connected");
        result.UserCount.Should().Be(7);
    }

    [Fact]
    public async Task CheckAsync_NoUsers_ReturnsHealthyWithZeroCount()
    {
        // 0 user vẫn là Healthy — count = 0 không phải tín hiệu lỗi.
        _users.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var result = await _service.CheckAsync();

        result.Status.Should().Be("Healthy");
        result.Database.Should().Be("Connected");
        result.UserCount.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_RepositoryThrows_ReturnsDegradedUnreachable()
    {
        _users.Setup(r => r.CountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cannot open database"));

        var result = await _service.CheckAsync();

        // Không được throw ra ngoài: controller vẫn trả 200 nhưng báo Degraded.
        result.Status.Should().Be("Degraded");
        result.Database.Should().Be("Unreachable");
        result.UserCount.Should().Be(0);
    }

    [Fact]
    public async Task CheckAsync_ReturnsServerTimeUtc()
    {
        _users.Setup(r => r.CountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var before = DateTime.UtcNow.AddSeconds(-5);

        var result = await _service.CheckAsync();

        result.ServerTimeUtc.Should().BeOnOrAfter(before);
        result.ServerTimeUtc.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task CheckAsync_PassesCancellationTokenToRepository()
    {
        using var cts = new CancellationTokenSource();
        _users.Setup(r => r.CountAsync(cts.Token)).ReturnsAsync(3);

        var result = await _service.CheckAsync(cts.Token);

        result.UserCount.Should().Be(3);
        _users.Verify(r => r.CountAsync(cts.Token), Times.Once);
    }
}
