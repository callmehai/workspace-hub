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

## Users — Hồ sơ cá nhân ⭐ SCRUM-75 (đổi tên, đổi mật khẩu, avatar Cloudflare R2)
- `PATCH /api/users/me` — body `{ fullName }` → đổi họ tên. Trả `UserDto`. 400 validation (rỗng/quá 200 ký tự).
- `POST /api/users/me/change-password` — body `{ currentPassword, newPassword }` → 204. Chỉ áp dụng user có mật khẩu (`AuthProvider` Local/Both) — Google-only → 422 `"Tài khoản đăng nhập qua Google, không có mật khẩu để đổi."`. Sai `currentPassword` → 422.
- `POST /api/users/me/avatar` — multipart/form-data, field `file` (JPEG/PNG/WebP, tối đa 5MB). Upload lên R2 (key `avatars/{userId}.{ext}`, cùng định dạng thì ghi đè; đổi định dạng thì **xoá object cũ** để không rác), lưu `Users.AvatarUrl`. Trả `UserDto` (đã kèm `avatarUrl`). 422 sai định dạng/quá size.
- `DELETE /api/users/me/avatar` — xoá object trên R2 + set `Users.AvatarUrl = null`. Trả `UserDto`.
- `UserDto` nay có thêm `avatarUrl` + `authProvider` (`Local`/`Google`/`Both`) — FE dùng `authProvider` để ẩn form đổi mật khẩu với tài khoản Google-only.
- Config R2 đọc từ section `R2` (`AccountId`, `BucketName`, `PublicUrl`, `AccessKeyId`, `SecretAccessKey`) — secret qua user-secrets (dev) / env `R2__*` (prod), không commit. Xem `docs/SETUP.md`.

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

`POST /api/admin/users/{id}/toggle-active` — toggle lock/unlock user, Admin only.
- Response 200: `{ id, email, fullName, role, isActive, lastLoginAt, createdAt, connectionCount, itemCount }` (updated AdminUserDto).
- **422** không khoá được: tự khoá chính mình · khoá user role Admin đang active (mở khoá Admin vẫn OK).
- Status: 200 · 401 · 403 · 404 (user not found).

`GET /api/admin/users/{id}`, `DELETE /api/admin/connections/{id}` — spec target, chưa implement.

