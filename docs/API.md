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
| `GET /api/ScheduledEmails` | ⊕ | Status | SendAt, CreatedAt |
| `GET /api/Notifications` | ⊕ | IsRead | CreatedAt |
| `GET /api/EmailContactSuggestions` | ⊕ | Email, DisplayName, Source (contains) | DisplayName, Email |
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

## Auth (email/password)
- `POST /api/auth/register` · `POST /api/auth/login` · `GET /api/auth/me` · `POST /api/auth/logout`
- **SCRUM-62 — cookie auth:** login/register/google **KHÔNG trả `accessToken` trong body** nữa; body = `AuthResultDto { expiresIn, user }`. Access token JWT set vào HttpOnly cookie `wh_access`; kèm cookie `wh_csrf` (đọc được) cho double-submit. Request mutating (POST/PUT/PATCH/DELETE) **bằng cookie** phải gửi header `X-CSRF-Token` = `wh_csrf` (thiếu → 403 `CsrfError`). Request dùng `Authorization: Bearer` (Swagger/Postman) bỏ qua CSRF.
- `POST /api/auth/logout` — AllowAnonymous; revoke refresh token (Redis) + xoá cookie `wh_access`/`wh_csrf`/`wh_refresh` → 204.

## Auth OTP đăng ký ⭐ SCRUM-64
- `POST /api/auth/register` — body `{ email, password, fullName, phone }` (phone E.164, vd `+84901234567`). Tạo user `PhoneVerified=false` + gửi OTP SMS. **KHÔNG đăng nhập ngay** — trả `201 RegisterResult { email, requiresPhoneVerification, resendCooldownSeconds }`. 409 email trùng, 400 validation (kể cả phone sai format).
- `POST /api/auth/send-otp` — body `{ email }` → gửi lại OTP. Trả `{ resendCooldownSeconds }`. 404 user không tồn tại, 422 đã verify / không có phone / đang cooldown.
- `POST /api/auth/verify-otp` — body `{ email, code }` → verify; đúng → `PhoneVerified=true` + **set cookie auth (đăng nhập)**, trả `AuthResultDto`. 422 mã sai / hết hạn / quá số lần.
- **Login chặn chưa verify:** đăng nhập khi `PhoneVerified=false` → **403** với `message = "PHONE_NOT_VERIFIED"` (FE bắt mã này → gửi OTP + sang màn verify). Google Sign-In KHÔNG bị chặn (không có phone, `PhoneVerified` mặc định true).
- OTP: 6 số, lưu **hash** ở Redis (`otp:{userId}`), TTL 5', cooldown gửi lại 60s, tối đa 5 lần sai. Provider Twilio (`Sms:Twilio:*`); thiếu config → dev `LogSmsSender` ghi OTP ra log.

## Auth refresh token ⭐ SCRUM-63
- `POST /api/auth/refresh` — AllowAnonymous; đọc cookie `wh_refresh` (HttpOnly, Path=`/api/auth/refresh`) → verify + **rotate** (cấp access token mới + refresh token mới, revoke jti cũ) → set lại cookie `wh_access`+`wh_refresh`, body `AuthResultDto`. Token thiếu/hết hạn/đã revoke → **401**. Reuse refresh token đã xoay (token theft) → revoke cả family → 401.
- Access TTL ngắn (`Jwt:ExpiresIn`, mặc định 900s); refresh TTL dài (`Jwt:RefreshExpiresIn`, mặc định 7 ngày) lưu Redis (`refresh:{jti}`, `refreshfam:{fam}`).
- FE: interceptor 401 tự gọi `/auth/refresh` 1 lần (single-flight) rồi retry request gốc; fail → về /login.

## Auth Google Sign-In ⭐ mới
- `POST /api/auth/google/start` — AllowAnonymous → {authorizationUrl, state}. Scope chỉ openid/email/profile.
- `POST /api/auth/google/callback` — {code, state} → verify id_token, tìm/tạo/link user, set cookie auth (access + refresh). (400 CSRF, 401 token invalid / user khoá)
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

### Jira / Atlassian — ✅ CRUD đầy đủ: OAuth (54) + sync đọc (55) + tạo (56) + update (57) + xoá (58); còn lại ⏳ (metadata 59 / contacts 60)
Dùng chung 2 endpoint `oauth/start` + `oauth/callback`, mô hình B. Sync issue → Item(Type=Ticket) đi qua `POST /api/connections/{id}/sync` (không có endpoint riêng):
- `POST /api/connections/oauth/start` — `{integrationKey: "atlassian", serviceType: "Jira", redirectUri}` → `{authorizationUrl, state}`. Scope read-write Jira (`read:jira-work write:jira-work read:jira-user offline_access`).
- `POST /api/connections/oauth/callback` — `{code, state}` → đổi token, gọi `/oauth/token/accessible-resources` lấy **cloudId**, lưu `ProviderAccountId = cloudId`, tạo 1 Connection ServiceType=Jira. (400 CSRF/scope thiếu, 409 trùng cloudId)

