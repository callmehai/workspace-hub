# API Design — Workspace Hub (Mô hình B + 2 chiều)

> Cập nhật 2026-06-11: thêm Google Sign-In, write-back (PATCH items), Connections thay OAuthConnection/ServiceConnection. Lịch sử: CHANGELOG.md.
>
> File này là **spec target**. Status implement: Auth + Google Sign-In ✅ · Connections đang transitional (xem mục Connections) · Items GET/filter ✅, write-back ⏳ (SCRUM-37) · Scheduled ⏳ (SCRUM-30/31) · **Jira/Atlassian ⏳ phase Jira (SCRUM-54→60, chưa code)** — các endpoint đánh dấu "phase Jira" là spec target chưa implement.

## Quy ước chung
- Auth: JWT Bearer. Claim: sub, email, role.
- DateTime ISO 8601 UTC. Pagination ?page&limit (default 20, max 100).
- Collection lớn → envelope `{items,total,page,limit}`; nhỏ → array.

## OData query — ⏳ target, chưa implement (chưa có ticket)
Bật **OData query options** cho các endpoint **GET đọc collection trên `IQueryable` EF** (filter/sort/paging đẩy xuống SQL). Đánh dấu `OData ⊕` ở từng endpoint bên dưới.

- **Package:** `Microsoft.AspNetCore.OData` (v8) + `[EnableQuery]` trên action (endpoint routing, KHÔNG cần EDM cho query thuần).
- **Option cho phép:** `$filter` · `$orderby` · `$select` · `$top` · `$skip` · `$count`. **KHÔNG** cho `$expand` (tránh lộ nav + N+1). Cấu hình an toàn: `[EnableQuery(MaxTop = 100, PageSize = 20, AllowedQueryOptions = Select|Filter|OrderBy|Top|Skip|Count)]`.
- **Bảo mật (bắt buộc):** scope theo `CurrentUserId` (và role) **server-side TRƯỚC**, rồi mới trả `IQueryable<TDto>` cho `[EnableQuery]` áp lên. OData **không được** vượt qua lọc theo user.
- **Shape:** action trả `IQueryable<TDto>` đã `AsNoTracking()` + projection sang DTO (KHÔNG trả Entity). `$count` thay `total`, `$top/$skip` thay `page/limit` của envelope — endpoint nào bật OData thì dùng cơ chế OData thay cho query param thủ công cũ.
- **KHÔNG bật OData cho:** endpoint trả **dữ liệu live từ provider** (item detail, Jira metadata helpers), endpoint **mask/decrypt token** (connections), single-resource GET, aggregate (admin/stats), và mọi POST/PATCH/DELETE/write-back.

| Endpoint | OData | $filter (vd) | $orderby (vd) |
|---|---|---|---|
| `GET /api/items` | ⊕ | status, type, isImportant, isArchived, connectionId | occurredAt, dueAt, title |
| `GET /api/admin/users` | ⊕ | role, isActive, email, fullName | createdAt, lastLoginAt |
| `GET /api/scheduled-emails` | ⊕ | status | sendAt |
| `GET /api/folders` | ⊕ | isArchived, name | sortOrder, name |
| `GET /api/tags` | ⊕ | name | name |
| `GET /api/integrations` | ⊕ | isEnabled, provider | displayName |
| `GET /api/connections` | ✗ | — (mask token ở service) | — |
| `GET /api/items/{id}/detail` | ✗ | — (body live provider) | — |
| `GET /api/jira/*` (metadata) | ✗ | — (proxy Jira API) | — |
| `GET /api/admin/stats` | ✗ | — (scalar tổng hợp) | — |

## Error format chuẩn (SCRUM-24 ✅)
Mọi lỗi (4xx/5xx) đi qua `ExceptionMiddleware` → trả body thống nhất:
```json
{ "error": "NotFoundError", "message": "...", "details": [], "traceId": "..." }
```
- `error`: loại lỗi (`ValidationError`, `UnauthorizedError`, `ForbiddenError`, `NotFoundError`, `ConflictError`, `BusinessRuleError`, `CsrfError`, `InternalError`).
- `details[]`: với 400 validation = `"field: message"` mỗi lỗi; với lỗi khác = `[]`.
- `traceId`: đối chiếu log.
- **Mapping exception → status:** ValidationException→400 · UnauthorizedException→401 · ForbiddenException→403 · NotFoundException→404 · ConflictException→409 · BusinessRuleException→422 · CsrfException→400 · còn lại→500.
- **500 không lộ stack trace / message nội bộ ở production** (chỉ message generic + traceId; chi tiết ghi log). Ở Development thì kèm vào `details[]` để debug.
- Controller KHÔNG tự format lỗi — chỉ throw custom exception (`WorkspaceHub.Application.Common`) hoặc gọi `ValidateAndThrowAsync`.

## Status code (bổ sung cho write-back)
Như cũ, lưu ý: **403** thiếu scope ghi (connection cũ readonly) · **409** conflict ETag · **502** provider lỗi khi ghi/đọc live.

