using System.Text.Json;
using FluentAssertions;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Tests cho <see cref="ScheduledEmailMapper"/> — entity ScheduledEmail → DTO,
/// trong đó To/Cc/Bcc lưu DB dạng JSON string phải deserialize về List&lt;string&gt;.
/// </summary>
public class ScheduledEmailMapperTests
{
    private static ScheduledEmail NewEmail(Action<ScheduledEmail>? tweak = null)
    {
        var email = new ScheduledEmail
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ConnectionId = Guid.NewGuid(),
            ToJson = "[\"to@a.com\"]",
            CcJson = "[]",
            BccJson = "[]",
            Subject = "Chủ đề",
            BodyHtml = "<p>Nội dung</p>",
            SendAt = new DateTime(2026, 7, 21, 9, 0, 0, DateTimeKind.Utc),
            Status = ScheduledEmailStatus.Pending,
            RetryCount = 0,
            CreatedAt = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc)
        };
        tweak?.Invoke(email);
        return email;
    }

    [Fact]
    public void ToDto_EntityDayDu_MapMoiFieldVoHuong()
    {
        var sentAt = new DateTime(2026, 7, 21, 9, 0, 5, DateTimeKind.Utc);
        var email = NewEmail(e =>
        {
            e.Status = ScheduledEmailStatus.Sent;
            e.RetryCount = 2;
            e.LastError = "temporary failure";
            e.SentAt = sentAt;
        });

        var dto = ScheduledEmailMapper.ToDto(email);

        dto.Id.Should().Be(email.Id);
        dto.ConnectionId.Should().Be(email.ConnectionId);
        dto.Subject.Should().Be("Chủ đề");
        dto.BodyHtml.Should().Be("<p>Nội dung</p>");
        dto.SendAt.Should().Be(email.SendAt);
        dto.Status.Should().Be("Sent"); // enum expose dạng string
        dto.RetryCount.Should().Be(2);
        dto.LastError.Should().Be("temporary failure");
        dto.SentAt.Should().Be(sentAt);
        dto.CreatedAt.Should().Be(email.CreatedAt);
    }

    [Fact]
    public void ToDto_ToCcBccCoDuLieu_DeserializeThanhList()
    {
        var email = NewEmail(e =>
        {
            e.ToJson = "[\"a@x.com\",\"b@x.com\"]";
            e.CcJson = "[\"cc@x.com\"]";
            e.BccJson = "[\"bcc1@x.com\",\"bcc2@x.com\"]";
        });

        var dto = ScheduledEmailMapper.ToDto(email);

        dto.To.Should().Equal("a@x.com", "b@x.com");
        dto.Cc.Should().Equal("cc@x.com");
        dto.Bcc.Should().Equal("bcc1@x.com", "bcc2@x.com");
    }

    [Fact]
    public void ToDto_MangJsonRong_TraListRong()
    {
        var email = NewEmail(e =>
        {
            e.ToJson = "[]";
            e.CcJson = "[]";
            e.BccJson = "[]";
        });

        var dto = ScheduledEmailMapper.ToDto(email);

        dto.To.Should().BeEmpty();
        dto.Cc.Should().BeEmpty();
        dto.Bcc.Should().BeEmpty();
    }

    [Fact]
    public void ToDto_JsonRongHoacNull_TraListRongKhongThrow()
    {
        // Cột có thể rỗng với dữ liệu cũ → mapper guard bằng IsNullOrEmpty.
        var email = NewEmail(e =>
        {
            e.ToJson = string.Empty;
            e.CcJson = null!;
            e.BccJson = string.Empty;
        });

        var dto = ScheduledEmailMapper.ToDto(email);

        dto.To.Should().NotBeNull().And.BeEmpty();
        dto.Cc.Should().NotBeNull().And.BeEmpty();
        dto.Bcc.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ToDto_JsonLaChuoiNull_TraNull_HanhViHienTai()
    {
        // GHI NHẬN hành vi hiện tại: chuỗi "null" qua được guard IsNullOrEmpty, Deserialize trả null
        // và mapper dùng null-forgiving (!) nên DTO nhận null thay vì list rỗng.
        // Nếu sau này thêm guard `?? new List<string>()` thì test này phải sửa theo.
        var email = NewEmail(e => e.ToJson = "null");

        var dto = ScheduledEmailMapper.ToDto(email);

        dto.To.Should().BeNull();
    }

    [Fact]
    public void ToDto_JsonKhongHopLe_NemJsonException_HanhViHienTai()
    {
        // GHI NHẬN hành vi hiện tại: mapper KHÔNG try/catch nên JSON hỏng làm vỡ request.
        // Dữ liệu trong DB do BE tự ghi nên thực tế không xảy ra; nếu muốn an toàn hơn thì
        // bọc try/catch trong DeserializeList và test này phải sửa theo.
        var email = NewEmail(e => e.ToJson = "khong-phai-json");

        var act = () => ScheduledEmailMapper.ToDto(email);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData(ScheduledEmailStatus.Pending, "Pending")]
    [InlineData(ScheduledEmailStatus.Sent, "Sent")]
    [InlineData(ScheduledEmailStatus.Failed, "Failed")]
    [InlineData(ScheduledEmailStatus.Cancelled, "Cancelled")]
    public void ToDto_MoiStatus_MapSangChuoiTuongUng(ScheduledEmailStatus status, string expected)
    {
        var dto = ScheduledEmailMapper.ToDto(NewEmail(e => e.Status = status));

        dto.Status.Should().Be(expected);
    }

    [Fact]
    public void ToDto_ChuaGui_SentAtVaLastErrorNull()
    {
        var dto = ScheduledEmailMapper.ToDto(NewEmail());

        dto.SentAt.Should().BeNull();
        dto.LastError.Should().BeNull();
        dto.RetryCount.Should().Be(0);
        dto.Status.Should().Be("Pending");
    }
}
