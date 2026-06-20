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
    /// <summary>Google's NewStartPageToken — cursor for next incremental sync, not a pagination token.</summary>
    public string? NextSyncCursor { get; set; }

    public DriveSyncResult(bool expired, List<DriveFileDto> files, string? nextSyncCursor)
    {
        Expired = expired;
        Files = files;
        NextSyncCursor = nextSyncCursor;
    }
}