## Folders / Folder Shares / Tags / Important Contacts / Notifications
Không đổi (trừ Tags — xem dưới). Xem bản trước. List endpoint `GET /api/folders`, `GET /api/tags` → **OData ⊕** (target — $filter/$orderby trên IQueryable, scope theo CurrentUserId trước).

### Tags — ✅ SCRUM-70 (BE, CRUD + assign)
Label private của user (không share), gắn cho Item qua junction `TagAssignment` (m-n). Tên tag **không** unique toàn hệ thống nhưng **unique trong 1 user**. Mọi endpoint owner-scoped theo `CurrentUserId`.
- `GET /api/tags` — list tag của user, kèm `itemCount` (số item đang gắn). OData ⊕.
- `POST /api/tags` — `{name, color}` → 201. `color` = hex (`#RGB`/`#RRGGBB`). (400 validation, 409 trùng tên trong user)
- `PUT /api/tags/{id}` — `{name, color}` → 200. (400 validation, 404 không phải của mình, 409 trùng tên)
- `DELETE /api/tags/{id}` — 204. Hard delete; DB cascade gỡ mọi `TagAssignment`, **Item giữ nguyên**. (404 không phải của mình)
- `POST /api/tags/{id}/items` — `{itemId}` → 201 `{tagId, itemId, assignedAt}`. Gắn tag vào item. (404 tag/item không thuộc user, 409 đã gắn)
- `DELETE /api/tags/{id}/items/{itemId}` — 204. Gỡ tag khỏi item. (404 tag không thuộc user / chưa gắn)

### Important Contacts — ✅ SCRUM-60 (CRUD)
Đánh dấu Email/JiraAccount là liên hệ quan trọng (Item sync về từ contact này tự set IsImportant).
- `GET /api/importantcontacts?type=` — list của user (lọc Email/JiraAccount nếu có type).
- `POST /api/importantcontacts` — `{type, identifier, label}` → 201. Email: identifier = email hợp lệ; JiraAccount: identifier = accountId. (409 trùng (UserId,Type,Identifier), 400 validation)
- `DELETE /api/importantcontacts/{id}` — 204, owner-only (404 nếu không phải của mình).
> Notification type cho Jira (jira_assigned…): chưa làm — optional, chờ có nguồn sync-event Jira.

## Items (thêm write-back ⭐)
- `GET /api/items?folderId&statuses&types&isImportant&tagId&search&page&limit` — envelope. Trả kèm ETag. `statuses`/`types` **đa chọn** (query lặp key, vd `?statuses=Inbox&statuses=Doing&types=Email`) — không truyền = không lọc field đó (FE: chip toggle kiểu tag, bấm lại để bỏ). `tagId` ✅ **SCRUM-71** = lọc item gắn tag đó (join `TagAssignment`). Mỗi item trong response trả kèm `tags: [{id, name, color}]` (tag đang gắn). **OData ⊕** (target — $filter/$orderby/$select/$top/$skip/$count thay query param thủ công; vẫn scope theo CurrentUserId trước).
- `GET /api/items/{id}/detail` — metadata + body live. (403 Viewer, 502 provider)
- `POST /api/items/note` — tạo Note.
- `POST /api/items/event` ⭐ — tạo Event mới → đẩy lên Calendar.
- `POST /api/items/ticket` ✅ **SCRUM-56** — tạo issue mới → đẩy lên Jira.
  - Body: `{connectionId, projectKey, issueType, summary, description?, assignee?(accountId), priority?, labels?[]}` (connection phải ServiceType=Jira + Active). `description` nhận plain text, service convert sang **ADF** (`AdfConverter.FromPlainText`) trước khi gửi. `labels` không chứa khoảng trắng.
  - Tạo trên Jira (`POST /rest/api/3/issue`) → fetch lại issue → tạo Item(Type=Ticket) local (kèm issueKey + metadata + ETag=`fields.updated`).
  - → 201 (CreatedAtAction → GetItemById). (400 validation, 403 connection của user khác / thiếu scope write, 404 connection, 422 connection không phải Jira / Jira reject field-project-issueType, 502 provider lỗi)
