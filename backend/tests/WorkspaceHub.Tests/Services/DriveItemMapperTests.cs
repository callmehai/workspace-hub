using System.Text.Json;
using FluentAssertions;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Enums;
using Xunit;

namespace WorkspaceHub.Tests.Services;

/// <summary>
/// Tests cho <see cref="DriveItemMapper"/> — map DriveFileDto (Google) → Item (Domain),
/// gồm MetadataJson (mimeType/isFolder/size/link/parents/isTopLevel) và ETag fallback.
/// </summary>
public class DriveItemMapperTests
{
    private readonly DriveItemMapper _mapper = new();

    private static JsonElement Metadata(string metadataJson) =>
        JsonDocument.Parse(metadataJson).RootElement;

    [Fact]
    public void ToItem_FileThuong_MapDayDuFieldCoBan()
    {
        var modified = new DateTimeOffset(2026, 7, 5, 10, 30, 0, TimeSpan.Zero);
        var file = new DriveFileDto
        {
            Id = "drive-file-1",
            Name = "báo-cáo.pdf",
            MimeType = "application/pdf",
            Size = 2048,
            WebViewLink = "https://drive.google.com/file/d/drive-file-1/view",
            IconLink = "https://drive-thirdparty.googleusercontent.com/pdf.png",
            ModifiedTime = modified,
            Version = 42
        };
        var userId = Guid.NewGuid();
        var connId = Guid.NewGuid();

        var item = _mapper.ToItem(file, userId, connId);

        item.Id.Should().NotBeEmpty();
        item.UserId.Should().Be(userId);
        item.ConnectionId.Should().Be(connId);
        item.Type.Should().Be(ItemType.File);
        item.Status.Should().Be(ItemStatus.Inbox);
        item.Title.Should().Be("báo-cáo.pdf");
        item.Snippet.Should().BeEmpty();
        item.ExternalId.Should().Be("drive-file-1");
        item.OccurredAt.Should().Be(modified.UtcDateTime);
        item.IsImportant.Should().BeFalse();
        item.IsArchived.Should().BeFalse();
        item.ETag.Should().Be("42");
    }

    [Fact]
    public void ToItem_MetadataJson_ChuaThongTinDrive()
    {
        var file = new DriveFileDto
        {
            Id = "drive-file-2",
            Name = "slide.pptx",
            MimeType = "application/vnd.ms-powerpoint",
            Size = 999,
            WebViewLink = "https://drive.google.com/view",
            IconLink = "https://icon.png",
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var meta = Metadata(_mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid()).MetadataJson!);

        meta.GetProperty("mimeType").GetString().Should().Be("application/vnd.ms-powerpoint");
        meta.GetProperty("isFolder").GetBoolean().Should().BeFalse();
        meta.GetProperty("size").GetInt64().Should().Be(999);
        meta.GetProperty("webViewLink").GetString().Should().Be("https://drive.google.com/view");
        meta.GetProperty("iconLink").GetString().Should().Be("https://icon.png");
    }

    [Fact]
    public void ToItem_MimeTypeFolder_IsFolderTrue()
    {
        var file = new DriveFileDto
        {
            Id = "folder-1",
            Name = "Dự án A",
            MimeType = DriveMimeTypes.Folder
        };

        var item = _mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid());

        Metadata(item.MetadataJson!).GetProperty("isFolder").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ToItem_CoParents_GhiMangParentsVaoMetadata()
    {
        var file = new DriveFileDto
        {
            Id = "drive-file-3",
            Name = "child.txt",
            MimeType = "text/plain",
            Parents = new List<string> { "parent-folder-1" }
        };

        var meta = Metadata(_mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid()).MetadataJson!);

        meta.GetProperty("parents").EnumerateArray()
            .Select(p => p.GetString()).Should().ContainSingle().Which.Should().Be("parent-folder-1");
    }

    [Fact]
    public void ToItem_ParentsNullHoacRong_KhongGhiKeyParents()
    {
        var noParents = new DriveFileDto { Id = "f1", Name = "a.txt", MimeType = "text/plain" };
        var emptyParents = new DriveFileDto
        {
            Id = "f2",
            Name = "b.txt",
            MimeType = "text/plain",
            Parents = new List<string>()
        };

        Metadata(_mapper.ToItem(noParents, Guid.NewGuid(), Guid.NewGuid()).MetadataJson!)
            .TryGetProperty("parents", out _).Should().BeFalse();
        Metadata(_mapper.ToItem(emptyParents, Guid.NewGuid(), Guid.NewGuid()).MetadataJson!)
            .TryGetProperty("parents", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToItem_TruyenIsTopLevel_GhiVaoMetadata(bool isTopLevel)
    {
        var file = new DriveFileDto { Id = "f3", Name = "c.txt", MimeType = "text/plain" };

        var meta = Metadata(_mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid(), isTopLevel).MetadataJson!);

        meta.GetProperty("isTopLevel").GetBoolean().Should().Be(isTopLevel);
    }

    [Fact]
    public void ToItem_IsTopLevelNull_KhongGhiKeyIsTopLevel()
    {
        // Null = để DriveSyncService tự tính sau (giữ hành vi sync cũ).
        var file = new DriveFileDto { Id = "f4", Name = "d.txt", MimeType = "text/plain" };

        var item = _mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid(), null);

        Metadata(item.MetadataJson!).TryGetProperty("isTopLevel", out _).Should().BeFalse();
    }

    [Fact]
    public void ToItem_TenRong_DungTitleMacDinh()
    {
        var file = new DriveFileDto { Id = "f5", Name = string.Empty, MimeType = "text/plain" };

        _mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid()).Title.Should().Be("(Không có tên)");
    }

    [Fact]
    public void ToItem_KhongCoModifiedTime_OccurredAtLaThoiDiemHienTai()
    {
        var file = new DriveFileDto { Id = "f6", Name = "e.txt", MimeType = "text/plain" };

        var item = _mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid());

        item.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ToItem_KhongCoVersion_ETagFallbackHeadRevisionId()
    {
        var file = new DriveFileDto
        {
            Id = "f7",
            Name = "f.txt",
            MimeType = "text/plain",
            HeadRevisionId = "head-rev-1",
            ModifiedTime = DateTimeOffset.UtcNow
        };

        _mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid()).ETag.Should().Be("head-rev-1");
    }

    [Fact]
    public void ToItem_KhongCoVersionVaHeadRevision_ETagFallbackModifiedTime()
    {
        var modified = new DateTimeOffset(2026, 7, 5, 10, 30, 0, TimeSpan.Zero);
        var file = new DriveFileDto
        {
            Id = "f8",
            Name = "g.txt",
            MimeType = "text/plain",
            ModifiedTime = modified
        };

        _mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid()).ETag.Should().Be(modified.ToString("o"));
    }

    [Fact]
    public void ToItem_KhongCoNguonVersionNao_ETagNull()
    {
        var file = new DriveFileDto { Id = "f9", Name = "h.txt", MimeType = "text/plain" };

        _mapper.ToItem(file, Guid.NewGuid(), Guid.NewGuid()).ETag.Should().BeNull();
    }
}
