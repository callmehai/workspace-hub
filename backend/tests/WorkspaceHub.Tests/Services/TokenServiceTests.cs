using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Security;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Infrastructure.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class TokenServiceTests
{
    private readonly Mock<ITokenProtector> _protectorMock;
    private readonly Mock<IIntegrationRepository> _integrationsMock;
    private readonly Mock<IConnectionRepository> _connectionsMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly TokenService _service;

    public TokenServiceTests()
    {
        _protectorMock = new Mock<ITokenProtector>();
        _integrationsMock = new Mock<IIntegrationRepository>();
        _connectionsMock = new Mock<IConnectionRepository>();
        _configMock = new Mock<IConfiguration>();

        _service = new TokenService(
            _protectorMock.Object,
            _integrationsMock.Object,
            _connectionsMock.Object,
            _configMock.Object);
    }

    [Fact]
    public async Task GetFreshAccessTokenAsync_WhenTokenStillValid_ReturnsUnprotectedTokenWithoutNetwork()
    {
        var connection = new Connection
        {
            AccessTokenEncrypted = "encrypted-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _protectorMock.Setup(m => m.Unprotect("encrypted-token")).Returns("plain-token");

        var token = await _service.GetFreshAccessTokenAsync(connection);

        token.Should().Be("plain-token");
        _connectionsMock.Verify(m => m.Update(It.IsAny<Connection>()), Times.Never);
        _connectionsMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
