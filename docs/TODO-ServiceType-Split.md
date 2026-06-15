# TODO: Tách ServiceType thành read/write riêng lẻ

> Chưa làm — để lại sau khi có feature write thực sự (tạo issue Jira, gửi Gmail).

## Vấn đề hiện tại

`ServiceType` enum đang gộp read + write vào 1 giá trị:
- `Gmail` → vừa đại diện cho đọc mail, vừa gửi mail
- `Jira` → vừa đọc issue, vừa tạo/sửa issue

Khi user grant write scope nhưng app chỉ có 1 `ServiceConnection`, không phân biệt được user đã grant write hay chưa.

## Hướng sửa

### 1. Thêm enum values
```csharp
public enum ServiceType
{
    Gmail,      // deprecated → giữ cho backward compat
    GCal,
    Drive,
    Jira,       // deprecated → giữ cho backward compat
    GmailRead,
    GmailSend,
    JiraRead,
    JiraWrite
}
```

### 2. Sửa ServicesFromGrantedScopes

**Google:**
```csharp
if (set.Contains(GmailReadonly)) result.Add(ServiceType.GmailRead);
if (set.Contains(GmailSend))     result.Add(ServiceType.GmailSend);
if (set.Contains(CalendarReadonly)) result.Add(ServiceType.GCal);
if (set.Contains(DriveReadonly)) result.Add(ServiceType.Drive);
```

**Jira:**
```csharp
if (set.Contains(ReadJiraWork))  result.Add(ServiceType.JiraRead);
if (set.Contains(WriteJiraWork)) result.Add(ServiceType.JiraWrite);
```

### 3. Data migration

Với các row `ServiceConnections` đang có `ServiceType = "Gmail"` hoặc `"Jira"`:
- `Gmail` → tạo row mới `GmailRead` (copy CursorType/CursorValue), xóa row cũ
- `Jira` → tạo row mới `JiraRead`, xóa row cũ
- Items/ScheduledEmails trỏ vào row cũ → repoint sang row mới trước khi xóa

### 4. ScheduledEmails validation

Hiện chỉ Gmail mới gửi được. Sau tách phải validate:
```csharp
if (serviceConnection.ServiceType != ServiceType.GmailSend)
    throw new BusinessRuleException("ServiceConnection không có quyền gửi mail");
```

### 5. Seed data

Cập nhật `SupportedServices` trong `AppDbContext`:
```csharp
// Google
SupportedServices = "[\"GmailRead\",\"GmailSend\",\"GCal\",\"Drive\"]"

// Jira
SupportedServices = "[\"JiraRead\",\"JiraWrite\"]"
```

### 6. Frontend

Response `serviceType` thay đổi từ `"Gmail"` → `"GmailRead"` / `"GmailSend"` — frontend phải update UI labels và logic tương ứng.

---

## Files cần sửa

| File | Việc cần làm |
|------|-------------|
| `Domain/Enums/Enums.cs` | Thêm GmailRead, GmailSend, JiraRead, JiraWrite |
| `OAuth/Providers/Google/GoogleScopes.cs` | Thêm GmailSend scope + update ServicesFromGrantedScopes |
| `OAuth/Providers/Jira/JiraScopes.cs` | Update ServicesFromGrantedScopes trả về JiraRead + JiraWrite |
| `OAuth/Core/ServiceConnectionSync.cs` | Kiểm tra lại ApplyGrantedScopes nếu cần |
| `Infrastructure/Data/AppDbContext.cs` | Update seed SupportedServices |
| `[Migration mới]` | Data migration repoint Items + ScheduledEmails |
| Frontend | Update serviceType string handling |

---

## Rủi ro cần chú ý

- **Items orphan**: Phải repoint `ServiceConnectionId` trước khi xóa row cũ
- **ScheduledEmails stranded**: Nếu không repoint đúng → FK NoAction gây lỗi khi xóa
- **Sync trùng**: GmailRead + GmailSend cùng sync 1 mailbox → cần chỉ sync trên GmailRead
