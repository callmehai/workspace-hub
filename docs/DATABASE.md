# Database Schema — Workspace Hub (Sprint 1–3 scope)

> Chỉ gồm các bảng trong scope hiện tại. Các bảng future (Friendships, AutomationRules, WebhookChannels...) KHÔNG nằm ở đây.
> **DB: SQL Server** (dev + prod). JSON lưu `nvarchar(max)`; datetime lưu `datetime2` (luôn UTC). Parse JSON ở frontend.

## Quan hệ tổng quan

```
Users 1──n OAuthConnections 1──n ServiceConnections 1──n Items
Users 1──n Folders 1──n ItemFolders n──1 Items
Users 1──n Tags 1──n TagAssignments n──1 Items
Users 1──n ImportantContacts
Users 1──n ScheduledEmails ──n──1 ServiceConnections
Users 1──n Notifications
Integrations 1──n OAuthConnections
Folders 1──n FolderShares ──n──1 Users (SharedWithUserId / CreatedByUserId)
```

- Users là gốc của hầu hết bảng (1-n).
- Items ↔ Folders: nhiều-nhiều qua ItemFolders.
- Items ↔ Tags: nhiều-nhiều qua TagAssignments.
- OAuthConnection → ServiceConnection: 1 grant Google bật nhiều service.
- Item → ServiceConnection: n-1, nullable (Note không thuộc service nào).

---

## Nhóm 1 — Auth & RBAC

### Users
Tài khoản local. Không lưu mật khẩu thật, chỉ BCrypt hash (cost 12).
**1 user = 1 role** (không bảng junction). Role lưu thẳng cột string trên Users.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| Email | string UNIQUE | dùng đăng nhập |
| PasswordHash | string | BCrypt cost 12 |
| FullName | string | |
| AvatarUrl | string null | |
| IsActive | bool | false = bị Admin khoá, không login được |
| LockedReason | string null | |
| LastLoginAt | datetime2 null | |
| Role | string | `Admin` / `User` — mặc định `User` khi register. Đẩy vào JWT claim `role` |
| CreatedAt | datetime2 | |

> Không có bảng `Roles`/`UserRoles`. Phân quyền cấp trang qua claim `role` + `[Authorize(Roles="Admin")]`. Phân quyền cấp resource (folder) qua `FolderShares`.

---

## Nhóm 2 — Integration & OAuth

### Integrations
Catalog provider OAuth. Seed sẵn 1 row Google.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| Key | string UNIQUE | slug route callback (vd `google`) |
| DisplayName | string | "Google Workspace" |
| IconUrl | string | |
| Description | string | |
| Provider | string | nhóm provider |
| ClientIdEncrypted | string | Data Protection |
| ClientSecretEncrypted | string | Data Protection |
| AuthorizationEndpoint | string | |
| TokenEndpoint | string | |
| DefaultScopes | string | scope mặc định (readonly cho sync 1 chiều) |
| SupportedServices | nvarchar(max) | `["Gmail","GCal","Drive"]` |
| IsEnabled | bool | |

Seed: `{ Key:"google", DisplayName:"Google Workspace", Provider:"Google", DefaultScopes:"gmail.readonly calendar.readonly drive.readonly", SupportedServices:["Gmail","GCal","Drive"] }`

> Lưu ý scope: hiện tại readonly vì sync 1 chiều. Khi nào làm 2 chiều mới đổi sang gmail.modify/calendar/drive.file — KHÔNG đổi bây giờ.

### OAuthConnections
1 grant OAuth thật của user vào Google.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK→Users | CASCADE |
| IntegrationId | uuid FK→Integrations | |
| ProviderAccountId | string | email/sub — phân biệt nhiều account |
| AccessTokenEncrypted | string | Data Protection |
| RefreshTokenEncrypted | string | Data Protection |
| ExpiresAt | datetime2 | refresh nếu còn < 5 phút |
| Scopes | string | scope thực được cấp |
| Status | enum string | Active / Disconnected / Error |
| LastRefreshedAt | datetime2 null | |

**Constraint:** UNIQUE(UserId, IntegrationId, ProviderAccountId)

### ServiceConnections
1 grant Google bật nhiều sub-service. Đã gộp SyncStates cũ vào đây (cursor incremental sync).

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| OAuthConnectionId | uuid FK | CASCADE |
| ServiceType | enum string | Gmail / GCal / Drive |
| IsEnabled | bool | user toggle |
| DisplayName | string null | |
| CursorType | enum string null | HistoryId (Gmail) / PageToken (Drive) / SyncToken (GCal) |
| CursorValue | string null | null = sync lần đầu (dùng list thay history.list) |
| LastSyncedAt | datetime2 null | |
| LastError | string null | |

