using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using WorkspaceHub.Api.Middleware;
using WorkspaceHub.Application.Common;
using Xunit;

namespace WorkspaceHub.Tests.Middleware;

/// <summary>
/// Tests cho <see cref="ExceptionMiddleware"/> (SCRUM-24):
/// đảm bảo error format chuẩn { error, message, details[], traceId }, map đúng status code,
/// và không lộ stack trace/message nội bộ của lỗi 500 ở môi trường production.
/// </summary>
public class ExceptionMiddlewareTests
{
    private sealed record ErrorBody(string Error, string Message, string[] Details, string TraceId);

    /// <summary>Chạy middleware với một exception cho trước, trả về (status, body đã parse).</summary>
    private static async Task<(int status, ErrorBody body)> RunAsync(Exception toThrow, bool isDevelopment = false)
    {
        var env = new FakeEnvironment(isDevelopment ? Environments.Development : Environments.Production);
        var middleware = new ExceptionMiddleware(
            next: _ => throw toThrow,
            logger: NullLogger<ExceptionMiddleware>.Instance,
            env: env);

        var context = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        await middleware.InvokeAsync(context);

        responseStream.Position = 0;
        var json = await new StreamReader(responseStream).ReadToEndAsync();
        var body = JsonSerializer.Deserialize<ErrorBody>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        })!;

        return (context.Response.StatusCode, body);
    }

    [Fact]
    public async Task ValidationException_Returns400_WithFieldDetails()
    {
        var failures = new[]
        {
            new ValidationFailure("Email", "Email is required"),
            new ValidationFailure("Password", "Password must be at least 8 characters")
        };

        var (status, body) = await RunAsync(new ValidationException(failures));

        status.Should().Be((int)HttpStatusCode.BadRequest);
        body.Error.Should().Be("ValidationError");
        body.Details.Should().HaveCount(2);
        body.Details.Should().Contain("Email: Email is required");
        body.TraceId.Should().Be("trace-123");
    }

    [Theory]
    [InlineData(typeof(UnauthorizedException), HttpStatusCode.Unauthorized, "UnauthorizedError")]
    [InlineData(typeof(ForbiddenException), HttpStatusCode.Forbidden, "ForbiddenError")]
    [InlineData(typeof(NotFoundException), HttpStatusCode.NotFound, "NotFoundError")]
    [InlineData(typeof(ConflictException), HttpStatusCode.Conflict, "ConflictError")]
    [InlineData(typeof(BusinessRuleException), HttpStatusCode.UnprocessableEntity, "BusinessRuleError")]
    [InlineData(typeof(CsrfException), HttpStatusCode.BadRequest, "CsrfError")]
    public async Task DomainException_MapsToExpectedStatusAndErrorType(Type exceptionType, HttpStatusCode expectedStatus, string expectedError)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var (status, body) = await RunAsync(ex);

        status.Should().Be((int)expectedStatus);
        body.Error.Should().Be(expectedError);
        body.Message.Should().Be("boom"); // domain message an toàn để trả ra client
        body.Details.Should().NotBeNull();
        body.TraceId.Should().Be("trace-123");
    }

    [Fact]
    public async Task UnknownException_Production_Returns500_WithoutLeakingDetails()
    {
        var (status, body) = await RunAsync(new InvalidOperationException("secret internal detail"), isDevelopment: false);

        status.Should().Be((int)HttpStatusCode.InternalServerError);
        body.Error.Should().Be("InternalError");
        body.Message.Should().Be("An unexpected error occurred.");
        body.Message.Should().NotContain("secret internal detail");
        body.Details.Should().BeEmpty();
        body.TraceId.Should().Be("trace-123");
    }

    [Fact]
    public async Task UnknownException_Development_IncludesDiagnosticsInDetails()
    {
        var (status, body) = await RunAsync(new InvalidOperationException("secret internal detail"), isDevelopment: true);

        status.Should().Be((int)HttpStatusCode.InternalServerError);
        body.Message.Should().Be("An unexpected error occurred."); // message vẫn generic
        body.Details.Should().Contain("secret internal detail");
        body.Details.Should().Contain(nameof(InvalidOperationException));
    }

    /// <summary>IHostEnvironment giả lập để test nhánh dev/prod mà không cần host thật.</summary>
    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "WorkspaceHub.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
