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
}

public class DriveSyncResult
{
    public bool Expired { get; set; }
    public List<DriveFileDto> Files { get; set; } = new();
    public string? NextPageToken { get; set; }

    public DriveSyncResult(bool expired, List<DriveFileDto> files, string? nextPageToken)
    {
        Expired = expired;
        Files = files;
        NextPageToken = nextPageToken;
    }
}
