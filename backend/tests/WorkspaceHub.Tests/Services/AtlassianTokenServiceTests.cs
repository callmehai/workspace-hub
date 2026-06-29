using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class AtlassianTokenServiceTests
{
    private readonly Mock<ITokenProtector> _protector = new();
    private readonly Mock<IIntegrationRepository> _integrations = new();
    private readonly Mock<IConnectionRepository> _connections = new();
    private readonly Mock<IOAuthTokenClient> _tokenClient = new();
    private readonly Mock<IConfiguration> _config = new();
    private readonly AtlassianTokenService _service;

    public AtlassianTokenServiceTests()
    {
        _service = new AtlassianTokenService(
            _protector.Object, _integrations.Object, _connections.Object, _tokenClient.Object, _config.Object);
    }

    private Connection Conn(DateTime expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        IntegrationId = Guid.NewGuid(),
        AccessTokenEncrypted = "enc-access",
        RefreshTokenEncrypted = "enc-refresh",
        ExpiresAt = expiresAt
    };

    private void SetupCredentials(Guid integrationId)
    {
        _integrations.Setup(m => m.GetByIdAsync(integrationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Integration { Id = integrationId, Key = "atlassian", TokenEndpoint = "https://auth.atlassian.com/oauth/token", Provider = "Atlassian", DisplayName = "", IconUrl = "", Description = "", AuthorizationEndpoint = "", SupportedServices = "[\"Jira\"]" });
        _config.Setup(c => c["OAuth:atlassian:ClientId"]).Returns("cid");
        _config.Setup(c => c["OAuth:atlassian:ClientSecret"]).Returns("secret");
    }

    [Fact]
    public async Task ValidToken_FastPath_NoNetworkNoRefresh()
    {
        var conn = Conn(DateTime.UtcNow.AddHours(1));
        _protector.Setup(m => m.Unprotect("enc-access")).Returns("plain-access");

        var token = await _service.GetFreshAccessTokenAsync(conn);

        token.Should().Be("plain-access");
        _tokenClient.Verify(m => m.PostFormAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _connections.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExpiredToken_Refreshes_StoresRotatedRefreshToken()
    {
        var conn = Conn(DateTime.UtcNow.AddMinutes(1)); // < 5 min buffer → refresh
        SetupCredentials(conn.IntegrationId);

        // Double-check đọc lại tracked connection → trả về chính conn (vẫn expired) để đi vào refresh.
        _connections.Setup(m => m.GetByIdTrackedAsync(conn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conn);
        _protector.Setup(m => m.Unprotect("enc-refresh")).Returns("plain-refresh");
        _protector.Setup(m => m.Protect(It.IsAny<string>())).Returns((string s) => "enc-" + s);

        _tokenClient.Setup(m => m.PostFormAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"access_token\":\"new-access\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");

        var token = await _service.GetFreshAccessTokenAsync(conn);

        token.Should().Be("new-access");
        conn.AccessTokenEncrypted.Should().Be("enc-new-access");
        conn.RefreshTokenEncrypted.Should().Be("enc-new-refresh"); // rotating token được lưu
        _connections.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshFails_ThrowsBusinessRule_Not500()
    {
        var conn = Conn(DateTime.UtcNow.AddMinutes(1));
        SetupCredentials(conn.IntegrationId);
        _connections.Setup(m => m.GetByIdTrackedAsync(conn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(conn);
        _protector.Setup(m => m.Unprotect("enc-refresh")).Returns("plain-refresh");

        _tokenClient.Setup(m => m.PostFormAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("400 invalid_grant"));

        var act = () => _service.GetFreshAccessTokenAsync(conn);

        // BusinessRuleException → 422 (re-auth), KHÔNG phải InvalidOperationException → 500.
        await act.Should().ThrowAsync<BusinessRuleException>();
        conn.Status.Should().Be(WorkspaceHub.Domain.Enums.ConnectionStatus.Error);
    }

    [Fact]
    public async Task ConcurrentRefresh_SecondSeesFirstResult_NoDoubleRefresh()
    {
        // Sau khi acquire lock, double-check đọc tracked connection đã được refresh (token còn hạn) → không refresh lần 2.
        var conn = Conn(DateTime.UtcNow.AddMinutes(1));
        var refreshed = Conn(DateTime.UtcNow.AddHours(1));
        refreshed.Id = conn.Id;
        refreshed.AccessTokenEncrypted = "enc-fresh";

        _connections.Setup(m => m.GetByIdTrackedAsync(conn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(refreshed);
        _protector.Setup(m => m.Unprotect("enc-fresh")).Returns("fresh-access");

        var token = await _service.GetFreshAccessTokenAsync(conn);

        token.Should().Be("fresh-access");
        _tokenClient.Verify(m => m.PostFormAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