- `PATCH /api/items/{id}` ⭐ — write-back, body theo Type:
  - Email: `{isUnread?, isStarred?, labels?[], isTrashed?}` (KHÔNG sửa nội dung)
  - Event: `{title?, start?, end?, location?, attendees?[]}`
  - File: `{name?, isTrashed?}`
  - Ticket ✅ **SCRUM-57:** `{summary?, description?, assignee?(accountId), priority?, statusTransition?, labels?[], comment?}` — **nội dung sửa được** (khác Email immutable). `description` plain text → ADF (`AdfConverter.FromPlainText`). `summary/description/priority/labels` qua `PUT /issue`; `assignee` qua `PUT /issue/{key}/assignee`; `statusTransition` = id/tên transition (Jira đổi status qua transition, không set field trực tiếp — không khả dụng theo workflow → 422); `comment` = thêm comment (`POST /comment`, không sửa field). Đi qua cùng `IWriteBackGuard` của SCRUM-38; Jira không có HTTP ETag → version-token = `fields.updated` lưu trong `Items.ETag`. Reject field Google trên ticket → 422.
  - → đẩy lên provider, fetch lại + cập nhật ETag/metadata local. (400 validation, 403 thiếu scope, 409 conflict version, 422 transition/field không hợp lệ, 502 provider lỗi)
- `PATCH /api/items/{id}/status` — Kanban (local only).
- `PATCH /api/items/{id}/archive` — local only.
- `DELETE /api/items/{id}` ⭐ — trash/xoá trên provider + local. Type=Ticket ✅ **SCRUM-58:** xoá issue trên Jira (`DELETE /rest/api/3/issue/{key}?deleteSubtasks=true`) **rồi mới** xoá Item local — Jira lỗi (403 thiếu quyền / 502) thì Item local giữ nguyên (không xoá lệch). Owner check (không phải owner → 404). (403 thiếu quyền, 404 không tồn tại/không phải owner, 502 provider lỗi)

### Jira metadata helpers — ✅ SCRUM-59
Phục vụ FE chọn giá trị khi tạo/sửa ticket (`?connectionId=` bắt buộc, ServiceType=Jira + Active). Trả dữ liệu live (KHÔNG OData). Cache nhẹ TTL 5' cho project/issue-type/priority; transitions + assignable-users không cache.
- `GET /api/jira/projects?connectionId=` — list project (`{id, key, name}`).
- `GET /api/jira/issue-types?connectionId=&projectKey=` — issue type hợp lệ của project (`{id, name, subtask}`).
- `GET /api/jira/transitions?connectionId=&itemId=` — transition khả dụng cho issue hiện tại (`{id, name, toStatusName}`), đổi status.
- `GET /api/jira/assignable-users?connectionId=&projectKey=&query=` — user gán được (`{accountId, displayName, email, active}`).
- `GET /api/jira/priorities?connectionId=` — danh sách priority (`{id, name}`).
- (404 connection (cả của user khác), 422 connection không phải Jira / không active / projectKey thiếu, 502 provider lỗi)

## Item-Folder — không đổi
`POST/DELETE /api/folders/{id}/items`, `PATCH .../reorder`.

## Emails — gửi trực tiếp ⭐
- `POST /api/emails/send` — [Authorize]. Body `{ connectionId, to[], cc[], bcc[], subject, bodyHtml }`. Gửi **ngay** (đồng bộ) qua Gmail. Validate connection thuộc user + ServiceType=Gmail + Active. Trả `200 { messageId, sentAt }`. (400 validation, 404 connection, 422 connection không phải Gmail / không Active, 502 provider lỗi). Gmail write-back "gửi mới".
- `GET /api/emails/signature?connectionId=` — [Authorize]. Lấy chữ ký HTML đã đặt trong Gmail của connection (qua `users.settings.sendAs`, ưu tiên primary). Trả `200 { signature }` (rỗng nếu chưa đặt HOẶC connection thiếu scope `gmail.settings.basic` — không lỗi). Lưu ý: Gmail API **không** tự chèn chữ ký khi gửi, FE tự append. Scope `gmail.settings.basic` là **optional** (request thêm khi connect Gmail, không bắt buộc); connection tạo trước thay đổi này phải **reconnect** mới đọc được chữ ký.
- `GET /api/EmailContactSuggestions?connectionId=` — [Authorize]. Gợi ý contact từ cache DB `GoogleContacts` khi soạn mail (To/Cc/Bcc). **OData ⊕** convention route (`EmailContactSuggestionsController`): `$filter` (vd `contains(Email,'al') or contains(DisplayName,'al')`), `$orderby`, `$top` (max 20), `$skip`, `$count`, `$select`. Query OData dùng **PascalCase** tên property CLR (`Email`, `DisplayName`, `Source`); JSON response vẫn camelCase. Bắt buộc query `connectionId` (scope server-side theo connection Gmail của user). Trả `{ value: [{ email, displayName, source }], @odata.count? }` — **cả** `Source=Contact` và `Source=OtherContact`. 404 connection, 422 không phải Gmail / không Active. Dữ liệu có sau sync Gmail (cron ~60s / Đồng bộ thủ công); scope optional `contacts.readonly` + `contacts.other.readonly` — thiếu → `value` rỗng, vẫn nhập tay.

