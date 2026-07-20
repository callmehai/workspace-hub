using FluentAssertions;
using WorkspaceHub.Application.DTOs.Contacts;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Tests cho <see cref="GoogleContactMapper"/> (SCRUM-69) — map dòng People API → entity cache
/// và entity → DTO gợi ý To/Cc/Bcc.
/// </summary>
public class GoogleContactMapperTests
{
    private readonly GoogleContactMapper _mapper = new();

    [Fact]
    public void ToEntity_RowDayDu_MapMoiFieldVaSinhIdMoi()
    {
        var row = new PeopleContactRow
        {
            Email = "bạn@gmail.com",
            DisplayName = "Nguyễn Văn A",
            Source = GoogleContactSource.Contact,
            ExternalResourceName = "people/c123"
        };
        var connectionId = Guid.NewGuid();
        var syncedAt = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc);

        var entity = _mapper.ToEntity(row, connectionId, syncedAt);

        entity.Id.Should().NotBeEmpty();
        entity.ConnectionId.Should().Be(connectionId);
        entity.Email.Should().Be("bạn@gmail.com");
        entity.DisplayName.Should().Be("Nguyễn Văn A");
        entity.Source.Should().Be(GoogleContactSource.Contact);
        entity.ExternalResourceName.Should().Be("people/c123");
        entity.SyncedAt.Should().Be(syncedAt);
    }

    [Fact]
    public void ToEntity_ThieuDisplayNameVaResourceName_GiuNull()
    {
        // otherContacts.list có thể chỉ trả email.
        var row = new PeopleContactRow
        {
            Email = "someone@example.com",
            Source = GoogleContactSource.OtherContact
        };

        var entity = _mapper.ToEntity(row, Guid.NewGuid(), DateTime.UtcNow);

        entity.DisplayName.Should().BeNull();
        entity.ExternalResourceName.Should().BeNull();
        entity.Source.Should().Be(GoogleContactSource.OtherContact);
    }

    [Fact]
    public void ToEntity_GoiNhieuLan_SinhIdKhacNhau()
    {
        var row = new PeopleContactRow { Email = "a@b.com", Source = GoogleContactSource.Contact };
        var connectionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var first = _mapper.ToEntity(row, connectionId, now);
        var second = _mapper.ToEntity(row, connectionId, now);

        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public void ToSuggestion_EntityDayDu_MapEmailTenVaSourceDangChuoi()
    {
        var entity = new GoogleContact
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            Email = "user@gmail.com",
            DisplayName = "User Name",
            Source = GoogleContactSource.OtherContact,
            SyncedAt = DateTime.UtcNow
        };

        var dto = _mapper.ToSuggestion(entity);

        dto.Email.Should().Be("user@gmail.com");
        dto.DisplayName.Should().Be("User Name");
        dto.Source.Should().Be("OtherContact"); // enum lưu/expose dạng string
    }

    [Fact]
    public void ToSuggestion_KhongCoDisplayName_TraNull()
    {
        var entity = new GoogleContact
        {
            Id = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            Email = "no-name@gmail.com",
            DisplayName = null,
            Source = GoogleContactSource.Contact,
            SyncedAt = DateTime.UtcNow
        };

        var dto = _mapper.ToSuggestion(entity);

        dto.DisplayName.Should().BeNull();
        dto.Source.Should().Be("Contact");
    }

    [Fact]
    public void ToEntity_RoiToSuggestion_GiuNguyenEmailVaTen()
    {
        // Round-trip: row → entity → suggestion (luồng thật của SendEmailService.SuggestContacts).
        var row = new PeopleContactRow
        {
            Email = "round@trip.com",
            DisplayName = "Round Trip",
            Source = GoogleContactSource.Contact
        };

        var dto = _mapper.ToSuggestion(_mapper.ToEntity(row, Guid.NewGuid(), DateTime.UtcNow));

        dto.Email.Should().Be("round@trip.com");
        dto.DisplayName.Should().Be("Round Trip");
        dto.Source.Should().Be("Contact");
    }
}