## Integrations
- `GET /api/integrations` — catalog cho user đăng nhập (`id`, `key`, `displayName`, `isEnabled`). ✅ SCRUM-61.
- `GET /api/admin/integrations` — Admin list catalog (`id`, `key`, `displayName`, `isEnabled`). ✅ SCRUM-61.
- `PATCH /api/admin/integrations/{key}/enable` — Admin bật/tắt integration (`IsEnabled`); tắt → user không initiate connection được (**422** `message = "integrations.connectDisabled"` — FE dịch qua i18n). ✅ SCRUM-40.
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
- `GET /api/items?folderId&statuses&types&isImportant&tagIds&projectKey&assignee&gmailLabel&connectionId&occurredFrom&occurredTo&search&page&limit` — envelope. Trả kèm ETag. `statuses`/`types`/`tagIds` **đa chọn** (query lặp key, vd `?statuses=Inbox&statuses=Doing&types=Email`) — không truyền = không lọc field đó (FE: chip toggle kiểu tag, bấm lại để bỏ). `tagIds` ✅ **SCRUM-71** = lọc item gắn **bất kỳ tag nào** trong danh sách (OR; join `TagAssignment`; lặp key `?tagIds={g1}&tagIds={g2}`). `projectKey` = lọc theo dự án (Jira Ticket). `assignee` = lọc Ticket theo **người phụ trách** (accountId; `"unassigned"` = chưa gán) — match `metadata.assigneeAccountId`. `connectionId` = lọc item thuộc **một connection** (Drive modal chọn folder cha, v.v.). `gmailLabel` = lọc email theo **Gmail label** (`INBOX`/`SENT`/`DRAFT`/`STARRED`/`IMPORTANT`/`CATEGORY_PROMOTIONS`/`CATEGORY_SOCIAL`/`CATEGORY_UPDATES`/...) — match token trong `metadata.labels`; chỉ Email có labels nên loại khác tự loại. **SPAM/TRASH chưa lọc được** (sync bỏ qua, `includeSpamTrash=false`); Purchases/Bills của Gmail là view ML nội bộ, **không** expose qua API. Mỗi item trong response trả kèm `tags: [{id, name, color}]` (tag đang gắn). **OData ⊕** (target — $filter/$orderby/$select/$top/$skip/$count thay query param thủ công; vẫn scope theo CurrentUserId trước).
- `GET /api/items/{id}/detail` — metadata + body live. (403 Viewer, 502 provider)
- `GET /api/items/assignees` — danh sách người phụ trách (`{accountId, displayName}[]`) suy từ Ticket Jira đã sync của user (cho filter tab Jira). Gồm `{accountId:"unassigned"}` nếu có ticket chưa gán.
- `POST /api/items/note` — tạo Note.
- `POST /api/items/event` ⭐ — tạo Event mới → đẩy lên Calendar. Body: `{connectionId, title, start, end, allDay?, location?, attendees?[], description?, driveItemIds?[], reminders?[]}` — `end` **bắt buộc** (all-day: gửi ngày kế tiếp). Chỉ Google Calendar Event (không có khái niệm "task"). `reminders[]`: `{id?, reminderType:"GooglePopup"|"GoogleEmail"|"InApp", offsetValue, offsetUnit:"Minutes|Hours|Days|Weeks", timeOfDay?}` — mỗi phần tử = 1 row DB (`EventReminders.ReminderType`). `GooglePopup/GoogleEmail` map sang Google Calendar overrides `popup/email`; `InApp` chỉ lưu để Workspace Hub bắn notification nội bộ.
- `POST /api/items/ticket` ✅ **SCRUM-56** — tạo issue mới → đẩy lên Jira.
  - Body: `{connectionId, projectKey, issueType, summary, description?, assignee?(accountId), priority?, labels?[]}` (connection phải ServiceType=Jira + Active). `description` nhận plain text, service convert sang **ADF** (`AdfConverter.FromPlainText`) trước khi gửi. `labels` không chứa khoảng trắng.
  - Tạo trên Jira (`POST /rest/api/3/issue`) → fetch lại issue → tạo Item(Type=Ticket) local (kèm issueKey + metadata + ETag=`fields.updated`).
  - → 201 (CreatedAtAction → GetItemById). (400 validation, 403 connection của user khác / thiếu scope write, 404 connection, 422 connection không phải Jira / Jira reject field-project-issueType, 502 provider lỗi)
- `PATCH /api/items/{id}` ⭐ — write-back, body theo Type:
  - Email: `{isUnread?, isStarred?, labels?[], isTrashed?}` (KHÔNG sửa nội dung)
  - Event: `{title?, start?, end?, allDay?, location?, attendees?[], description?, driveItemIds?[], reminders?[]}` — sau patch cập nhật `occurredAt`/`dueAt` + `metadata.start`/`end`/`allDay`. `allDay=true` → Google `EventDateTime.Date`. Đổi all-day ↔ timed dùng GET+UPDATE (không Patch) để tránh lỗi `Invalid start time`. Reminder dùng `reminderType` như `POST /api/items/event`.
  - File: `{name?, isTrashed?}`
  - Ticket ✅ **SCRUM-57:** `{summary?, description?, assignee?(accountId), priority?, statusTransition?, labels?[], comment?, issueType?}` — **nội dung sửa được** (khác Email immutable). `issueType` = đổi loại issue (Task/Bug/Story…) qua `PUT /issue` field `issuetype.name` (có thể 422 nếu Jira workflow/screen không cho đổi giữa 2 loại). `description` plain text → ADF (`AdfConverter.FromPlainText`). `summary/description/priority/labels` qua `PUT /issue`; `assignee` qua `PUT /issue/{key}/assignee`; `statusTransition` = id/tên transition (Jira đổi status qua transition, không set field trực tiếp — không khả dụng theo workflow → 422); `comment` = thêm comment (`POST /comment`, không sửa field). Đi qua cùng `IWriteBackGuard` của SCRUM-38; Jira không có HTTP ETag → version-token = `fields.updated` lưu trong `Items.ETag`. Reject field Google trên ticket → 422.
  - → đẩy lên provider, fetch lại + cập nhật ETag/metadata local. (400 validation, 403 thiếu scope, 409 conflict version, 422 transition/field không hợp lệ, 502 provider lỗi)