> Connection migrate từ mô hình A (SCRUM-34 copy data) vẫn giữ token scope readonly cũ → user phải **reconnect** để có scope ghi (gmail.modify+send / calendar / drive.file) trước khi dùng write-back.

---

## Auth (email/password) — không đổi
- `POST /api/auth/register` · `POST /api/auth/login` · `GET /api/auth/me` · `POST /api/auth/logout`

## Auth Google Sign-In ⭐ mới
- `POST /api/auth/google/start` — AllowAnonymous → {authorizationUrl, state}. Scope chỉ openid/email/profile.
- `POST /api/auth/google/callback` — {code, state} → verify id_token, tìm/tạo/link user, phát JWT. (400 CSRF, 401 token invalid / user khoá)
> KHÔNG tạo Connection. Chỉ tạo/tìm User. Auto-link nếu email trùng.

## Admin — ✅ Implemented (SCRUM-49 2026-06-19)
`GET /api/admin/users` — danh sách user phân trang + search, Admin only. **OData ⊕** (target — $filter/$orderby/$top/$skip/$count; Admin-only vẫn enforce trước).
- Query: `?search=` (Email|FullName, case-insensitive, max 200 chars), `?page=1`, `?limit=20` (max 100).
- Response 200: `{ items: AdminUserDto[], total, page, limit }`. AdminUserDto gồm: id, email, fullName, role, isActive, lastLoginAt, createdAt, connectionCount (tất cả connection), itemCount.
- Status: 200 · 400 (validation) · 401 · 403.

`GET /api/admin/stats` — thống kê hệ thống, Admin only.
- Response 200: `{ totalUsers, activeUsers, lockedUsers, totalConnections, connectionsByStatus: {Active,Error,Disconnected}, totalItems, syncErrorsLast24h }`.
- `syncErrorsLast24h` = count Connections với Status=Error VÀ LastSyncedAt!=null VÀ LastSyncedAt>=UtcNow-24h.
- `activeUsers + lockedUsers == totalUsers` (invariant).
- Status: 200 · 401 · 403.

`GET /api/admin/users/{id}`, `PATCH /users/{id}/lock`, `DELETE /api/admin/connections/{id}` — spec target, chưa implement.

## Integrations
- `GET /api/integrations` — catalog cho user. **OData ⊕** (target — $filter isEnabled/provider, $orderby).
- `PATCH /api/admin/integrations/{key}/enable` — Admin bật/tắt integration (`IsEnabled`); tắt → user không initiate connection được (422). ✅ SCRUM-48.
  - Request: `{ "isEnabled": true | false }`
  - Response 200: `{ "id", "key", "displayName", "isEnabled" }`
  - 404 key không tồn tại · 403 không phải Admin
- ~~`PUT /api/connections/{key}/credentials`~~ — **sẽ xoá ở SCRUM-47**: admin không quản lý credentials nữa, ClientId/Secret đọc từ config/env (CHANGELOG 2026-06-12).

## Connections ⭐ (thay OAuth Connections + Service Connections)
Mô hình B: mỗi service authorize riêng, tạo 1 Connection.

- `POST /api/connections/oauth/start` — `{integrationKey, serviceType, redirectUri}` → `{authorizationUrl, state}`. Scope = full của service đó (dev quyết). Mỗi lần chỉ connect 1 service. (404 integration không tồn tại, 422 serviceType không hợp lệ / provider không hỗ trợ serviceType / integration disabled)
- `POST /api/connections/oauth/callback` — `{code, state}` → 201 tạo **1** Connection row. Response: `{integrationKey, providerAccountId, connections: [{id, serviceType, status}]}`. (400 CSRF, 400 provider từ chối/scope thiếu, 409 trùng service+account)
- `GET /api/connections` — array (token mask). Mỗi row = 1 service. ✅ SCRUM-14.
- `POST /api/connections/{id}/refresh` — refresh token. (422 invalid→Error) ✅ SCRUM-14.
- `DELETE /api/connections/{id}` — 204, xoá đúng service đó. Items giữ lại (ConnectionId=NULL). KHÔNG ảnh hưởng login hay service khác. ✅ SCRUM-14.
- `POST /api/connections/{id}/sync` — 202 trigger thủ công (fallback). Dispatcher route theo ServiceType: Gmail/GCal/Drive (Google) + **Jira → `JiraSyncService` ✅ SCRUM-55** (search JQL → Item Type=Ticket, dedupe, cursor `JqlUpdated`).

