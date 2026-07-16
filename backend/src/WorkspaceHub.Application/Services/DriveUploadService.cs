using System.Text.Json;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Common;
using WorkspaceHub.Application.DTOs;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services;

/// <summary>
/// Orchestrate upload file/folder lên Google Drive (bước 2).
/// Luồng chung: validate → resolve parent → gateway upload → mapper → insert Item.
/// Controller (bước 3) chỉ nhận multipart và gọi service này.
/// </summary>
public class DriveUploadService : IDriveUploadService
{
    private readonly IDriveGateway _gateway;
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;
    private readonly IDriveItemMapper _mapper;

    public DriveUploadService(
        IDriveGateway gateway,
        IItemRepository items,
        IConnectionRepository connections,
        IDriveItemMapper mapper)
    {
        _gateway = gateway;
        _items = items;
        _connections = connections;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public async Task<ItemResponse> UploadFileAsync(
        Guid userId,
        Guid connectionId,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        Guid? parentItemId = null,
        CancellationToken ct = default)
    {
        ValidateFileName(fileName);
        ValidateFileSize(contentLength);

        var conn = await GetValidDriveConnectionAsync(connectionId, userId, ct);
        var parentExternalId = await ResolveParentExternalIdAsync(
            connectionId, userId, parentItemId, ct);

        var mimeType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType.Trim();

        // Gọi gateway — stream đi thẳng lên Google, không buffer trong service.
        var driveFile = await _gateway.UploadFileAsync(
            conn,
            fileName.Trim(),
            mimeType,
            parentExternalId,
            content,
            ct);

        // Insert Item local ngay để UI thấy file mới, không chờ sync cron.
        var item = _mapper.ToItem(driveFile, userId, connectionId);
        await _items.AddAsync(item, ct);
        await _items.SaveChangesAsync(ct);

        return MapToItemResponse(item);
    }

    /// <inheritdoc />
    public async Task<DriveFolderUploadResponse> UploadFolderAsync(
        Guid userId,
        Guid connectionId,
        IReadOnlyList<DriveFolderUploadEntry> entries,
        Guid? parentItemId = null,
        CancellationToken ct = default)
    {
        if (entries.Count == 0)
            throw new BusinessRuleException("Không có file nào để upload.");

        if (entries.Count > DriveUploadLimits.MaxFolderFileCount)
            throw new BusinessRuleException(
                $"Tối đa {DriveUploadLimits.MaxFolderFileCount} file mỗi lần upload folder.");

        var totalBytes = entries.Sum(e => e.ContentLength);
        if (totalBytes > DriveUploadLimits.MaxFolderTotalBytes)
            throw new BusinessRuleException("Tổng dung lượng upload folder vượt quá 500 MB.");

        foreach (var entry in entries)
        {
            ValidateFileName(entry.FileName);
            ValidateFileSize(entry.ContentLength);
        }

        var conn = await GetValidDriveConnectionAsync(connectionId, userId, ct);
        var rootParentExternalId = await ResolveParentExternalIdAsync(
            connectionId, userId, parentItemId, ct);

        // Cache path folder tương đối → Google file id (tránh tạo trùng khi nhiều file cùng thư mục).
        var folderExternalIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var createdItems = new List<ItemResponse>();
        var foldersCreated = 0;

        foreach (var entry in entries)
        {
            var (fileName, dirPath) = SplitRelativePath(entry.RelativePath, entry.FileName);

            var pathResult = await EnsureFolderPathAsync(
                conn,
                connectionId,
                userId,
                rootParentExternalId,
                dirPath,
                folderExternalIds,
                createdItems,
                ct);
            foldersCreated += pathResult.FoldersCreated;

            var mimeType = string.IsNullOrWhiteSpace(entry.ContentType)
                ? "application/octet-stream"
                : entry.ContentType.Trim();

            var driveFile = await _gateway.UploadFileAsync(
                conn,
                fileName,
                mimeType,
                pathResult.ParentExternalId,
                entry.Content,
                ct);

            var item = _mapper.ToItem(driveFile, userId, connectionId);
            await _items.AddAsync(item, ct);
            createdItems.Add(MapToItemResponse(item));
        }

        await _items.SaveChangesAsync(ct);

        return new DriveFolderUploadResponse(
            createdItems,
            FilesUploaded: entries.Count,
            FoldersCreated: foldersCreated);
    }

    // ───────────────────────── Private helpers ─────────────────────────

    /// <summary>Chặn file rỗng hoặc vượt <see cref="DriveUploadLimits.MaxFileBytes"/>.</summary>
    private static void ValidateFileSize(long contentLength)
    {
        if (contentLength <= 0)
            throw new BusinessRuleException("File rỗng không thể upload.");

        if (contentLength > DriveUploadLimits.MaxFileBytes)
            throw new BusinessRuleException("File vượt quá giới hạn 100 MB.");
    }

    /// <summary>Chặn tên file trống hoặc quá dài (Google giới hạn 255 ký tự).</summary>
    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new BusinessRuleException("Tên file không được để trống.");

        if (fileName.Trim().Length > 255)
            throw new BusinessRuleException("Tên file tối đa 255 ký tự.");
    }