- `PATCH /api/items/{id}/status` — Kanban (local only).
- `PATCH /api/items/{id}/archive` — local only.
- `DELETE /api/items/{id}` ⭐ — trash/xoá trên provider + local. Type=Ticket ✅ **SCRUM-58:** xoá issue trên Jira (`DELETE /rest/api/3/issue/{key}?deleteSubtasks=true`) **rồi mới** xoá Item local — Jira lỗi (403 thiếu quyền / 502) thì Item local giữ nguyên (không xoá lệch). Owner check (không phải owner → 404). (403 thiếu quyền, 404 không tồn tại/không phải owner, 502 provider lỗi)
  - **Type=Email gộp thread:** mỗi thư trong hội thoại là 1 Item row riêng (sync tách theo message). Xoá 1 email = **xoá CẢ thread** — `Users.Threads.Trash(threadId)` (trash cả thread trên Gmail) **rồi** xoá mọi Item row cùng `ThreadId` của user (`DeleteThreadAsync`). Nếu chỉ trash/remove thư đại diện thì thread hiện lại ở list với thư mới-nhì. Gmail lỗi → giữ nguyên row local. Email không có `ThreadId` (item cũ chưa backfill) → fallback trash 1 message + xoá 1 row. Là **Trash** (khôi phục được trong Gmail), KHÔNG hard-delete (`Messages.Delete`).

### Jira metadata helpers — ✅ SCRUM-59
Phục vụ FE chọn giá trị khi tạo/sửa ticket (`?connectionId=` bắt buộc, ServiceType=Jira + Active). Trả dữ liệu live (KHÔNG OData). Cache nhẹ TTL 5' cho project/issue-type/priority; transitions + assignable-users không cache.
- `GET /api/jira/projects?connectionId=` — list project (`{id, key, name}`).
- `GET /api/jira/issue-types?connectionId=&projectKey=` — issue type hợp lệ của project (`{id, name, subtask}`).
- `GET /api/jira/transitions?connectionId=&itemId=` — transition khả dụng cho issue hiện tại (`{id, name, toStatusName}`), đổi status.
- `GET /api/jira/assignable-users?connectionId=&projectKey=&query=` — user gán được (`{accountId, displayName, email, active, avatarUrl}`). `email` thường **null** (Atlassian ẩn theo quyền riêng tư) → FE hiển thị avatar + tên, KHÔNG email. `avatarUrl` = ảnh 48x48 (null nếu không có).
- `GET /api/jira/priorities?connectionId=` — danh sách priority (`{id, name}`).
- `GET /api/jira/site?connectionId=` — tên + URL Jira site (`{name, url}`) tra từ accessible-resources theo cloudId; cache 5 phút. **204** nếu không lấy được. FE dùng `name` làm nhãn dropdown "Jira account" thay cho cloudId (GUID).
- (404 connection (cả của user khác), 422 connection không phải Jira / không active / projectKey thiếu, 502 provider lỗi)

