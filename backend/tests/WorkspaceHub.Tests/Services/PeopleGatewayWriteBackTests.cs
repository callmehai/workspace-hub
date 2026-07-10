using System.Net;
using FluentAssertions;
using Google;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Infrastructure.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class PeopleGatewayWriteBackTests
{
    [Fact]
    public void Handle_PreconditionFailed_MapsToConflictException()
    {
        var ex = new GoogleApiException("precondition failed")
        {
            HttpStatusCode = HttpStatusCode.PreconditionFailed
        };

        var mapped = GoogleApiExceptionHandler.Handle(ex, "People", "Contact", "people/c1");

        mapped.Should().BeOfType<ConflictException>();
    }

    [Fact]
    public void Handle_Forbidden_MapsToForbiddenException()
    {
        var ex = new GoogleApiException("forbidden")
        {
            HttpStatusCode = HttpStatusCode.Forbidden
        };

        var mapped = GoogleApiExceptionHandler.Handle(ex, "People", "Contact", "people/c1", "Reconnect Gmail to allow editing contacts.");

        mapped.Should().BeOfType<ForbiddenException>();
    }
}
