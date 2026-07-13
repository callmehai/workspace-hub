namespace WorkspaceHub.Domain.Enums;

/// <summary>Vai trò hệ thống. 1 user = 1 role. Lưu DB dạng string, đẩy vào JWT claim "role".</summary>
public enum UserRole
{
    User,
    Admin
}

/// <summary>How the user authenticates. Local = password only, Google = Google OAuth only, Both = both methods linked.</summary>
public enum AuthProvider
{
    Local,
    Google,
    Both
}

/// <summary>Nhóm provider của 1 Connection. Atlassian dùng ở phase Jira.</summary>
public enum ProviderType
{
    Google,
    Atlassian
}

/// <summary>Trạng thái 1 connection.</summary>
public enum ConnectionStatus
{
    Active,
    Disconnected,
    Error
}

/// <summary>Service gắn với 1 Connection (mô hình B: mỗi service 1 connection riêng).</summary>
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
    HistoryId,   // Gmail
    PageToken,   // Drive
    SyncToken,   // Google Calendar
    JqlUpdated   // Jira — lưu mốc fields.updated gần nhất, poll issue đổi sau đó
}

/// <summary>Loại Item — đồng nhất 4 nguồn về 1 model.</summary>
public enum ItemType
{
    Email,
    Event,
    File,
    Note,
    Ticket  // Jira issue — phase Jira (SCRUM-55)
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

/// <summary>Loại liên hệ quan trọng. Email (Gmail) + JiraAccount (phase Jira — SCRUM-60).</summary>
public enum ImportantContactType
{
    Email,
    JiraAccount   // Jira accountId — đánh dấu 1 account Jira là liên hệ quan trọng
}

/// <summary>Nguồn contact cache từ Google People API (SCRUM-69).</summary>
public enum GoogleContactSource
{
    Contact,       // Danh bạ đã lưu (connections.list)
    OtherContact   // Người từng mail (otherContacts.list) — read-only trên Google
}

/// <summary>Phân loại thông báo in-app.</summary>
public enum NotificationType
{
    ShareInvite,
    ImportantEmail,
    SyncError,
    ScheduleSent,
    ItemSynced,
    CalendarReminder,
    CalendarInvite
}

/// <summary>Trạng thái phản hồi lời mời Calendar, tương ứng responseStatus của Google.</summary>
public enum CalendarInvitationStatus
{
    NeedsAction,
    Accepted,
    Tentative,
    Declined
}

/// <summary>Kênh nhắc nhở — lưu cột <c>ReminderType</c> (enum string).</summary>
public enum ReminderType
{
    GooglePopup,
    GoogleEmail,
    InApp
}

/// <summary>Đơn vị thời gian nhắc nhở.</summary>
public enum ReminderUnit
{
    Minutes,
    Hours,
    Days,
    Weeks
}