### Jira ticket — comment + attachment (2 chiều) ✅
Chỉ áp cho Item `Type=Ticket`. Resolve item → (Connection Jira Active, issueKey) + kiểm ownership (`IJiraTicketService`). Gọi Jira REST v3 trực tiếp (không đi qua write-back-guard vì comment/attachment độc lập field, không đụng version-token). FE render **inline** trong drawer chi tiết ticket (`JiraTicketPanel`) — không còn form "Sửa ticket".
- `GET /api/items/{id}/comments` — list comment (`{id, body, authorName, authorAccountId, created, updated}[]`, `orderBy=created`). `body` = **markdown subset** (ADF→markdown qua `AdfConverter.ToMarkdown`): `**đậm**`, `*nghiêng*`, `~~gạch~~`, `[text](url)`, bullet `- `, ordered `1. `, và media (file nhúng) → marker `[[attach:{attachmentId}]]` (FE resolve tên file + tải).
- `POST /api/items/{id}/comments` — thêm comment. Body `{ body, mediaIds?[] }`. `body` = markdown subset → ADF (`FromMarkdown`); `mediaIds` = id các attachment (đã upload lên issue) để **nhúng vào comment** dạng ADF media node. Cho phép `body` rỗng nếu có `mediaIds`. → comment vừa tạo.
- `PUT /api/items/{id}/comments/{commentId}` — sửa comment. Body `{ body }` (markdown). Marker `[[attach:id]]` trong body được giữ (round-trip → media node), nên sửa text không mất file đã nhúng.
- `DELETE /api/items/{id}/comments/{commentId}` — xoá comment (204).
- `GET /api/items/{id}/attachments` — list metadata (`{id, filename, mimeType, size, authorName, created}[]`).
- `GET /api/items/{id}/attachments/{attId}/download` — tải nội dung file (stream, `File(data, mime, filename)`; FE tải blob rồi save-as đúng tên).
- `POST /api/items/{id}/attachments` — upload (`multipart/form-data`, field `file`; `X-Atlassian-Token: no-check` server-side; `[RequestSizeLimit]` ~30MB, FE chặn >25MB). → list attachment mới.
- `DELETE /api/items/{id}/attachments/{attId}` — xoá attachment trên Jira (204).
- (400 body/file rỗng, 404 item không phải owner / không tồn tại, 422 item không phải Ticket / connection không Jira-Active, 502 provider lỗi)

## Google Drive — tạo folder & chia sẻ ⭐ SCRUM-79 ✅

Route prefix `/api/drive/*`. Controller mỏng → `IDriveSharingService` → `IDriveGateway` (Google Drive API permissions). **Không** lưu quyền trong DB — hỏi Google mỗi lần list/share. Áp dụng mọi Item `Type=File` có `connectionId` Drive (file lẫn folder). Share **không** qua `PATCH /api/items/{id}`, **không** dùng ETag conflict.

**Sync metadata (A6):** Item File từ Drive sync kèm `metadataJson.isFolder` + `parents` (Google folder id) — FE `isDriveFolder()` dùng dropdown parent + icon.

- `POST /api/drive/folders` — [Authorize]. Body `{ connectionId, name, parentItemId? }`. Tạo folder trên Google Drive + Item local ngay (không chờ cron). `parentItemId` null = gốc My Drive; nếu có → phải là folder Drive cùng connection. Trả **201** `ItemResponse` + `Location: GET /api/items/{id}`. (400 validation, 404 connection/item, 422 tên rỗng/quá dài / parent không phải folder / khác connection / parent đã trash, 403 thiếu scope Drive, 502 provider)
- `GET /api/drive/items/{itemId}/permissions` — [Authorize]. Trả **200** `{ items: DrivePermissionDto[] }`. Mỗi dòng: `{ id, type, role, emailAddress?, displayName?, isOwner, isLink }`. `role`: reader | commenter | writer | owner. `isLink=true` khi type=anyone. (404 item không thuộc user / không phải File Drive, 502 provider)
- `POST /api/drive/items/{itemId}/permissions` — [Authorize]. Body `{ email, role, notify? }` (`role`: reader|commenter|writer; `notify` default true). Mời user qua email. Trả **201** `DrivePermissionDto`. (400 validation, 404, 409 email đã có quyền, 422 business rule, 502)
- `PATCH /api/drive/items/{itemId}/permissions/{permissionId}` — [Authorize]. Body `{ role }`. Đổi role (không áp dụng owner). Trả **200** `DrivePermissionDto`. (404 permission/item, 422 không sửa owner, 502)
- `DELETE /api/drive/items/{itemId}/permissions/{permissionId}` — [Authorize]. Gỡ quyền. Trả **204**. (404, 422 owner, 502)
- `PUT /api/drive/items/{itemId}/link-sharing` — [Authorize]. Body `{ enabled, role? }`. `enabled=true` → bật anyone-with-link (`role` bắt buộc: reader|commenter|writer); `enabled=false` → tắt link. Trả **200** `DrivePermissionDto` hoặc `null` khi tắt. (400 validation, 404, 502)

**FE (SCRUM-79):** `DriveShareDialog`, `CreateDriveFolderModal`, `driveApi`; entry: ItemDetail (Chia sẻ + folder con), Integrations (Tạo folder), WorkspaceToolbar (Folder Drive). i18n `drive.*`.

