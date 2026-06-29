# Database Schema — Workspace Hub (Mô hình B)

> Cập nhật 2026-06-11: bỏ tách OAuthConnections/ServiceConnections, gộp thành **Connections** (mỗi service 1 row, token riêng). Thêm Google Sign-In + write-back fields. Lịch sử: CHANGELOG.md.
>
> **Status:** ✅ schema này ĐÃ áp dụng vào code — migration `ModelBConnections` (SCRUM-34, sau `InitialCreate` + `UsersMultiAuth`). DB là **SQL Server**: JSON lưu `nvarchar(max)`, datetime `datetime2` UTC, enum lưu string.
>
> Ghi chú: các giá trị `Jira` / `JiraAccount` / `Ticket` bên dưới được **seed sẵn trong enum**. Jira/Atlassian giờ **đã có phase lên kế hoạch (SCRUM-54→60)** nhưng **chưa code** — current phase vẫn dừng ở SCRUM-38. Phần dưới mô tả schema target cho phase Jira (đánh dấu rõ "phase Jira"); đừng implement tới khi tới lượt.

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
Catalog provider. Seed Google + Atlassian. (Atlassian seed đã có từ migration `AddAtlassianIntegrationSeed` — SCRUM-54 Done; `IsEnabled=false` cho đến khi admin bật qua `/api/admin/integrations/atlassian/enable` và cấu hình `OAuth:atlassian:ClientId/Secret`.)

| Cột | Kiểu | Ghi chú |
|---|---|---|
| Id | uuid PK | |
| Key | string UNIQUE | google / atlassian (đã seed cả 2 — SCRUM-54) |
| DisplayName, IconUrl, Description | string | |
| Provider | string | Google / Atlassian |
| AuthorizationEndpoint, TokenEndpoint | string | |
| SupportedServices | nvarchar(max) (JSON) | `["Gmail","GCal","Drive"]` / `["Jira"]` |
| IsEnabled | bool | |

> **SCRUM-54 Done:** row `atlassian` đã seed (`IsEnabled=false`). `ProviderAccountId` của Connection = **cloudId** (id của Jira Cloud site từ `GET /oauth/token/accessible-resources`). Credentials đọc từ config `OAuth:atlassian:ClientId/Secret` (như Google, SCRUM-39). Bật integration qua admin API khi có credentials.

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
| ServiceType | enum string | Gmail / GCal / Drive (Jira — seed sẵn; bật ở phase Jira SCRUM-54) |
| ProviderAccountId | string | account nào (email/sub Google; **cloudId** cho Jira — phase Jira) |
| AccessTokenEncrypted | string | Data Protection |
| RefreshTokenEncrypted | string | Data Protection; **chuỗi rỗng `""` = provider không trả refresh token** (vd Google re-consent) — check `IsNullOrEmpty`, không check null |
| ExpiresAt | datetime | refresh nếu < 5 phút |
| Status | enum string | Active / Disconnected / Error |
| CursorType | enum string null | HistoryId / PageToken / SyncToken / **JqlUpdated** (Jira: cursor theo `fields.updated` lưu ISO-8601 UTC mốc max; JQL lần sau `updated >= cursor`. ✅ dùng ở `JiraSyncService` — SCRUM-55) |
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
| Type | enum string | Email / Event / File / Note / Ticket (Jira — ✅ dùng ở `JiraSyncService`, SCRUM-55) |
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
- Ticket (Jira, ✅ SCRUM-55): `{issueKey, projectKey, status, assignee, priority, issueType, issueUrl}`. `ETag` = `fields.updated` (ISO-8601 UTC) làm version-token cho conflict (SCRUM-57). Description gốc là ADF → `AdfConverter.ToPlainText` lấy Snippet (đọc); ghi ngược (text→ADF) ở SCRUM-57. Xem CHANGELOG.

**Constraint:** UNIQUE(ConnectionId, ExternalId). **Index:** (UserId, Status, OccurredAt DESC).

---

## Tags / TagAssignments / ImportantContacts / Notifications
Không đổi cấu trúc.
- Tags (UserId, Name không unique toàn hệ thống, Color).
- TagAssignments composite PK.
- ImportantContacts (Type: Email / **JiraAccount** — ✅ SCRUM-60, Identifier=email (Email) / accountId (JiraAccount); UNIQUE(UserId,Type,Identifier)). CRUD: `GET/POST /api/importantcontacts`, `DELETE /{id}`. Enum lưu string nên thêm JiraAccount KHÔNG cần migration.
- Notifications (Type: share_invite/important_email/sync_error/schedule_sent; **phase Jira (SCRUM-60, optional) thêm type cho Jira** — vd jira_assigned/jira_mention; tương lai thêm friend_request/automation_triggered nếu làm).

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
