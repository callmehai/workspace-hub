using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WorkspaceHub.Api.Middleware;
using Xunit;

namespace WorkspaceHub.Tests.Middleware;

/// <summary>
/// Tests cho <see cref="RequestLoggingMiddleware"/> (SCRUM-25): mỗi request log đúng 1 dòng
/// completion, mức log theo status code (≥400 → Warning, còn lại → Information), kèm
/// method/path/status và userId (anonymous khi chưa auth).
/// </summary>
public class RequestLoggingMiddlewareTests
{
    /// <summary>Chạy middleware với status code + claims cho trước, trả về logger đã ghi lại các entry.</summary>
    private static async Task<CapturingLogger<RequestLoggingMiddleware>> RunAsync(
        int statusCode, string method = "GET", string path = "/api/items", ClaimsPrincipal? user = null)
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(
            next: ctx => { ctx.Response.StatusCode = statusCode; return Task.CompletedTask; },
            logger: logger);

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (user is not null) context.User = user;

        await middleware.InvokeAsync(context);
        return logger;
    }

    [Fact]
    public async Task SuccessResponse_LogsAtInformation_WithRequestDetails()
    {
        var logger = await RunAsync(200, "GET", "/api/items");

        logger.Entries.Should().ContainSingle();
        var (level, message) = logger.Entries[0];
        level.Should().Be(LogLevel.Information);
        message.Should().Contain("GET");
        message.Should().Contain("/api/items");
        message.Should().Contain("200");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task ErrorResponse_LogsAtWarning(int statusCode)
    {
        var logger = await RunAsync(statusCode);

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task AuthenticatedRequest_IncludesUserIdFromNameIdentifier()
    {
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "TestAuth"));

        var logger = await RunAsync(200, user: principal);

        logger.Entries[0].Message.Should().Contain(userId.ToString());
    }

    [Fact]
    public async Task AuthenticatedRequest_FallsBackToSubClaim_WhenNoNameIdentifier()
    {
        // Token chỉ có "sub" (vd Google Sign-In) mà không có NameIdentifier → phải dùng nhánh fallback.
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", userId.ToString()) }, "TestAuth"));

        var logger = await RunAsync(200, user: principal);

        logger.Entries[0].Message.Should().Contain(userId.ToString());
    }

    [Fact]
    public async Task AnonymousRequest_LogsAnonymousUser()
    {
        var logger = await RunAsync(200);

        logger.Entries[0].Message.Should().Contain("anonymous");
    }

    /// <summary>ILogger giả lập, ghi lại (level, message đã format) để assert.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