## Item-Folder — không đổi
`POST/DELETE /api/folders/{id}/items`, `PATCH .../reorder`.

## Emails — gửi trực tiếp ⭐
- `POST /api/emails/send` — [Authorize]. Body `{ connectionId, to[], cc[], bcc[], subject, bodyHtml, attachments[]? }`. Gửi **ngay** (đồng bộ) qua Gmail. `attachments[]` = `{ filename, mimeType, contentBase64 }` (file user tự đính kèm, base64; tổng ≤ 25MB, `[RequestSizeLimit]` 40MB). Validate connection thuộc user + ServiceType=Gmail + Active. Trả `200 { messageId, sentAt }`. (400 validation, 404 connection, 422 connection không phải Gmail / không Active, 502 provider lỗi). Gmail write-back "gửi mới".
- `GET /api/emails/signature?connectionId=` — [Authorize]. Lấy chữ ký HTML đã đặt trong Gmail của connection (qua `users.settings.sendAs`, ưu tiên primary). Trả `200 { signature }` (rỗng nếu chưa đặt HOẶC connection thiếu scope `gmail.settings.basic` — không lỗi). Lưu ý: Gmail API **không** tự chèn chữ ký khi gửi, FE tự append. Scope `gmail.settings.basic` là **optional** (request thêm khi connect Gmail, không bắt buộc); connection tạo trước thay đổi này phải **reconnect** mới đọc được chữ ký.
- `GET /api/EmailContactSuggestions?connectionId=` — [Authorize]. Gợi ý contact từ cache DB `GoogleContacts` khi soạn mail (To/Cc/Bcc). **OData ⊕** convention route (`EmailContactSuggestionsController`): `$filter` (vd `contains(Email,'al') or contains(DisplayName,'al')`), `$orderby`, `$top` (max 20), `$skip`, `$count`, `$select`. Query OData dùng **PascalCase** tên property CLR (`Email`, `DisplayName`, `Source`); JSON response vẫn camelCase. Bắt buộc query `connectionId` (scope server-side theo connection Gmail của user). Trả `{ value: [{ email, displayName, source }], @odata.count? }` — **cả** `Source=Contact` và `Source=OtherContact`. 404 connection, 422 không phải Gmail / không Active. Dữ liệu có sau sync Gmail (cron ~60s / Đồng bộ thủ công); scope optional `contacts.readonly` + `contacts.other.readonly` — thiếu → `value` rỗng, vẫn nhập tay. **Lưu ý:** `$filter/$orderby` chạy **in-memory** sau khi load cache connection (dedupe email) — không SQL push-down.

