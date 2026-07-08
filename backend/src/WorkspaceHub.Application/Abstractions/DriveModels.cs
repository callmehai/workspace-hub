namespace WorkspaceHub.Application.Abstractions;

public class DriveFileDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long? Size { get; set; }
    public string? WebViewLink { get; set; }
    public string? IconLink { get; set; }
    public DateTimeOffset? ModifiedTime { get; set; }
    public bool Trashed { get; set; }
    public long? Version { get; set; }
    public string? HeadRevisionId { get; set; }
}

public class DriveSyncResult
{
    public bool Expired { get; set; }
    public List<DriveFileDto> Files { get; set; } = new();
    /// <summary>Google's NewStartPageToken — cursor for next incremental sync, not a pagination token.</summary>
    public string? NextSyncCursor { get; set; }

    public DriveSyncResult(bool expired, List<DriveFileDto> files, string? nextSyncCursor)
    {
        Expired = expired;
        Files = files;
        NextSyncCursor = nextSyncCursor;
    }
}

/// <summary>
/// Quyền share trên Google Drive (reader / commenter / writer).
/// Dùng ở Service + Validator; gửi lên Google qua <see cref="DrivePermissionRoles.ToApiValue"/>.
/// </summary>

public enum DrivePermissionRole
{
    Reader,
    Commenter,
    Writer
}

/// <summary>
/// Chuỗi role Google Drive API — map 1-1 với docs Google.
/// Owner chỉ để hiển thị, không assign qua API app.
/// </summary>

public static class DrivePermissionRoloes
{
    public const string Reader = "reader";
    public const string Commenter = "commenter";
    public const string Writer = "writer";
    public const string Owner = "owner";

    // Chuyển role enum sang string để gửi lên Google API
    public static string ToApiValue(this DrivePermissionRole role)
    {
        return role switch
        {
            DrivePermissionRole.Reader => Reader,
            DrivePermissionRole.Commenter => Commenter,
            DrivePermissionRole.Writer => Writer,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };
    }

    //Parse role từ Google trả về hoặc từ request string
   public static bool TryParse(string? value, out DrivePermissionRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        switch (value.Trim().ToLowerInvariant())
        {
            case Reader:
                role = DrivePermissionRole.Reader;
                return true;
            case Commenter:
                role = DrivePermissionRole.Commenter;
                return true;
            case Writer:
                role = DrivePermissionRole.Writer;
                return true;
            default:
                return false;
        }
    }

    //Kiểm tra role là Owner (không assign được qua API app)
    public static bool IsOwner(string? role) => string.Equals(role, Owner, StringComparison.OrdinalIgnoreCase);

    //Trong gg drive (pdf/docx/..) đều lưu dạng drive.file, nên dùng minetype để check file type
    public static class DriveMimeTypes
    {
        /// <summary>MimeType của folder trên Google Drive.</summary>
        public const string Folder = "application/vnd.googgle-apps.folder";

        public static bool IsFolder(string? mimeTyppe)=>
            string.Equals(mimeTyppe, Folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// type permission từ Google: user, anyone, domain, group.
    /// v1 chủ yếu dùng User + Anyone.
    /// </summary>
    public static class DrivePermision
    {
        public const string User = "user";
        public const string Anyone = "anyone";
        public const string Domain = "domain";
        public const string Group = "group";

        /// Kiểm tra type permission là Anyone (public link)
        public static bool IsLinkType(string? type)=>
            string.Equals(type, Anyone, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Một dòng permission trả về từ Gateway / Service / API list.
    /// Map từ Google.Apis.Drive.v3.Data.Permission (Bước A2).
    /// </summary>
    public class DrivePermissionDto
    {
        //permissionId trên gg, dùng cho việc update/delete permission, không phải email
        public string Id { get;set; } = string.Empty;
        //user / anyone / domain / group
        public string Type { get;set; } = string.Empty;
        //reader / commenter / writer / owner
        public string Role { get;set; } = string.Empty;
        public string? EmailAddress { get;set; }
        public string? DisplayName { get;set; }
        //true nếu là owner, UI chỉ hiển thị khong xóa
        public bool IsOwner { get; set; }
        //true khi là link public, false nếu là link public (anyone)
        public bool IsLink { get;set; }
    }

}