> Bỏ /api/services/* (mô hình A). Toggle = connect/disconnect cả Connection.

### Jira / Atlassian — ✅ OAuth (SCRUM-54) + sync đọc (SCRUM-55); write CRUD ⏳ (SCRUM-56→60)
Dùng chung 2 endpoint `oauth/start` + `oauth/callback`, mô hình B. Sync issue → Item(Type=Ticket) đi qua `POST /api/connections/{id}/sync` (không có endpoint riêng):
- `POST /api/connections/oauth/start` — `{integrationKey: "atlassian", serviceType: "Jira", redirectUri}` → `{authorizationUrl, state}`. Scope read-write Jira (`read:jira-work write:jira-work read:jira-user offline_access`).
- `POST /api/connections/oauth/callback` — `{code, state}` → đổi token, gọi `/oauth/token/accessible-resources` lấy **cloudId**, lưu `ProviderAccountId = cloudId`, tạo 1 Connection ServiceType=Jira. (400 CSRF/scope thiếu, 409 trùng cloudId)

## Folders / Folder Shares / Tags / Important Contacts / Notifications
Không đổi. Xem bản trước. List endpoint `GET /api/folders`, `GET /api/tags` → **OData ⊕** (target — $filter/$orderby trên IQueryable, scope theo CurrentUserId trước).

## Items (thêm write-back ⭐)
- `GET /api/items?folderId&status&type&isImportant&search&page&limit` — envelope. Trả kèm ETag. **OData ⊕** (target — $filter/$orderby/$select/$top/$skip/$count thay query param thủ công; vẫn scope theo CurrentUserId trước).
- `GET /api/items/{id}/detail` — metadata + body live. (403 Viewer, 502 provider)
- `POST /api/items/note` — tạo Note.
- `POST /api/items/event` ⭐ — tạo Event mới → đẩy lên Calendar.
- `POST /api/items/ticket` ⏳ **phase Jira (SCRUM-56, chưa implement)** — tạo issue mới → đẩy lên Jira.
  - Body: `{connectionId, projectKey, issueType, summary, description?, assignee?, priority?}` (connection phải ServiceType=Jira). `description` nhận markdown, service convert sang **ADF** trước khi gửi.
  - → 201 tạo Item(Type=Ticket) + issue trên Jira. (404 connection, 422 connection không phải Jira / projectKey-issueType không hợp lệ, 502 provider lỗi)
- `PATCH /api/items/{id}` ⭐ — write-back, body theo Type:
  - Email: `{isUnread?, isStarred?, labels?[], isTrashed?}` (KHÔNG sửa nội dung)
  - Event: `{title?, start?, end?, location?, attendees?[]}`
  - File: `{name?, isTrashed?}`
  - Ticket ⏳ **phase Jira (SCRUM-57, chưa implement):** `{summary?, description?, assignee?, priority?, statusTransition?, comment?}` — **nội dung sửa được** (khác Email immutable). `description` markdown ↔ ADF. `statusTransition` = id/tên transition (Jira đổi status qua transition, không set trực tiếp). `comment` = thêm comment (không sửa field). Đi qua cùng `IWriteBackGuard` của SCRUM-38; Jira không có HTTP ETag → version-token = `fields.updated` lưu trong `Items.ETag`.
  - → đẩy lên provider. (403 thiếu scope, 409 conflict ETag/version, 502 provider lỗi)
- `PATCH /api/items/{id}/status` — Kanban (local only).
- `PATCH /api/items/{id}/archive` — local only.
- `DELETE /api/items/{id}` ⭐ — trash/xoá trên provider + local. Type=Ticket ⏳ **phase Jira (SCRUM-58, chưa implement):** xoá issue trên Jira (`DELETE /rest/api/3/issue/{key}`) + local. (403 thiếu quyền, 502 provider lỗi)

### Jira metadata helpers — ⏳ phase Jira (SCRUM-59, chưa implement)
Phục vụ FE chọn giá trị khi tạo/sửa ticket (`?connectionId=` bắt buộc, ServiceType=Jira):
- `GET /api/jira/projects?connectionId=` — list project (`{key, name, id}`).
- `GET /api/jira/issue-types?connectionId=&projectKey=` — issue type hợp lệ của project.
- `GET /api/jira/transitions?connectionId=&itemId=` — transition khả dụng cho issue hiện tại (đổi status).
- `GET /api/jira/assignable-users?connectionId=&projectKey=&query=` — user gán được.
- `GET /api/jira/priorities?connectionId=` — danh sách priority.
- (404 connection, 422 connection không phải Jira, 502 provider lỗi)

## Item-Folder — không đổi
`POST/DELETE /api/folders/{id}/items`, `PATCH .../reorder`.

## Scheduled Emails (đổi ConnectionId ⭐)
- `POST /api/scheduled-emails` — {connectionId, to[], cc[], bcc[], subject, bodyHtml, sendAt} → 201. (404 connection, 422 connection không phải Gmail)
- `GET /api/scheduled-emails?status&page&limit` — envelope. **OData ⊕** (target — $filter status, $orderby sendAt, $top/$skip/$count).
- `PATCH /api/scheduled-emails/{id}/cancel` — (422 đã gửi).
- `POST /api/internal/process-scheduled` — X-Cron-Secret. Lấy token từ Connections (Gmail).