### Email Threading — Reply / Forward / Attachments ⭐
- `GET /api/emails/{itemId}/thread` — [Authorize]. Lấy toàn bộ luồng hội thoại (thread) của email item. Dữ liệu live từ Gmail API (`threads.get`), không lưu cứng xuống DB. Trả `200 { threadId, subject, messages: [{ messageId, from, to[], cc[], bcc[], subject, bodyHtml, bodyPlainText, occurredAt, isUnread, isStarred, hasAttachment, attachments: [{ attachmentId, filename, mimeType, size }] }] }`. Messages sắp theo thời gian cũ→mới. (404 item không tồn tại/không phải owner, 422 item không có threadId/connection không Gmail/không Active, 502 provider lỗi)
- `POST /api/emails/reply` — [Authorize]. Trả lời (Reply / Reply-All) trong luồng hội thoại email đã có. Body `{ connectionId, itemId, cc?[], bcc?[], bodyHtml, replyAll, attachments[]? }`. Reply-All tự động lấy To/Cc từ metadata + live Gmail API, loại trừ email của chính user (exact match, không substring). Sử dụng header `In-Reply-To`/`References` chuẩn RFC 5322 (lấy `Message-ID` gốc từ metadata) để mail client ngoài Gmail cũng nhóm thread đúng. `attachments[]` = `{ filename, mimeType, contentBase64 }` — file user tự đính kèm (base64, tổng ≤ 25MB, `[RequestSizeLimit]` 40MB). Trả `200 { messageId, threadId, sentAt }`. (400 validation, 404 item/connection, 422 item không có threadId/connection không Gmail, 502 provider lỗi)
- `POST /api/emails/forward` — [Authorize]. Chuyển tiếp email trong luồng. Body `{ connectionId, itemId, to[], cc?[], bcc?[], bodyHtml, includeAttachments, attachments[]? }`. Khi `includeAttachments=true`, tải attachment gốc từ Gmail và đính kèm vào thư mới (MIME multipart/mixed); `attachments[]` = file user thêm mới (base64, như `reply`). Trả `200 { messageId, threadId, sentAt }`. (400 validation, 404 item/connection, 422 item không có threadId, 502 provider lỗi)
- `GET /api/emails/{itemId}/messages/{messageId}/attachments/{attachmentId}?filename=&mimeType=` — [Authorize]. Tải file đính kèm trực tiếp từ Gmail qua Backend Gateway (stream binary). Gọi `attachments.get(messageId, attachmentId)` **trực tiếp** với id client gửi lên — **không** re-fetch thread để so khớp id, vì Gmail cấp `attachmentId` mới mỗi lần đọc thread (id cũ vẫn hợp lệ với `attachments.get`); re-fetch + so khớp id sẽ gây 404 giả. `filename`/`mimeType` (query, optional) để set `Content-Type` + tên file tải về. Quyền đọc giới hạn ở mailbox của user (connection `me`). Trả `200` + `Content-Disposition: attachment`. (404 item/attachment không tồn tại, 422 connection không Gmail/không Active, 502 provider lỗi)
- `GET /api/emails/{itemId}/messages/{messageId}/attachments/zip` — [Authorize]. Tải **toàn bộ** attachment của 1 message trong thread, đóng gói `.zip` (server-side `ZipArchive`, tên trùng tự thêm hậu tố `" (n)"`). Trả `application/zip` (`attachments.zip`). (404 item/message, 422 message không có attachment)
- `POST /api/emails/drafts` — [Authorize]. Tạo nháp mới. Body `{ connectionId, to[], cc[], bcc[], subject?, bodyHtml?, threadId?, inReplyToMessageId?, attachments[]? }`. Trả `200` + `ItemResponse`. (400 validation, 404 connection, 422 connection không phải Gmail/không Active, 502 provider lỗi)
- `PUT /api/emails/drafts/{itemId}` — [Authorize]. Cập nhật nháp đã có. Body `{ connectionId, to[], cc[], bcc[], subject?, bodyHtml?, threadId?, inReplyToMessageId?, attachments[]? }`. Trả `200` + `ItemResponse`. (400 validation, 404 connection/item, 422 connection không phải Gmail/không Active, 502 provider lỗi)
- `POST /api/emails/drafts/{itemId}/send` — [Authorize]. Gửi nháp đã có. Trả `200` + `SendEmailResult`. (404 item, 422 connection không phải Gmail/không Active, 502 provider lỗi)
- `DELETE /api/emails/drafts/{itemId}` — [Authorize]. Xoá nháp **VĨNH VIỄN** trên Gmail (`drafts.delete`, KHÔNG đẩy vào thùng rác → tránh background-sync kéo về lại) + xoá item local. Trả `204 NoContent`. (404 item, 422 connection không phải Gmail/không Active, 502 provider lỗi)

