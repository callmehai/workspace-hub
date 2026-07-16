using WorkspaceHub.Application.DTOs;

namespace WorkspaceHub.Application.Interfaces.Services;

/// <summary>
/// Business logic upload file/folder từ máy người dùng lên Google Drive (qua backend proxy).
/// Biết User/Item/Connection của app; gọi Google qua <see cref="Abstractions.IDriveGateway"/>.
/// Sau upload thành công insert Item local ngay — không chờ cron sync.
/// </summary>
public interface IDriveUploadService
{
    /// <summary>
    /// Upload một file đơn lên Drive.
    /// Validate connection + parent folder + kích thước → gateway upload → insert Item.
    /// </summary>
    /// <param name="userId">User đang đăng nhập (từ JWT).</param>
    /// <param name="connectionId">Connection Drive trong DB.</param>
    /// <param name="fileName">Tên file gốc từ máy (vd. báo-cáo.pdf).</param>
    /// <param name="contentType">MIME type từ browser/IFormFile.</param>
    /// <param name="content">Stream nội dung — truyền thẳng xuống gateway.</param>
    /// <param name="contentLength">Dung lượng byte — dùng validate trước khi upload.</param>
    /// <param name="parentItemId">Item folder cha trong app (null = My Drive gốc).</param>
    /// <param name="ct">Token hủy.</param>
    Task<ItemResponse> UploadFileAsync(
        Guid userId,
        Guid connectionId,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        Guid? parentItemId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Upload nhiều file kèm đường dẫn tương đối (từ webkitdirectory trên FE).
    /// Tự tạo cây folder trên Drive theo path rồi upload từng file vào đúng vị trí.
    /// </summary>
    /// <param name="userId">User đang đăng nhập.</param>
    /// <param name="connectionId">Connection Drive.</param>
    /// <param name="entries">Danh sách file + relativePath (vd. docs/2024/file.pdf).</param>
    /// <param name="parentItemId">Folder cha trong app (null = gốc My Drive).</param>
    /// <param name="ct">Token hủy.</param>
    Task<DriveFolderUploadResponse> UploadFolderAsync(
        Guid userId,
        Guid connectionId,
        IReadOnlyList<DriveFolderUploadEntry> entries,
        Guid? parentItemId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Một file trong batch upload folder.
/// RelativePath là đường dẫn đầy đủ kể cả tên file (từ File.webkitRelativePath).
/// </summary>
/// <param name="RelativePath">Đường dẫn tương đối, vd. MyFolder/docs/file.pdf.</param>
/// <param name="FileName">Tên file fallback nếu path thiếu.</param>
/// <param name="ContentType">MIME type.</param>
/// <param name="Content">Stream nội dung — caller chịu trách nhiệm dispose sau khi service xong.</param>
/// <param name="ContentLength">Dung lượng byte.</param>
public record DriveFolderUploadEntry(
    string RelativePath,
    string FileName,
    string ContentType,
    Stream Content,
    long ContentLength);

/// <summary>Kết quả upload folder — danh sách Item đã tạo + thống kê.</summary>
/// <param name="Items">Mọi Item mới (folder trung gian + file) để FE refresh list.</param>
/// <param name="FilesUploaded">Số file binary đã upload.</param>
/// <param name="FoldersCreated">Số folder mới tạo trên Drive (không tính folder cha sẵn có).</param>
public record DriveFolderUploadResponse(
    IReadOnlyList<ItemResponse> Items,
    int FilesUploaded,
    int FoldersCreated);
