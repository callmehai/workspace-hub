namespace WorkspaceHub.Application.Abstractions;

/// <summary>
/// Giới hạn upload Drive qua backend — FE và BE cùng tham chiếu các hằng số này.
/// Google cho phép file rất lớn (tới TB); app chặn ở mức hạ tầng EC2 + proxy qua API.
/// Service/Controller sẽ validate theo các hằng số này ở các bước tiếp theo.
/// </summary>
public static class DriveUploadLimits
{
    /// <summary>
    /// Dung lượng tối đa cho một file đơn (100 MB).
    /// Dùng trong DriveUploadService (validate) và FE (chặn trước khi gửi).
    /// </summary>
    public const long MaxFileBytes = 100L * 1024 * 1024;

    /// <summary>
    /// Số file tối đa trong một lần upload folder từ máy (webkitdirectory).
    /// Tránh request quá lớn hoặc treo browser khi chọn folder nặng.
    /// </summary>
    public const int MaxFolderFileCount = 200;

    /// <summary>
    /// Tổng dung lượng tối đa cho một lần upload folder (500 MB).
    /// Mỗi file con vẫn phải ≤ <see cref="MaxFileBytes"/>.
    /// </summary>
    public const long MaxFolderTotalBytes = 500L * 1024 * 1024;

    /// <summary>
    /// Giới hạn body HTTP cho endpoint upload 1 file (~105 MB, dư overhead multipart/form-data).
    /// Gắn vào <c>[RequestSizeLimit]</c> ở DriveController (bước sau).
    /// </summary>
    public const int RequestSizeSingleFileBytes = 105_000_000;

    /// <summary>
    /// Giới hạn body HTTP cho endpoint upload folder (~520 MB, dư overhead multipart).
    /// Gắn vào <c>[RequestSizeLimit]</c> ở DriveController (bước sau).
    /// </summary>
    public const int RequestSizeFolderUploadBytes = 520_000_000;
}
