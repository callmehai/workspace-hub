# Database Schema — Workspace Hub (Mô hình B)

> Cập nhật 2026-06-11: bỏ tách OAuthConnections/ServiceConnections, gộp thành **Connections** (mỗi service 1 row, token riêng). Thêm Google Sign-In + write-back fields. Lịch sử: CHANGELOG.md.
>
> **Status:** ✅ schema này ĐÃ áp dụng vào code — migration `ModelBConnections` (SCRUM-34, sau `InitialCreate` + `UsersMultiAuth`). DB là **SQL Server**: JSON lưu `nvarchar(max)`, datetime `datetime2` UTC, enum lưu string.
>
> Ghi chú: các giá trị `Jira` / `JiraAccount` / `Ticket` bên dưới được **seed sẵn trong enum** nhưng Jira/Atlassian **không nằm trong scope hiện tại** (không có ticket Jira) — chỉ là chỗ trống cho tương lai, đừng implement.

## Quan hệ tổng quan
```
Users 1──n Connections 1──n Items
Users 1──n Folders 1──n ItemFolders n──1 Items
Users 1──n Tags 1──n TagAssignments n──1 Items
Users 1──n ImportantContacts
Users 1──n ScheduledEmails ──n──1 Connections
Users 1──n Notifications
Integrations 1──n Connections
Folders 1──n FolderShares ──n──1 Users
```

---

## Users
Đăng nhập bằng password HOẶC Google (auto-link nếu email trùng).

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| Email | string UNIQUE | |
| PasswordHash | string **null** | null nếu chỉ đăng nhập Google |
| GoogleSub | string null UNIQUE | `sub` từ Google, định danh ổn định |
| AuthProvider | enum string | Local / Google / Both |
| FullName | string | |
| AvatarUrl | string null | |
| IsActive | bool | false = khoá |
| LockedReason | string null | |
| LastLoginAt | datetime null | |
| Role | string | |
| CreatedAt | datetime | |

> UNIQUE filtered index cho GoogleSub (chỉ khi not null).

## Role
KHÔNG có bảng Roles/UserRoles (code thật dùng cột `Users.Role` string `Admin`/`User`, đẩy vào JWT claim `role`). 1 user = 1 role.

---

## Integrations
Catalog provider. Seed Google. (Atlassian là chỗ trống tương lai — không seed/không dùng ở scope hiện tại.)

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| Key | string UNIQUE | google (/ atlassian — tương lai) |
| DisplayName, IconUrl, Description | string | |
| Provider | string | Google (/ Atlassian — tương lai) |
| AuthorizationEndpoint, TokenEndpoint | string | |
| SupportedServices | nvarchar(max) (JSON) | `["Gmail","GCal","Drive"]` |
| IsEnabled | bool | |

> Bỏ cột DefaultScopes — scope suy từ ServiceType trong code (`GoogleScopes.BuildRequestScopes`).
> Không có cột ClientId/ClientSecret — OAuth credentials đọc từ config `OAuth:{key}:...`, không lưu DB (SCRUM-39).

---

## Connections  ⭐ (thay OAuthConnections + ServiceConnections)
Mỗi service = 1 row độc lập, token riêng. Bật service = tạo 1 row, full scope.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK→Users | CASCADE |
| IntegrationId | uuid FK→Integrations | |
| Provider | enum string | Google (Atlassian — chỗ trống tương lai) |
| ServiceType | enum string | Gmail / GCal / Drive (Jira — seed sẵn, chưa dùng) |
| ProviderAccountId | string | account nào (email/sub) |
| AccessTokenEncrypted | string | Data Protection |
| RefreshTokenEncrypted | string | Data Protection; **chuỗi rỗng `""` = provider không trả refresh token** (vd Google re-consent) — check `IsNullOrEmpty`, không check null |
| ExpiresAt | datetime | refresh nếu < 5 phút |
| Status | enum string | Active / Disconnected / Error |
| CursorType | enum string null | HistoryId / PageToken / SyncToken |
| CursorValue | string null | null = sync lần đầu |
| LastSyncedAt | datetime null | cập nhật sau mỗi lần sync on-demand |
| LastError | string null | |
| CreatedAt | datetime | |

**KHÔNG có:** cột Scopes (suy từ ServiceType), cột Permission (bật là full).
**Constraint:** UNIQUE(UserId, Provider, ServiceType, ProviderAccountId).
**Disconnect:** xoá đúng row → không ảnh hưởng service khác. Items giữ lại (ConnectionId = NULL).

> Lưu ý implement: FK `Items.ConnectionId` và `ScheduledEmails.ConnectionId` để **NoAction ở DB** (SQL Server cấm multiple cascade path User→Items và User→Connections→Items). Semantics "set NULL khi disconnect" xử lý ở **service layer** trước khi xoá Connection — xem comment trong `AppDbContext`.

---

## Folders / FolderShares / ItemFolders
Không đổi so với bản trước. Folders (OwnerId, Name, Color, Icon, SortOrder, IsArchived). FolderShares (Viewer-only, metadata, không thấy body). ItemFolders (composite PK, Position).

---

## Items
Lõi app. Thêm ETag cho write-back. ConnectionId thay ServiceConnectionId.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK→Users | CASCADE |
| Type | enum string | Email / Event / File / Note (Ticket — chỗ trống tương lai) |
| Title | string | |
| Snippet | string | ~200 ký tự |
| ExternalId | string null | ID gốc provider; NULL cho Note |
| **ConnectionId** | uuid FK→Connections null | thay ServiceConnectionId; SET NULL khi xoá connection |
| **ETag** | string null | version provider, cho conflict (409) |
| Status | enum string | Inbox / Doing / Done |
| OccurredAt | datetime | sort |
| DueAt | datetime null | event start |
| IsImportant | bool | |
| IsArchived | bool | |
| MetadataJson | nvarchar(max) (JSON) | field riêng từng type |

**MetadataJson shape:**
- Email: `{from, to[], threadId, labels[], hasAttachment, isUnread, isStarred, webUrl}`  ← thêm isUnread/isStarred cho 2 chiều
- Event: `{start, end, location, attendees[], meetUrl}`
- File: `{mimeType, size, webViewLink, iconLink}`
- Note: `{contentMarkdown}`
- Ticket (tương lai, nếu làm Jira): `{issueKey, projectKey, status, assignee, priority, issueType, issueUrl}`

**Constraint:** UNIQUE(ConnectionId, ExternalId). **Index:** (UserId, Status, OccurredAt DESC).

---

## Tags / TagAssignments / ImportantContacts / Notifications
Không đổi cấu trúc.
- Tags (UserId, Name không unique toàn hệ thống, Color).
- TagAssignments composite PK.
- ImportantContacts (Type: Email; tương lai có thể thêm JiraAccount; UNIQUE(UserId,Type,Identifier)).
- Notifications (Type: share_invite/important_email/sync_error/schedule_sent; tương lai thêm friend_request/automation_triggered nếu làm).

## ScheduledEmails
Đổi tham chiếu sang Connections.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK→Users | |
| **ConnectionId** | uuid FK→Connections | thay ServiceConnectionId; phải là ServiceType=Gmail |
| ToJson/CcJson/BccJson | nvarchar(max) (JSON) | |
| Subject, BodyHtml | string | |
| SendAt | datetime | |
| Status | enum string | Pending/Sent/Failed/Cancelled |
| RetryCount | int | max 3 |
| LastError | string null | |
| SentAt | datetime null | |

**Index:** (Status, SendAt) WHERE Status='Pending'.
