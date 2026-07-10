using FluentAssertions;
using Google.Apis.PeopleService.v1.Data;
using WorkspaceHub.Infrastructure.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class PeopleContactNameHelperTests
{
    [Fact]
    public void ExtractDisplayName_PrefersDisplayName()
    {
        var name = new Name { DisplayName = "Alice", GivenName = "Bob" };
        PeopleContactNameHelper.ExtractDisplayName(name).Should().Be("Alice");
    }

    [Fact]
    public void ExtractDisplayName_FallsBackToGivenAndFamily()
    {
        var name = new Name { GivenName = "Vu", FamilyName = "KL" };
        PeopleContactNameHelper.ExtractDisplayName(name).Should().Be("Vu KL");
    }

    [Fact]
    public void BuildNameForUpdate_SingleFieldUi_SetsGivenNameWhenNoFamilyName()
    {
        var result = PeopleContactNameHelper.BuildNameForUpdate(
            new Name { GivenName = "vukl", FamilyName = null },
            "vukl12333");

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("vukl12333");
        result.GivenName.Should().Be("vukl12333");
    }

    [Fact]
    public void BuildNameForUpdate_PreservesFamilyNameWhenPresent()
    {
        var result = PeopleContactNameHelper.BuildNameForUpdate(
            new Name { GivenName = "Vu", FamilyName = "KL", DisplayName = "Vu KL" },
            "Vu Khanh Linh");

        result!.FamilyName.Should().Be("KL");
        result.DisplayName.Should().Be("Vu Khanh Linh");
        result.GivenName.Should().Be("Vu");
    }

    [Fact]
    public void BuildNameForUpdate_NullExisting_CreatesNewName()
    {
        var result = PeopleContactNameHelper.BuildNameForUpdate(null, "vukl12333");

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("vukl12333");
        result.GivenName.Should().Be("vukl12333");
    }

    [Fact]
    public void BuildNameForUpdate_NullDisplayName_PreservesExisting()
    {
        var existing = new Name { GivenName = "vukl", DisplayName = "vukl" };
        var result = PeopleContactNameHelper.BuildNameForUpdate(existing, null);

        result!.GivenName.Should().Be("vukl");
        result.DisplayName.Should().Be("vukl");
    }
}
