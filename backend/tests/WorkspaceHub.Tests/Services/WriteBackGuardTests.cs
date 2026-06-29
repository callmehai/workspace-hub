using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Tests cho <see cref="WriteBackGuard"/> (SCRUM-38): conflict detection theo ETag.
/// Guard chỉ so sánh — lệch thì ném <see cref="ConflictException"/> (middleware map 409),
/// null thì skip-check (không coi là conflict).
/// </summary>
public class WriteBackGuardTests
{
    private readonly WriteBackGuard _guard = new(NullLogger<WriteBackGuard>.Instance);

    [Fact]
    public void EnsureNoConflict_WhenEtagsMatch_DoesNotThrow()
    {
        var act = () => _guard.EnsureNoConflict("etag-v1", "etag-v1");

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNoConflict_WhenEtagsDiffer_ThrowsConflictException()
    {
        var act = () => _guard.EnsureNoConflict("etag-v1", "etag-v2");

        act.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData(null, "etag-v2")]   // item chưa có version đối chiếu
    [InlineData("", "etag-v2")]
    [InlineData("etag-v1", null)]   // provider không cấp ETag
    [InlineData("etag-v1", "")]
    [InlineData(null, null)]
    [InlineData("", "")]            // cả hai đều empty → skip-check
    public void EnsureNoConflict_WhenEitherEtagIsNullOrEmpty_SkipsCheck(string? stored, string? provider)
    {
        var act = () => _guard.EnsureNoConflict(stored, provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNoConflict_IsCaseSensitive_TreatsCasingDifferenceAsConflict()
    {
        // ETag từ provider là opaque token — khác hoa/thường là khác version.
        var act = () => _guard.EnsureNoConflict("ETag-V1", "etag-v1");

        act.Should().Throw<ConflictException>();
    }
}
