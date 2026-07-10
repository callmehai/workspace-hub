using FluentAssertions;
using Google.Apis.PeopleService.v1.Data;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Infrastructure.Services;
using Xunit;

namespace WorkspaceHub.Tests.Services;

public class PeopleContactProfileMapperTests
{
    [Fact]
    public void FromPerson_MapsAllCoreFields()
    {
        var person = new Person
        {
            Names = [new Name { GivenName = "Vu", FamilyName = "KL", DisplayName = "Vu KL" }],
            EmailAddresses =
            [
                new EmailAddress { Value = "vu@example.com", Type = "work" },
            ],
            PhoneNumbers =
            [
                new PhoneNumber { Value = "+84123456789", Type = "mobile" },
            ],
            Birthdays =
            [
                new Birthday { Date = new Date { Month = 7, Day = 9, Year = 1990 } },
            ],
            Organizations =
            [
                new Organization { Name = "Acme", Title = "Engineer" },
            ],
        };

        var profile = PeopleContactProfileMapper.FromPerson(person);

        profile.GivenName.Should().Be("Vu");
        profile.FamilyName.Should().Be("KL");
        profile.Emails.Should().ContainSingle(e => e.Value == "vu@example.com" && e.Label == "work");
        profile.Phones.Should().ContainSingle(p => p.Value == "+84123456789");
        profile.Birthday!.Month.Should().Be(7);
        profile.Birthday.Day.Should().Be(9);
        profile.Organization!.Name.Should().Be("Acme");
        profile.Organization.Title.Should().Be("Engineer");
    }

    [Fact]
    public void ApplyProfile_UpdatePreservesFamilyNameWhenOnlyGivenNameSent()
    {
        var existing = new Person
        {
            Names = [new Name { GivenName = "Old", FamilyName = "Name", DisplayName = "Old Name" }],
            EmailAddresses = [new EmailAddress { Value = "a@x.com" }],
        };

        var target = new Person();
        var profile = new ContactProfileDto
        {
            GivenName = "New",
            Emails = [new LabeledEmailDto { Value = "a@x.com" }],
        };

        PeopleContactProfileMapper.ApplyProfile(target, profile, existing);

        target.Names.Should().ContainSingle();
        target.Names![0].GivenName.Should().Be("New");
        target.Names[0].FamilyName.Should().Be("Name");
        target.Names[0].DisplayName.Should().Be("New Name");
    }

    [Fact]
    public void ExtractDisplayName_PrefersDisplayName()
    {
        var name = new Name { DisplayName = "Alice", GivenName = "Bob" };
        PeopleContactProfileMapper.ExtractDisplayName(name).Should().Be("Alice");
    }

    [Fact]
    public void ExtractDisplayName_FallsBackToGivenAndFamily()
    {
        var name = new Name { GivenName = "Vu", FamilyName = "KL" };
        PeopleContactProfileMapper.ExtractDisplayName(name).Should().Be("Vu KL");
    }

    [Fact]
    public void ResolveDisplayName_PrefersProfileGivenFamilyOverStaleGoogleDisplayName()
    {
        var person = new Person
        {
            Names = [new Name { DisplayName = "Old Label", GivenName = "New", FamilyName = "Name" }],
        };
        var profile = new ContactProfileDto { GivenName = "New", FamilyName = "Name" };

        PeopleContactProfileMapper.ResolveDisplayName(person, profile).Should().Be("New Name");
    }

    [Fact]
    public void ResolveDisplayName_FallsBackToGoogleDisplayNameWhenProfileNameEmpty()
    {
        var person = new Person
        {
            Names = [new Name { DisplayName = "Only Display" }],
        };
        var profile = new ContactProfileDto();

        PeopleContactProfileMapper.ResolveDisplayName(person, profile).Should().Be("Only Display");
    }

    [Fact]
    public void ToJson_RoundTrip_PreservesProfile()
    {
        var original = new ContactProfileDto
        {
            GivenName = "A",
            FamilyName = "B",
            Emails = [new LabeledEmailDto { Value = "a@b.com", Label = "home" }],
        };

        var json = PeopleContactProfileMapper.ToJson(original);
        var restored = PeopleContactProfileMapper.FromJson(json);

        restored.GivenName.Should().Be("A");
        restored.FamilyName.Should().Be("B");
        restored.Emails[0].Value.Should().Be("a@b.com");
    }
}