---

## Nhóm 3 — Core Workspace

### Folders
Container theo context. Mỗi folder có Kanban 3 cột.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| OwnerId | uuid FK→Users | toàn quyền |
| Name | string | |
| Color | string | |
| Icon | string | |
| SortOrder | int | drag-drop |
| IsArchived | bool | |

### FolderShares
Owner mời teammate xem folder. MVP chỉ Viewer (read-only metadata, KHÔNG thấy body).

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| FolderId | uuid FK→Folders | CASCADE |
| SharedWithUserId | uuid FK→Users | người được mời |
| CreatedByUserId | uuid FK→Users | ai mời |
| Permission | enum string | Viewer (MVP) |
| AcceptedAt | datetime2 null | null = pending |
| ExpiresAt | datetime2 null | |

**Privacy:** Viewer chỉ thấy metadata Item (đã lưu DB), KHÔNG thấy body. Kiểm tra quyền ở mọi API có folderId.

### Items
Lõi app — 1 đơn vị thông tin. Đồng nhất 4 type về 1 model; field riêng lưu trong MetadataJson.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK→Users | CASCADE |
| Type | enum string | Email / Event / File / Note |
| Title | string | |
| Snippet | string | preview ~200 ký tự |
| ExternalId | string null | ID gốc Google; NULL cho Note |
| ServiceConnectionId | uuid FK null | NULL cho Note; SET NULL khi xoá connection |
| Status | enum string | Inbox / Doing / Done |
| OccurredAt | datetime2 | sort |
| DueAt | datetime2 null | event start; null cho khác |
| IsImportant | bool | |
| IsArchived | bool | |
| MetadataJson | nvarchar(max) | field riêng từng type |

**MetadataJson shape:**
- Email: `{from, to[], threadId, labels[], hasAttachment, webUrl}`
- Event: `{start, end, location, attendees[], meetUrl}`
- File: `{mimeType, size, webViewLink, iconLink}`
- Note: `{contentMarkdown}`

**Constraint:** UNIQUE(ServiceConnectionId, ExternalId) chống duplicate khi re-sync.
**Index:** (UserId, Status, OccurredAt DESC)

### ItemFolders (junction)
| Cột | Kiểu | Ghi chú |
|---|---|---|
| ItemId + FolderId | uuid + uuid | composite PK |
| Position | int | thứ tự Kanban trong cùng folder + status |
| AddedAt | datetime2 | |

CASCADE cả 2 phía.

---

## Nhóm 4 — Features

### Tags
Label user tự tạo, filter chéo. Private (không share).

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK→Users | |
| Name | string | KHÔNG unique toàn hệ thống |
| Color | string | |

### TagAssignments (junction)
| Cột | Kiểu | Ghi chú |
|---|---|---|
| TagId + ItemId | uuid + uuid | composite PK |
| AssignedAt | datetime2 | |

### ImportantContacts
Item sync về có `from` match list → tự set IsImportant=true.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK→Users | |
| Type | enum string | Email (MVP chỉ Email) |
| Identifier | string | vd boss@company.com |
| Label | string | vd "Sếp Tổng" |

**Constraint:** UNIQUE(UserId, Type, Identifier) → 409 nếu trùng.

### ScheduledEmails
Email hẹn giờ. BE lưu Pending; cron ngoài gọi /process-scheduled mỗi 5 phút.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK→Users | |
| ServiceConnectionId | uuid FK | chỉ Gmail gửi được |
| ToJson / CcJson / BccJson | nvarchar(max) | list recipient |
| Subject | string | |
| BodyHtml | string | |
| SendAt | datetime2 | |
| Status | enum string | Pending / Sent / Failed / Cancelled |
| RetryCount | int | max 3 rồi Failed |
| LastError | string null | |
| SentAt | datetime2 null | |

**Index:** (Status, SendAt) WHERE Status='Pending'

### Notifications
Thông báo in-app.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK→Users | |
| Type | enum string | share_invite / important_email / sync_error / schedule_sent |
| Title | string | |
| Body | string | |
| LinkUrl | string | |
| IsRead | bool | |
| CreatedAt | datetime2 | |

**Index:** (UserId, IsRead, CreatedAt DESC)