    /// <summary>
    /// Map parentItemId (Guid trong app) → externalId folder trên Google.
    /// Null = upload vào gốc My Drive.
    /// </summary>
    private async Task<string?> ResolveParentExternalIdAsync(
        Guid connectionId,
        Guid userId,
        Guid? parentItemId,
        CancellationToken ct)
    {
        if (!parentItemId.HasValue)
            return null;

        var parent = await _items.GetByIdAndUserAsync(parentItemId.Value, userId, ct)
            ?? throw new NotFoundException("Item", parentItemId.Value);

        if (parent.ConnectionId != connectionId)
            throw new BusinessRuleException("Folder cha phải thuộc cùng connection Drive.");

        if (!IsDriveFolder(parent))
            throw new BusinessRuleException("parentItemId phải trỏ tới folder Drive.");

        if (parent.IsArchived)
            throw new BusinessRuleException("Không thể upload vào folder đã trash.");

        return parent.ExternalId;
    }

    /// <summary>
    /// Tạo dần cây folder theo đường dẫn tương đối (vd. docs/2024 → tạo docs rồi 2024).
    /// Mỗi folder mới cũng insert Item local.
    /// </summary>
    private async Task<(string? ParentExternalId, int FoldersCreated)> EnsureFolderPathAsync(
        Connection conn,
        Guid connectionId,
        Guid userId,
        string? baseParentExternalId,
        string? dirPath,
        Dictionary<string, string> folderExternalIds,
        List<ItemResponse> createdItems,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dirPath))
            return (baseParentExternalId, 0);

        var normalized = dirPath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized))
            return (baseParentExternalId, 0);

        if (folderExternalIds.TryGetValue(normalized, out var cachedId))
            return (cachedId, 0);

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? parentId = baseParentExternalId;
        var currentPath = string.Empty;
        var foldersCreated = 0;

        foreach (var segment in segments)
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}/{segment}";

            if (folderExternalIds.TryGetValue(currentPath, out var existingId))
            {
                parentId = existingId;
                continue;
            }

            var created = await _gateway.CreateFolderAsync(conn, segment, parentId, ct);
            folderExternalIds[currentPath] = created.Id;
            parentId = created.Id;
            foldersCreated++;

            var folderItem = _mapper.ToItem(created, userId, connectionId);
            await _items.AddAsync(folderItem, ct);
            createdItems.Add(MapToItemResponse(folderItem));
        }

        return (parentId, foldersCreated);
    }

    /// <summary>
    /// Tách webkitRelativePath thành thư mục cha + tên file.
    /// Vd. "MyFolder/docs/file.pdf" → dirPath="MyFolder/docs", fileName="file.pdf".
    /// </summary>
    private static (string FileName, string? DirPath) SplitRelativePath(string relativePath, string fallbackFileName)
    {
        var path = string.IsNullOrWhiteSpace(relativePath)
            ? fallbackFileName
            : relativePath.Replace('\\', '/').TrimStart('/');

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new BusinessRuleException("Đường dẫn file không hợp lệ.");

        var fileName = parts[^1];
        var dirPath = parts.Length > 1
            ? string.Join('/', parts[..^1])
            : null;

        return (fileName, dirPath);
    }

    /// <summary>Đảm bảo connection thuộc user, loại Drive, trạng thái Active.</summary>
    private async Task<Connection> GetValidDriveConnectionAsync(
        Guid connectionId,
        Guid userId,
        CancellationToken ct)
    {
        var conn = await _connections.GetByIdAsync(connectionId, ct);
        if (conn is null || conn.UserId != userId)
            throw new NotFoundException("Connection", connectionId);

        if (conn.ServiceType != ServiceType.Drive)
            throw new BusinessRuleException("Connection không phải Google Drive.");

        if (conn.Status != ConnectionStatus.Active)
            throw new BusinessRuleException("Drive connection không active.");

        return conn;
    }

    /// <summary>Kiểm tra item là folder Drive qua metadata isFolder hoặc mimeType.</summary>
    private static bool IsDriveFolder(Item item)
    {
        if (string.IsNullOrEmpty(item.MetadataJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(item.MetadataJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("isFolder", out var isFolder) && isFolder.ValueKind == JsonValueKind.True)
                return true;

            if (root.TryGetProperty("mimeType", out var mime))
                return DriveMimeTypes.IsFolder(mime.GetString());
        }
        catch (JsonException)
        {
            // Metadata hỏng → coi như không phải folder.
        }

        return false;
    }

    private static ItemResponse MapToItemResponse(Item item) => new(
        Id: item.Id,
        Type: item.Type,
        Title: item.Title,
        Snippet: item.Snippet,
        Status: item.Status,
        OccurredAt: item.OccurredAt,
        DueAt: item.DueAt,
        IsImportant: item.IsImportant,
        ExternalId: item.ExternalId,
        MetadataJson: item.MetadataJson,
        FolderIds: new List<Guid>(),
        Tags: new List<ItemTag>(),
        ConnectionId: item.ConnectionId);
}