## Scheduled Emails (đổi ConnectionId ⭐)
- `POST /api/scheduled-emails` — {connectionId, to[], cc[], bcc[], subject, bodyHtml, attachments[]?, sendAt} → 201. `attachments[]` = `{ filename, mimeType, contentBase64 }` (file user tự đính kèm, base64; lưu `AttachmentsJson` → cron gửi kèm khi tới hạn; tổng ≤ 25MB). (404 connection, 422 connection không phải Gmail)
- `GET /api/scheduled-emails/{id}` — chi tiết một email hẹn giờ.
- `GET /api/ScheduledEmails` — **OData ⊕** convention route (`ScheduledEmailsController`): `$filter` (vd `Status eq 'Pending'`), `$orderby` (vd `SendAt`, `CreatedAt`), `$top/$skip/$count`. Query OData **PascalCase** tên property CLR; JSON response camelCase. Response `{ value, @odata.count? }`. **Lưu ý:** `$filter/$orderby` chạy **in-memory** sau khi load rows theo `CurrentUserId` (map DTO có parse JSON) — không SQL push-down như `GET /api/Notifications`.
- `PATCH /api/scheduled-emails/{id}/cancel` — (422 đã gửi).
- `POST /api/internal/process-scheduled` — header `X-Cron-Secret` (so khớp `Cron:Secret`; thiếu/sai/secret chưa cấu hình → 401). Không JWT. Gửi mọi email Pending có `sendAt <= now` qua Gmail (token tự refresh từ Connection). Mỗi email lỗi → `Failed` (RetryCount++, LastError) chứ không chặn cả batch. Trả `200 { total, sent, failed }`. ✅ SCRUM-31.
  - **Cron ngoài** gọi endpoint này định kỳ (khuyến nghị 5 phút/lần) — thiết kế mặc định.
  - **Auto-cron nội bộ (tuỳ chọn):** `Cron:AutoRun=true` → BE tự chạy `ScheduledEmailProcessorService` (BackgroundService) quét/gửi mỗi `Cron:IntervalSeconds` (mặc định 300s), gọi thẳng service không qua HTTP/secret. Prod mặc định `false` (theo CLAUDE.md "BE không tự hẹn giờ"); Development mặc định `true` (interval 60s) để test.
- `POST /api/internal/process-sync` — header `X-Cron-Secret` (cùng `Cron:Secret` với `process-scheduled`). Không JWT. Quét mọi Connection Active + Integration enabled → debounce → refresh token nếu cần → `ConnectionSyncDispatcher.SyncAsync` (Gmail/GCal/Drive/Jira). Lỗi **auth bền** (401/403, refresh token fail) → `Status=Error`; lỗi tạm thời (network/5xx) giữ `Active` để cron lần sau retry — mỗi connection lỗi không chặn batch. Trả `200 ProcessSyncResult { totalConnections, successCount, skippedCount, errorCount, details? }`. ✅ SCRUM-72.
  - **Prod (mặc định):** `Cron:SyncAutoRun=true` trong `docker-compose.prod.yml` → `ConnectionSyncProcessorService` mỗi `Cron:SyncIntervalSeconds` (compose: 60s). Cùng pattern cron email — không cần cron-job.org. **Không** bật đồng thời với cron HTTP.
  - **HTTP cron (tuỳ chọn):** `POST /api/internal/process-sync` + `X-Cron-Secret` khi `SyncAutoRun=false` (test local hoặc thay BackgroundService).

## Notifications
- `GET /api/Notifications` — **OData ⊕** convention route (`NotificationsController`): `$filter` (vd `IsRead eq false`), `$orderby` (vd `CreatedAt desc`), `$top/$skip/$count`. Query OData **PascalCase** tên property CLR; JSON response camelCase. Response `{ "@odata.count"?, value: [...] }`. Badge unread: `GET /api/Notifications?$filter=IsRead eq false&$count=true&$top=0`. **SQL push-down:** `IQueryable` EF `AsNoTracking` — `$filter/$orderby` dịch sang SQL (khác ScheduledEmails/EmailContactSuggestions in-memory).
- `PATCH /api/notifications/{id}/read` — đánh dấu đã đọc → 204.
- `POST /api/notifications/read-all` — đánh dấu tất cả đã đọc → 204.
- `POST /api/notifications/dev/seed` — (DEBUG/dev) tạo notification test → 200.
- SignalR hub `/hubs/notifications` — event `ReceiveNotification` (toast + invalidate cache FE). Auth: cookie JWT. **CSRF:** path `/hubs/*` exempt khỏi double-submit (negotiate POST; hub vẫn `[Authorize]`). FE gửi `X-CSRF-Token` khi có cookie và **rebuild hub** trước mỗi lần `start()` retry để header khớp `wh_csrf` sau refresh/BE restart. **Reconnect:** `withAutomaticReconnect` sau connect; start lần đầu retry backoff `0→2s→5s→10s→30s` (lặp); wake khi tab `visible` / `online`. Badge/list poll REST ~45s khi hub chưa kết nối.
- **Copy:** `Title` = i18n key (`notifications.newEmailFrom`, …); `Body` = JSON `{ from?, itemTitle, preview }`. FE dịch title theo lang hệ thống; `preview` hiển thị làm subtitle (snippet email / tên item).
