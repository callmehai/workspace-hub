namespace WorkspaceHub.Domain.Enums;

/// <summary>Vai trò hệ thống. 1 user = 1 role. Lưu DB dạng string, đẩy vào JWT claim "role".</summary>
public enum UserRole
{
    User,
    Admin
}

/// <summary>Trạng thái 1 grant OAuth.</summary>
public enum ConnectionStatus
{
    Active,
    Disconnected,
    Error
}

/// <summary>Sub-service bật được trên 1 grant OAuth.</summary>
public enum ServiceType
{
    Gmail,
    GCal,
    Drive,
    Jira
}

/// <summary>Loại cursor cho incremental sync, tuỳ provider.</summary>
public enum CursorType
{
    HistoryId, // Gmail
    PageToken, // Drive
    SyncToken  // Google Calendar
}

/// <summary>Loại Item — đồng nhất 4 nguồn về 1 model.</summary>
public enum ItemType
{
    Email,
    Event,
    File,
    Note
}

/// <summary>Cột Kanban hiện tại của Item.</summary>
public enum ItemStatus
{
    Inbox,
    Doing,
    Done
}

/// <summary>Quyền chia sẻ folder. MVP chỉ Viewer (read-only metadata).</summary>
public enum SharePermission
{
    Viewer
}

/// <summary>Vòng đời email hẹn giờ.</summary>
public enum ScheduledEmailStatus
{
    Pending,
    Sent,
    Failed,
    Cancelled
}

/// <summary>Loại liên hệ quan trọng. MVP chỉ Email.</summary>
public enum ImportantContactType
{
    Email
}

/// <summary>Phân loại thông báo in-app.</summary>
public enum NotificationType
{
    ShareInvite,
    ImportantEmail,
    SyncError,
    ScheduleSent
}