## Scheduled Emails (đổi ConnectionId ⭐)
- `POST /api/scheduled-emails` — {connectionId, to[], cc[], bcc[], subject, bodyHtml, sendAt} → 201. (404 connection, 422 connection không phải Gmail)
- `GET /api/scheduled-emails/{id}` — chi tiết một email hẹn giờ.
- `GET /api/ScheduledEmails` — **OData ⊕** convention route (`ScheduledEmailsController`): `$filter` (vd `Status eq 'Pending'`), `$orderby` (vd `SendAt`, `CreatedAt`), `$top/$skip/$count`. Query OData **PascalCase** tên property CLR; JSON response camelCase. Response `{ value, @odata.count? }`.
- `PATCH /api/scheduled-emails/{id}/cancel` — (422 đã gửi).
- `POST /api/internal/process-scheduled` — header `X-Cron-Secret` (so khớp `Cron:Secret`; thiếu/sai/secret chưa cấu hình → 401). Không JWT. Gửi mọi email Pending có `sendAt <= now` qua Gmail (token tự refresh từ Connection). Mỗi email lỗi → `Failed` (RetryCount++, LastError) chứ không chặn cả batch. Trả `200 { total, sent, failed }`. ✅ SCRUM-31.
  - **Cron ngoài** gọi endpoint này định kỳ (khuyến nghị 5 phút/lần) — thiết kế mặc định.
  - **Auto-cron nội bộ (tuỳ chọn):** `Cron:AutoRun=true` → BE tự chạy `ScheduledEmailProcessorService` (BackgroundService) quét/gửi mỗi `Cron:IntervalSeconds` (mặc định 300s), gọi thẳng service không qua HTTP/secret. Prod mặc định `false` (theo CLAUDE.md "BE không tự hẹn giờ"); Development mặc định `true` (interval 60s) để test.
- `POST /api/internal/process-sync` — header `X-Cron-Secret` (cùng `Cron:Secret` với `process-scheduled`). Không JWT. Quét mọi Connection Active + Integration enabled → debounce → refresh token nếu cần → `ConnectionSyncDispatcher.SyncAsync` (Gmail/GCal/Drive/Jira). Lỗi **auth bền** (401/403, refresh token fail) → `Status=Error`; lỗi tạm thời (network/5xx) giữ `Active` để cron lần sau retry — mỗi connection lỗi không chặn batch. Trả `200 ProcessSyncResult { totalConnections, successCount, skippedCount, errorCount, details? }`. ✅ SCRUM-72.
  - **Prod (mặc định):** `Cron:SyncAutoRun=true` trong `docker-compose.prod.yml` → `ConnectionSyncProcessorService` mỗi `Cron:SyncIntervalSeconds` (compose: 60s). Cùng pattern cron email — không cần cron-job.org. **Không** bật đồng thời với cron HTTP.
  - **HTTP cron (tuỳ chọn):** `POST /api/internal/process-sync` + `X-Cron-Secret` khi `SyncAutoRun=false` (test local hoặc thay BackgroundService).

## Notifications
- `GET /api/Notifications` — **OData ⊕** convention route (`NotificationsController`): `$filter` (vd `IsRead eq false`), `$orderby` (vd `CreatedAt desc`), `$top/$skip/$count`. Query OData **PascalCase** tên property CLR; JSON response camelCase. Response `{ "@odata.count"?, value: [...] }`. Badge unread: `GET /api/Notifications?$filter=IsRead eq false&$count=true&$top=0`.
- `PATCH /api/notifications/{id}/read` — đánh dấu đã đọc → 204.
- `POST /api/notifications/read-all` — đánh dấu tất cả đã đọc → 204.
- `POST /api/notifications/dev/seed` — (DEBUG/dev) tạo notification test → 200.
- SignalR hub `/hubs/notifications` — event `ReceiveNotification` (toast + invalidate cache FE).
- **Copy:** `Title` = i18n key (`notifications.newEmailFrom`, …); `Body` = JSON `{ from?, itemTitle, preview }`. FE dịch title theo lang hệ thống; `preview` hiển thị làm subtitle (snippet email / tên item).
