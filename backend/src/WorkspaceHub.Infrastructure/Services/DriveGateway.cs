using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Domain.Entities;

namespace WorkspaceHub.Infrastructure.Services;

public class DriveGateway : IDriveGateway
{
    private const string FileFields = "id, name, mimeType, version, modifiedTime, trashed, headRevisionId";
    //Sau tạo chỉ trả về các field cần thiết, tránh trả về quá nhiều field không cần thiết.
    private const string CreateFolderFields =
    "id, name, mimeType, webViewLink, iconLink, modifiedTime, version, headRevisionId, parents, trashed";
    // Field Google trả về sau upload file — thêm size để lưu vào metadata Item local.
    private const string UploadFileFields =
        "id, name, mimeType, size, webViewLink, iconLink, modifiedTime, version, headRevisionId, parents, trashed";
    private const string PermissionFields =
    "id, type, role, emailAddress, displayName";
    private const string ListPermissionsFields =
        "permissions(id, type, role, emailAddress, displayName, deleted)";

    private readonly ITokenService _tokenService;

    public DriveGateway(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    private async Task<DriveService> BuildDriveServiceAsync(Connection connection, CancellationToken ct)
    {
        var accessToken = await _tokenService.GetFreshAccessTokenAsync(connection, ct);
        var credential = GoogleCredential.FromAccessToken(accessToken);
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WorkspaceHub"
        });
    }

    public async Task<DriveFile> GetFileAsync(Connection connection, string fileId, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var request = drive.Files.Get(fileId);
            request.Fields = FileFields;
            var file = await request.ExecuteAsync(ct);
            return MapToDto(file);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", fileId,
                forbiddenMessage: "Insufficient permissions to access this file.");
        }
    }

    public async Task<DriveFile> UpdateFileAsync(Connection connection, string fileId, string newName, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File { Name = newName };
            var request = drive.Files.Update(fileMetadata, fileId);
            request.Fields = FileFields;
            var updatedFile = await request.ExecuteAsync(ct);
            return MapToDto(updatedFile);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", fileId);
        }
    }

    public async Task<DriveFile> TrashFileAsync(Connection connection, string fileId, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File { Trashed = true };
            var request = drive.Files.Update(fileMetadata, fileId);
            request.Fields = FileFields;
            var updatedFile = await request.ExecuteAsync(ct);
            return MapToDto(updatedFile);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", fileId);
        }
    }

    public async Task<DriveFile> UntrashFileAsync(Connection connection, string fileId, CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var fileMetadata = new Google.Apis.Drive.v3.Data.File { Trashed = false };
            var request = drive.Files.Update(fileMetadata, fileId);
            request.Fields = FileFields;
            var updatedFile = await request.ExecuteAsync(ct);
            return MapToDto(updatedFile);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", fileId);
        }
    }

    /// <summary>
    /// Upload file binary lên Google Drive qua API <c>files.create</c>.
    /// Luồng: lấy token → tạo metadata (tên + folder cha) → stream nội dung lên Google.
    /// Google .NET client tự chọn simple upload (&lt;5 MB) hoặc resumable upload (file lớn hơn).
    /// Không insert Item local — việc đó do DriveUploadService xử lý ở bước sau.
    /// </summary>
    /// <param name="connection">Connection Drive của user (OAuth token).</param>
    /// <param name="name">Tên file trên Drive.</param>
    /// <param name="mimeType">MIME type gửi lên Google.</param>
    /// <param name="parentExternalId">Google id folder cha; null = My Drive root.</param>
    /// <param name="content">Stream đọc nội dung file — không buffer toàn bộ trong memory.</param>
    /// <param name="ct">Token hủy.</param>
    /// <returns><see cref="DriveFileDto"/> map từ response Google.</returns>
    public async Task<DriveFileDto> UploadFileAsync(
        Connection connection,
        string name,
        string mimeType,
        string? parentExternalId,
        Stream content,
        CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);

            // Bước 1: metadata — mô tả file (tên, vị trí folder cha) gửi kèm request tạo file.
            var metadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                Parents = string.IsNullOrEmpty(parentExternalId)
                    ? null
                    : new List<string> { parentExternalId }
            };

            // Bước 2: files.create + stream — Google client upload nội dung binary.
            var request = drive.Files.Create(metadata, content, mimeType);
            request.Fields = UploadFileFields;

            // Bước 3: chờ upload xong — resumable nếu file >5MB, simple nếu nhỏ hơn.
            var progress = await request.UploadAsync(ct);
            if (progress.Status != UploadStatus.Completed)
            {
                throw progress.Exception
                    ?? new InvalidOperationException("Upload file lên Google Drive thất bại.");
            }

            // Bước 4: lấy metadata file vừa tạo để map sang DriveFileDto.
            var created = request.ResponseBody
                ?? throw new InvalidOperationException("Google Drive không trả metadata file sau upload.");

            return MapToFileDto(created);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", parentExternalId ?? "root",
                forbiddenMessage: "Không đủ quyền upload file lên Drive.");
        }
    }

    public async Task<DriveFileDto> CreateFolderAsync(Connection connection,
        string name,
        string? parentExternalId,
        CancellationToken ct = default)
    {
        try
        {
            // ý nghĩa là tạo một đối tượng DriveService để tương tác với Google Drive API, sử dụng thông tin xác thực từ connection.
            using var drive = await BuildDriveServiceAsync(connection, ct);

            // Tạo metadata cho thư mục mới, bao gồm tên, loại MIME (thư mục) và danh sách cha nếu có.
            //Metadata này sẽ được gửi đến Google Drive API để tạo thư mục mới.
            //metadata là một đối tượng Google.Apis.Drive.v3.Data.File, được sử dụng để định nghĩa các thuộc tính của thư mục mới mà bạn muốn tạo trên Google Drive.
            var metadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                MimeType = DriveMimeTypes.Folder,
                Parents = string.IsNullOrEmpty(parentExternalId)
                    ? null
                    : new List<string> { parentExternalId }
            };
            var request = drive.Files.Create(metadata);
            request.Fields = CreateFolderFields;
            var created = await request.ExecuteAsync(ct);
            return MapToFileDto(created);
        }
        catch (Google.GoogleApiException ex)
        {

            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "Folder", parentExternalId ?? "root",
            forbiddenMessage: "Không đủ quyền tạo folder trên Drive.");
        }
    }

    public async Task<IReadOnlyList<DrivePermissionDto>> ListPermissionsAsync(
    Connection connection,
    string fileId,
    CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var request = drive.Permissions.List(fileId);
            request.Fields = ListPermissionsFields;
            var response = await request.ExecuteAsync(ct);

            if(response.Permissions == null)
            {
                return Array.Empty<DrivePermissionDto>();
            }
            return response.Permissions
                .Where(p=> p.Deleted !=true)
                .Select(MapPermissionToDto)
                .ToList();
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "File", fileId,
            forbiddenMessage: "Không đủ quyền xem danh sách chia sẻ.");
        }
    }

    public async Task<DrivePermissionDto> CreateUserPermissionAsync(
    Connection connection,
    string fileId,
    string email,
    DrivePermissionRole role,
    bool notify,
    CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            // Share trực tiếp cho một guest cụ thể với role FE chọn.
            var permission = new Google.Apis.Drive.v3.Data.Permission
            {
                Type = DrivePermissionTypes.User,
                Role = DrivePermissionRoles.ToApiValue(role),
                EmailAddress = email
            };
            var request = drive.Permissions.Create(permission, fileId);
            request.SendNotificationEmail = notify;
            request.Fields = PermissionFields;
            var created = await request.ExecuteAsync(ct);
            return MapPermissionToDto(created);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "Permission", fileId,
                forbiddenMessage: "Không đủ quyền chia sẻ file này.");
        }
    }
    public async Task<DrivePermissionDto> UpdatePermissionAsync(
    Connection connection,
    string fileId,
    string permissionId,
    DrivePermissionRole role,
    CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var patch = new Google.Apis.Drive.v3.Data.Permission
            {
                Role = DrivePermissionRoles.ToApiValue(role)
            };
            var request = drive.Permissions.Update(patch, fileId, permissionId);
            request.Fields= PermissionFields;
            var updated = await request.ExecuteAsync(ct);
            return MapPermissionToDto(updated);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "Permission", fileId,
                forbiddenMessage: "Không đủ quyền chia sẻ file này.");
        }
    }
    public async Task DeletePermissionAsync(
    Connection connection,
    string fileId,
    string permissionId,
    CancellationToken ct = default)
    {
        try
        {
            using var drive = await BuildDriveServiceAsync(connection, ct);
            await drive.Permissions.Delete(fileId, permissionId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "Permission", permissionId,
                forbiddenMessage: "Không đủ quyền gỡ chia sẻ.");
        }
    }


    public async Task<DrivePermissionDto?> SetLinkSharingAsync(
        Connection connection,
        string fileId,
        bool enabled,
        DrivePermissionRole role,
        CancellationToken ct = default)
    {
        //Lấy danh sách quyền của file/folder hiện tại
        var existing = await ListPermissionsAsync(connection, fileId, ct);
        //Lấy ra permission có type == anyone (link share) nếu có
        var linkPermission = existing.FirstOrDefault(p => DrivePermissionTypes.IsLinkType(p.Type));

        //nếu tắt share ==> phải xóa permission nếu có
        if (!enabled)
        {
            //Nếu k có permnission thì k có gì để xóa
            if (linkPermission == null)
                return null;
            //nếu có thì xóa permission
            await DeletePermissionAsync(connection, fileId, linkPermission.Id, ct);
            return null;
        }
        //Nếu đang bật share
        var roleValue = DrivePermissionRoles.ToApiValue(role);

        //Nếu có permission type== anyone 
        if (linkPermission != null)
        {
            //nếu role muốn share đã có trong permission thì k cần update
            if (string.Equals(linkPermission.Role, roleValue, StringComparison.OrdinalIgnoreCase))
            {
                return linkPermission;
            }
            //nếu role khác nhau thì update role
            return await UpdatePermissionAsync(connection, fileId, linkPermission.Id, role, ct);

        }
        try
        {
            // Bật "ai có link" khi file chưa có permission anyone.
            using var drive = await BuildDriveServiceAsync(connection, ct);
            var permission = new Google.Apis.Drive.v3.Data.Permission
            {
                Type = DrivePermissionTypes.Anyone,
                Role = roleValue
            };
            var request = drive.Permissions.Create(permission, fileId);
            request.Fields = PermissionFields;
            var created = await request.ExecuteAsync(ct);
            return MapPermissionToDto(created);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GoogleApiExceptionHandler.Handle(ex, "Drive", "Permission", fileId,
                forbiddenMessage: "Không đủ quyền bật link chia sẻ.");
        }
    }

    // ───────────────────────── Private helpers ─────────────────────────

    private static DriveFile MapToDto(Google.Apis.Drive.v3.Data.File file)
    {
        var etag = file.Version?.ToString()
            ?? file.HeadRevisionId
            ?? file.ModifiedTimeDateTimeOffset?.ToString("o");

        return new DriveFile(file.Id, etag, file.Name, file.MimeType);
    }

    private static DriveFileDto MapToFileDto(Google.Apis.Drive.v3.Data.File file)
    {
        return new DriveFileDto
        {
            Id = file.Id ?? string.Empty,
            Name = file.Name ?? string.Empty,
            MimeType = file.MimeType ?? string.Empty,
            WebViewLink = file.WebViewLink,
            IconLink = file.IconLink,
            ModifiedTime = file.ModifiedTimeDateTimeOffset,
            Size = file.Size,
            Trashed = file.Trashed ?? false,
            Version = file.Version,
            HeadRevisionId = file.HeadRevisionId,
            Parents = file.Parents
        };
    }

    private static DrivePermissionDto MapPermissionToDto(Google.Apis.Drive.v3.Data.Permission permission)
    {
        var role = permission.Role ?? string.Empty;
        var type = permission.Type ?? string.Empty;
        return new DrivePermissionDto
        {
            Id = permission.Id ?? string.Empty,
            Type = type,
            Role = role,
            EmailAddress = permission.EmailAddress,
            DisplayName = permission.DisplayName,
            IsOwner = DrivePermissionRoles.IsOwner(role),
            IsLink = DrivePermissionTypes.IsLinkType(type)
        };
    }


}
