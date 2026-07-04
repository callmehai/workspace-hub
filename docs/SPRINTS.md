# Sprints & Tickets — Workspace Hub

> Cập nhật 2026-06-21: đồng bộ lại toàn bộ theo Jira (export mới). Số SCRUM-39→46 **KHÔNG** còn là Webhook/Jira — đó là việc khác (39=bỏ DB credentials, 40=admin toggle, 41→50=FE, 51=deploy, 52=finalize, 53=defense — xem bảng). Sprint hiện hành: **Sprint 3**. Lịch sử quyết định: CHANGELOG.md.
>
> Cập nhật 2026-06-28: board Jira đã tạo **7 ticket mới SCRUM-54→60** cho **phase Jira/Atlassian integration** (xem bảng "Phase Jira" cuối file). Tất cả To Do, ở backlog, **chưa code** — current phase vẫn dừng ở SCRUM-38.
>
> **Quy tắc:** sau khi hoàn thành task code nào, cập nhật status ticket đó trong file này (và các .md liên quan).

## Team
| Tên | Jira | Vai trò |
|---|---|---|
| Hải | Hải Trần Việt | Lead — foundation, schema/migration, optimize |
| Lộc | Hoàng Đức Lộc | Auth, conflict resolution, scheduled cron |
| Khánh | Gia Khánh Phạm | OAuth flow, scope, integrations |
| Vũ | VuPM25 | Sync + write-back Google, scheduled email |
| Huy | Nguyễn Quang Huy | Folders/Items/filter/Admin |
| Dũng | Dũng Hoàng Tuấn | Frontend |

> ⚠️ **Đánh số đã đổi so với bản trước.** Các "ticket tạm" 47*/48*/49* nay có số Jira thật:
> bỏ DB credentials = **SCRUM-39**, admin toggle integration = **SCRUM-40**, admin users+stats = **SCRUM-23**.
> **Jira/Atlassian giờ ĐÃ có ticket = SCRUM-54→60** (phase Jira, chưa code — xem bảng cuối file). **Webhook** vẫn **không còn ticket** (ngoài scope đồ án).

---

## Sprint 1 — Nền tảng + Auth + OAuth + workspace (Done)

| Ticket | Việc | Assignee | Status |
|---|---|---|---|
| SCRUM-5 | Khởi tạo solution ASP.NET Core + layer architecture | Hải | ✅ Done |
| SCRUM-6 | EF Core schema + migrations toàn bộ MVP | Hải | ✅ Done |
| SCRUM-7 | Setup Data Protection mã hoá token | Vũ | ✅ Done |
| SCRUM-8 | GitHub repo, branching strategy, README | Hải | ✅ Done |
| SCRUM-9 | Register + login + BCrypt + JWT | Lộc | ✅ Done |
| SCRUM-10 | JWT middleware + protected route + GET /api/auth/me | Lộc | ✅ Done |
| SCRUM-11 | Role-based authorization + logout | Khánh | ✅ Done |
| SCRUM-12 | OAuth start flow + đăng ký app Google Cloud | Khánh | ✅ Done |
| SCRUM-18 | Folder CRUD | Huy | ✅ Done |
| SCRUM-19 | Items list + filter + pagination + search | Huy | ✅ Done |
| SCRUM-21 | Frontend setup: routing, layout, protected route | Dũng | ✅ Done |

## Sprint 2 — Sync + mô hình B + write-back foundation (Done, trừ 37)

| Ticket | Việc | Assignee | Status |
|---|---|---|---|
| SCRUM-13 | OAuth callback + lưu token encrypted | Khánh | ✅ Done |
| SCRUM-14 | List/disconnect/refresh connection | Lộc | ✅ Done — `GET /api/connections` (masked token), `DELETE /api/connections/{id}` (cascade + Items.ConnectionId=NULL), `POST /api/connections/{id}/refresh` (422 invalid→Error) |
| SCRUM-15 | Gmail client + lấy message → Item | Vũ | ✅ Done |
| SCRUM-16 | Sync **theo nhu cầu (lazy / on-demand)** — KHÔNG pull định kỳ | Vũ | ✅ Done — bỏ hẳn timer/cron đọc; khi user CRUD/mở list của 1 connection mới check Active+Enabled + token còn hạn (refresh nếu cần) rồi pull; chống trùng nhờ UNIQUE(ConnectionId, ExternalId); cập nhật LastSyncedAt |
| SCRUM-17 | Sync Calendar + Drive (stretch) | Dũng | ✅ Done |
| SCRUM-20 | Kanban status + Note CRUD + ItemFolders | Huy | ✅ Done |
| SCRUM-22 | Auth pages (login/register) nối API | Dũng | ✅ Done |
| SCRUM-23 | Admin API: users + stats (stretch) | Huy | ✅ Done — `GET /api/admin/users` (search + pagination, Admin) + `GET /api/admin/stats` (tổng user/connection/item, sync error 24h); user thường → 403 |
| SCRUM-25 | Logging + optimize queries | Hải | ✅ Done — `RequestLoggingMiddleware` (request/response + elapsed ms + userId, đặt outermost trước ExceptionMiddleware) + EF query logging dev-only. Query: fix cartesian/N+1 folder list (`GetUserFoldersAsync` gỡ Include FolderShares + `AsSplitQuery` ItemFolders; `GetSharedFoldersAsync` thêm `AsSplitQuery`); Items list `GetPagedAsync` = 1 SELECT OFFSET/FETCH (`AsNoTracking`). |
| SCRUM-32 | Migration: Users multi-auth (PasswordHash null, GoogleSub, AuthProvider) | Lộc | ✅ Done |
| SCRUM-33 | Google Sign-In (đăng nhập Google, auto-link) | Lộc | ✅ Done |
| SCRUM-34 | Migration mô hình B: gộp Connections, Items.ConnectionId + ETag | Hải | ✅ Done — migration `ModelBConnections`, đã apply DB dev |
| SCRUM-35 | OAuth start flow mô hình B (mỗi service authorize riêng) | Khánh | ✅ Done — `InitiateConnectionAsync` nhận `serviceType`, cache vào state; `ProviderStrategyContext` + `BuildAuthUrlAsync` per-service |
| SCRUM-36 | Đổi scope sang read-write (Google) — callback mô hình B | Khánh | ✅ Done — `CompleteConnectionAsync` đọc `serviceType` từ state, `ValidateAndExtract` chỉ check scope của service đó; scope read-write (gmail.modify+send, calendar, drive) |
| SCRUM-37 | Item write-back: ghi ngược lên Google (Email + Event + File) | Vũ | 🔍 **In Review** — `PATCH /api/items/{id}` phân nhánh theo Type; Email modify (label/read/star/trash, KHÔNG sửa nội dung), Event update, File rename/trash; thêm `Items.ETag` |
| SCRUM-39 | Bỏ DB credentials cho Integrations → config/env (`OAuth:`) | Khánh | ✅ Done — migration `RemoveClientCredentialsFromIntegration` (drop 2 cột encrypted), bỏ endpoint PUT /credentials + `SetCredentials`, code đọc `OAuth:{provider}:ClientId/Secret` |
| SCRUM-40 | Admin bật/tắt integration — `PATCH /api/admin/integrations/{key}/enable` | Khánh | ✅ Done — `AdminIntegrationsController`, `ToggleIntegrationAsync`, validator + DTOs |

> Lưu ý sau SCRUM-34: response `POST /api/connections/oauth/callback` đổi shape (trả list connections) — xem API.md; FE cập nhật khi wire.

## Sprint 3 — Hardening + write-back hoàn thiện + scheduled email + bắt đầu FE (hiện hành)

| Ticket | Việc | Assignee | Dependency | Status |
|---|---|---|---|---|
| SCRUM-24 | Exception middleware + error format chuẩn `{ error, message, details[], traceId }` | Lộc | — | ✅ Done — map ValidationException→400 (details[] theo field), Unauthorized→401, Forbidden→403, NotFound→404, Conflict→409, BusinessRule→422, Csrf→400, còn lại→500; 500 không lộ stack ở prod; traceId mọi response. Test: `ExceptionMiddlewareTests` (9 case). |
| SCRUM-26 | Refactor services + clean architecture | Khánh | — | ✅ Done |
| SCRUM-27 | API testing + Postman collection | Huy | 24 | ✅ Done — Postman collection + environment ở `backend/postman/` (48 request, 10 nhóm: auth/folders/items/connections/scheduled-emails/important-contacts/admin/health). Có test script tự assert + tự capture token/id (chạy Collection Runner / Newman). Phủ 200/201/204/400/401/403/404/409/422; happy-path provider tách riêng. |
| SCRUM-28 | README backend + setup guide | Dũng | — | ⏳ To Do |
| SCRUM-29 | Unit test cho service chính | Hải | — | ⏳ To Do |
| SCRUM-30 | Scheduled email: tạo / list / cancel (theo Connections) | Vũ | 34, 36 | ✅ Done — `POST /api/scheduled-emails` (422 nếu Connection ≠ Gmail, 400 nếu sendAt quá khứ); list phân trang; cancel |
| SCRUM-31 | Cron process-scheduled: gửi qua Gmail (token từ Connections) | Hải | 30, 37 | 🔍 In Review — `POST /api/internal/process-scheduled` (header `X-Cron-Secret`, không JWT). Quét batch Pending tới hạn → `IGmailGateway.SendMessageAsync` (MIME RFC2822, subject encoded-word UTF-8); thành công→Sent+SentAt, lỗi→Failed+RetryCount+LastError; trả `{total,sent,failed}`. Config `Cron:Secret`. 6 unit test (`ProcessScheduledEmailsServiceTests`). |
| SCRUM-38 | Conflict resolution chung (ETag → 409) | Lộc | 37 | ✅ Done — `WriteBackGuard : IWriteBackGuard.EnsureNoConflict(storedEtag, providerEtag)` (chỉ so sánh, không I/O; lệch → `ConflictException` → 409 qua middleware; null/empty một bên → skip-check). Thay `TempWriteBackGuard` placeholder của Vũ, DI cập nhật. Test: `WriteBackGuardTests` (9 case). Log `LogWarning` khi conflict. |
| SCRUM-41 | FE: API layer (axios + JWT interceptor + TanStack Query) | Vũ | — | ⏳ To Do |
| SCRUM-42 | FE: Wire Login/Register vào API | Lộc | 41 | ✅ Done — `Login.tsx` + `RegisterPage.tsx` redesign theo prototype (card + logo W + banner lỗi + inline field error + Google button). Login gọi `POST /api/auth/login` → lưu token qua `tokenStore` + `login()`, redirect `/`; lỗi 401 (sai mật khẩu/khoá) hiện ở banner. Register gọi `POST /api/auth/register` (thêm confirm-password client-side), 409 email trùng → banner. Refresh giữ session qua `AuthContext` (`/auth/me`); logout xoá token. `ApiError` type khớp error envelope SCRUM-24. **Google Sign-In FE:** nút "Đăng nhập/Đăng ký bằng Google" gọi `POST /api/auth/google/start` → redirect Google → callback route riêng `/auth/google/callback` (`GoogleCallback.tsx`) đổi code+state qua `POST /api/auth/google/callback` → login + redirect. Dùng config `Google:SignInRedirectUri` (= `/auth/google/callback`) tách khỏi `/oauth/callback` của connect-để-sync. **Logout:** nút ở user block cuối Sidebar gọi `authApi.logout()` (`POST /api/auth/logout`) + xoá token + clear query cache, redirect `/login`. |
| SCRUM-43 | FE: Connections page (list/connect/disconnect per-service) | Khánh | 41 | ⏳ To Do |

**Phối hợp:** SCRUM-37 (Vũ) đang review; SCRUM-38 (Lộc) thống nhất interface `IWriteBackGuard` trước khi code. Scheduled email (30/31) viết theo mô hình B (`ScheduledEmails.ConnectionId` → Connection ServiceType=Gmail).

## Sprint 4 — Frontend đầy đủ + deploy + nghiệm thu

| Ticket | Việc | Assignee | Status |
|---|---|---|---|
| SCRUM-44 | FE: Inbox/Items view (list + filter + search + pagination) | Huy | ✅ Done — `frontend/src/pages/Inbox.tsx` rebuild hoàn toàn theo `docs/prototype/` (design system slate-50/indigo-600, Inter font); TanStack Query `useQuery` key `['items', {status,type,isImportant,search,page,limit}]` gọi `itemsApi.getItems(params)`; filter chips Status (Inbox/Doing/Done) + Type (Email/Event/File/Note) + Important toggle; debounce search 350ms; numbered pagination + Prev/Next (reset page khi đổi filter); skeleton loading / empty-state / error-state; type icon tile màu (blue/amber/emerald/slate); status chip màu; `selectedId` local state cho active row; build TypeScript clean + eslint Inbox clean. |
| SCRUM-45 | FE: Kanban 3 cột (drag-drop) + Folder sidebar | Huy | ✅ Done — rebuild `frontend/src/pages/KanbanBoard.tsx` theo prototype (Light mode slate-50/indigo-600); implement HTML5 Drag & Drop với `onMutate` optimistic cache update; tích hợp Folder sidebar filter (sync URL query `?folder=`); fix TS errors. |
| SCRUM-46 | FE: Write-back actions + xử lý 409 conflict | Vũ | ✅ Done — Hỗ trợ Email (star/read/label/trash), Event (CRUD), File (rename/trash), Ticket (assignee/priority/transition/comment) kèm ETag/version conflict resolution |
| SCRUM-47 | FE: Scheduled email UI (compose/list/cancel) | Khánh | ✅ Done — Giao diện 2 cột, validation client, OData filter/pagination, modal HTML. |
| SCRUM-48 | FE: Loading/error/toast chuẩn | Khánh | ⏳ To Do |
| SCRUM-49 | FE: Admin dashboard (users list + stats charts) | Huy | ⏳ To Do |
| SCRUM-50 | FE: Responsive polish + dashboard chart + dark mode | Dũng | ⏳ To Do |
| SCRUM-51 | Deploy: BE + DB + FE + OAuth prod config | Khánh | ⏳ To Do |
| SCRUM-52 | Finalize: Swagger + setup guide + E2E smoke test prod | Hải | ⏳ To Do |
| SCRUM-53 | Defense: slide + demo phần mỗi người | Lộc | ⏳ To Do |
| SCRUM-61 | FE: Admin bật/tắt integration (wire `PATCH /api/admin/integrations/{key}/enable`) | Khánh | ⏳ To Do |
| SCRUM-65 | Implement CRUD Folder & Assign Items to Folder | Huy | ✅ Done |
| SCRUM-70 | **BE: Tag management** — CRUD tag + gắn/gỡ tag khỏi item | Lộc | ✅ Done — `TagsController` (`GET /api/tags`, `POST`, `PUT /{id}`, `DELETE /{id}`, `POST /{id}/items`, `DELETE /{id}/items/{itemId}`); `ITagService`/`TagService` (owner-scoped CRUD; tên tag unique **trong 1 user** → 409; assign/unassign junction `TagAssignment`, cả tag lẫn item phải thuộc user, trùng gắn → 409); `ITagRepository`/`TagRepository` (list kèm ItemCount, name-exists, assignment CRUD); validators (name ≤100, color hex). Dùng entity `Tag`/`TagAssignment` **có sẵn**. DI đăng ký. **Fix code-review PR #70:** (1) unique index `IX_Tags_UserId_Name` (migration `AddTagUserNameUniqueIndex`, đã apply DB) đóng TOCTOU race, `DbUpdateException`→409 qua `SaveOrThrowConflictAsync`; (2) `UpdateAsync` dùng `GetItemCountAsync` (1 COUNT) thay vì quét toàn bộ tag của user. Unit test: `TagServiceTests` (13). Build + 255 test pass. |
| SCRUM-71 | **FE: Tag UI** — quản lý tag (list/create/edit/delete) + chip tag + gắn/gỡ tag trên item + filter theo tag | Huy | ⏳ To Do — chờ làm sau (wire vào 6 endpoint của SCRUM-70) |

## Phase Jira — Atlassian integration (SCRUM-54→60)

> **Cụm task BE cho tích hợp Jira (CRUD đầy đủ).** Đã lên kế hoạch + tạo ticket trên board (To Do, backlog) nhưng **chưa bắt đầu code** — current phase vẫn dừng ở SCRUM-38. Bắt đầu sau khi Sprint 3 (write-back Google + conflict) ổn định. Mô hình B áp dụng nguyên: Atlassian = 1 Integration, mỗi Jira account = 1 Connection (ServiceType=Jira). Quyết định + lưu ý kỹ thuật (ADF, version-token thay ETag): xem CHANGELOG.md.

| Ticket | Việc | Assignee | Dependency | Status |
|---|---|---|---|---|
| SCRUM-54 | Jira: Atlassian Integration + OAuth 3LO (cloudId), mô hình B | Khánh | — | ✅ Done — `JiraStrategy` + `JiraScopes` (scope `read:jira-work write:jira-work manage:jira-project read:jira-user read:me offline_access`); seed Integration `atlassian` (IsEnabled=false, migration `AddAtlassianIntegrationSeed`); `JiraTokenResponse`; DI đăng ký `JiraStrategy`; `CursorType.JqlUpdated` + `ItemType.Ticket` thêm vào enum. OAuth flow tái dùng `ConnectionsService` + `ProviderStrategyContext` hiện có (`/api/connections/oauth/start` + `/oauth/callback`). |
| SCRUM-55 | Jira: client + đọc/sync issue → Item(Type=Ticket) | Lộc | 54 | ✅ Done — `IJiraGateway` + `JiraGateway` (search JQL qua `POST /rest/api/3/search/jql`, phân trang `nextPageToken`, base URL theo cloudId); `IJiraSyncService` + `JiraSyncService` (sync on-demand, JQL `assignee/reporter = currentUser()` + `updated >= cursor`, dedupe UNIQUE(ConnectionId,ExternalId), cursor `JqlUpdated` = mốc `fields.updated` max); `IJiraItemMapper` + `JiraItemMapper` (issue→Item Type=Ticket, ETag=`fields.updated`, metadata đủ); `AdfConverter` (ADF→plain text cho Snippet); `IAtlassianTokenService` + `AtlassianTokenService` (refresh offline_access, rotate refresh token); wired vào `ConnectionSyncDispatcher` (`ServiceType.Jira`). Unit test: `JiraSyncServiceTests`, `JiraItemMapperTests`, `AdfConverterTests`. |
| SCRUM-56 | Jira: tạo issue (`POST /api/items/ticket`) | Lộc | 55, 59 | ✅ Done — `POST /api/items/ticket` (`CreateTicketRequest` + `CreateTicketRequestValidator`); `ItemWriteBackService.CreateTicketAsync` (validate connection Jira + Active + đúng owner, gọi `IJiraGateway.CreateIssueAsync` → `POST /rest/api/3/issue` → fetch lại qua `GetIssueAsync` → map `JiraItemMapper` → Item Type=Ticket local kèm issueKey/metadata); `AdfConverter.FromPlainText` (text→ADF cho description); Jira 400 (field/project/issueType sai) → 422, thiếu scope → 403, provider lỗi → 502. Unit test: `ItemWriteBackServiceCreateTicketTests`, `CreateTicketRequestValidatorTests`, `AdfConverterTests.FromPlainText`. *(Dependency SCRUM-59 chưa làm: chưa validate project/issueType trước khi gọi — để Jira reject → 422.)* |
| SCRUM-57 | Jira: write-back update (`PATCH /api/items/{id}`, Type=Ticket) qua `IWriteBackGuard` | Lộc | 55, 38 | ✅ Done — nhánh Type=Ticket trong `ItemWriteBackService.PatchItemAsync` → `PatchTicketAsync`; body `{summary?, description?, assignee?, priority?, statusTransition?, labels?[], comment?}` (mở rộng `PatchItemRequest` + validator). Field sửa được (khác Email): summary/description/priority/labels qua `PUT /issue` (`UpdateIssueAsync`, description→ADF); reassign qua `PUT /issue/{key}/assignee`; **status qua transition** (`GetTransitionsAsync` tra tên→id, không khả dụng → 422; `TransitionIssueAsync`); comment qua `POST /comment` (ADF). Conflict: Jira KHÔNG có HTTP ETag → version-token = `fields.updated`, đi qua **cùng `IWriteBackGuard` (SCRUM-38)** → lệch 409. Ghi xong fetch lại + cập nhật ETag/metadata. Reject field Google trên ticket → 422; thiếu scope → 403; provider lỗi → 502; Jira 400 → 422. Unit test: `ItemWriteBackServicePatchTicketTests` (9), `PatchItemRequestValidatorTests` (8). |
| SCRUM-58 | Jira: xoá issue (`DELETE /api/items/{id}`, Type=Ticket) | Lộc | 55 | ✅ Done — nhánh `ItemType.Ticket` trong `ItemWriteBackService.DeleteItemAsync` → `IJiraGateway.DeleteIssueAsync` (`DELETE /rest/api/3/issue/{key}?deleteSubtasks=true`) rồi mới `Remove` local. Owner check qua `GetByIdAndUserAsync` (không phải owner → 404). Jira thiếu quyền → 403, provider lỗi → 502 — exception bay lên **trước** khi xoá local nên Item giữ nguyên (KHÔNG nuốt lỗi, KHÔNG xoá lệch). Unit test: `ItemWriteBackServiceDeleteTicketTests` (4). |
| SCRUM-59 | Jira: metadata helpers (projects / issue-types / transitions / assignable-users / priorities) | Lộc | 54 | ✅ Done — `JiraController` (`/api/jira/*`, `?connectionId=`): `GET /projects`, `/issue-types?projectKey=`, `/priorities`, `/assignable-users?projectKey=&query=`, `/transitions?itemId=`. `IJiraMetadataService` + `JiraMetadataService` (validate connection thuộc user + Jira + Active; **cache nhẹ TTL 5'** cho project/issue-type/priority qua `IMemoryCache`; transitions/assignable-users không cache vì phụ thuộc state). Gateway thêm `GetProjectsAsync`/`GetIssueTypesAsync`/`GetPrioritiesAsync`/`GetAssignableUsersAsync` (+ `GetTransitionsAsync` dùng lại từ SCRUM-57). 404 connection (cả của user khác), 422 không phải Jira / projectKey thiếu, 502 provider. Unit test: `JiraMetadataServiceTests` (13). |
| SCRUM-60 | Jira: ImportantContacts `JiraAccount` + Notification type (optional) | Lộc | 54 | ✅ Done — thêm `ImportantContactType.JiraAccount` (enum lưu string, KHÔNG cần migration); CRUD `ImportantContactsController` (`GET /api/importantcontacts?type=`, `POST`, `DELETE /{id}`) + `IImportantContactService`/`ImportantContactService` + repo (`GetByUserAsync`/`ExistsAsync`/`GetByIdAndUserAsync`) + validator (Email→email hợp lệ, JiraAccount→accountId tự do). UNIQUE(UserId,Type,Identifier) → 409 trùng; owner-only → 404. Unit test: `ImportantContactServiceTests` (6), `CreateImportantContactRequestValidatorTests` (5). **Notification type cho Jira: bỏ qua** (optional + chưa có nguồn sync-event Jira → tránh dead code). |

**Phối hợp:** 54 mở đường (Integration + OAuth + cloudId) cho tất cả. 57 tái dùng `IWriteBackGuard` của SCRUM-38 (Jira không có HTTP ETag → dùng `fields.updated` làm version-token lưu trong `Items.ETag`). 56 cần 59 (metadata để chọn project/issue-type/priority khi tạo). Khác Gmail: nội dung Jira (summary/description) **sửa được**, không immutable.

---

## Auth overhaul — cookie + refresh/Redis + OTP (SCRUM-62→64)

> ⚠️ **Phát sinh ngoài board (yêu cầu owner 2026-06-30), VƯỢT SCOPE SCRUM-42, ĐẢO nhiều quyết định nền tảng auth** (xem CHANGELOG mục [2026-06-30]). Làm theo **3 nhánh riêng** (không dồn vào PR SCRUM-42) theo thứ tự phụ thuộc: 62 → 63 → 64. **Cần báo team trước khi merge** vì đụng auth chung (Lộc/Khánh/Vũ). Số ticket 62/63/64 là **tạm gán ở docs** — tạo ticket Jira thật trước khi merge.

| Ticket | Việc | Assignee | Dependency | Status |
|---|---|---|---|---|
| SCRUM-62 | Access token → **HttpOnly cookie** + CSRF (BE Set-Cookie + đọc JWT từ cookie; FE bỏ localStorage, withCredentials, CSRF header) | — | — | ✅ Done (nhánh `feat/SCRUM-62-httponly-cookie-auth`) — BE: `AuthCookieService` set cookie `wh_access` (HttpOnly) + `wh_csrf` (double-submit); `AuthController` trả `AuthResultDto` (bỏ token khỏi body) + logout xoá cookie; JwtBearer `OnMessageReceived` đọc token từ cookie (fallback Bearer cho Swagger/Postman); `CsrfMiddleware` bắt header `X-CSRF-Token` trên request mutating có cookie; CORS opt-in `Cors:AllowedOrigins` + `AllowCredentials` (prod), `Auth:CrossSiteCookies` cho SameSite=None. FE: `api.ts` `withCredentials` + interceptor gắn CSRF header, bỏ `tokenStore`/Bearer; `AuthContext.login(user)` (không nhận token); `/auth/me` luôn gọi (cookie quyết định). Build BE + FE pass. |
| SCRUM-63 | **Refresh token + Redis** (rotation, `/auth/refresh`, logout stateful, docker-compose `wh-redis`, `AddStackExchangeRedisCache`; FE auto-refresh single-flight) | — | 62 | ✅ Done (nhánh `feat/SCRUM-63-refresh-token-redis`) — BE: `IRefreshTokenService`/`RefreshTokenService` (JWT+`jti`, Redis `refresh:{jti}` + `refreshfam:{fam}`, rotation, reuse→revoke family, inactive user→reject); `AuthCookieService.IssueRefreshCookie` cookie `wh_refresh` (HttpOnly, Path=/api/auth/refresh) + ClearAuthCookies xoá cả refresh; `AuthController` issue refresh khi login/register/google + `POST /api/auth/refresh` (rotate) + logout revoke; `AddStackExchangeRedisCache` (fallback in-memory + warning nếu thiếu `ConnectionStrings:Redis`); `docker-compose.yml` (`wh-redis` + `wh-sqlserver`); access TTL 15' (`Jwt:ExpiresIn=900`), refresh 7d (`Jwt:RefreshExpiresIn`). FE: `api.ts` interceptor 401 → `/auth/refresh` single-flight → retry, fail → /login. Test: `RefreshTokenServiceTests` (8). Build BE+FE pass, 223 test pass. |
| SCRUM-64 | **OTP đăng ký qua Twilio** (cột `Users.Phone`/`PhoneVerified` + migration, `ISmsSender`+Twilio, `/auth/send-otp` + `/auth/verify-otp`, OTP store Redis, login chặn chưa verify; FE field SĐT + màn OTP) | — | 63 (dùng Redis store) | ✅ Done (nhánh `feat/SCRUM-64-register-otp-twilio`) — Migration `AddUserPhoneOtp` (Phone nvarchar(20) null + PhoneVerified bit default **true** cho user cũ) **đã apply DB**. BE: `ISmsSender` + `TwilioSmsSender` (REST, basic auth) + `LogSmsSender` (dev fallback, OTP ra log — chọn theo `Sms:Twilio:AccountSid`); `IOtpService`/`OtpService` (Redis `otp:{userId}` hash SHA-256 + attempts, TTL 5', cooldown 60s, max 5 sai); register tạo user `PhoneVerified=false` + gửi OTP (trả `RegisterResult`, KHÔNG phát token); `POST /auth/send-otp` + `/auth/verify-otp` (verify → set PhoneVerified=true → đăng nhập); login chặn chưa verify → 403 `PHONE_NOT_VERIFIED`. FE: Register thêm field SĐT (E.164) → `/verify-otp` (`VerifyOtp.tsx`, resend + đếm ngược); Login bắt 403 → gửi OTP + sang màn verify. Test: `OtpServiceTests` (5) — **228 test pass**. Build BE+FE pass. |

**Phối hợp / lưu ý:**
- 62 đổi **hợp đồng response auth** (bỏ `accessToken` khỏi body) → mọi nơi FE đọc token phải sửa; báo Dũng (FE) + Lộc (auth).
- 63 đổi `AddDistributedMemoryCache` → Redis: ảnh hưởng cả `ConnectionsService` (đang dùng `IDistributedCache` cho OAuth state) — verify state OAuth vẫn chạy trên Redis.
- 64 migration thêm cột Users: `PhoneVerified` default **true** cho user cũ (không phá login hiện có); chỉ user đăng ký mới sau migration mới phải verify.
- Twilio = trial; dev fallback `LogSmsSender` (OTP ra log) khi chưa cấu hình `Sms:Twilio:*`.

---

## Ngoài scope (KHÔNG có ticket Jira)

Các ý tưởng dưới đây **không nằm trong Jira hiện tại** — chỉ là định hướng tương lai, đừng code, đừng gán số SCRUM (số 39–46 nay đã dùng cho việc khác; Jira giờ là 54→60):

- Webhook / push realtime (Gmail watch + Pub/Sub, Calendar/Drive/Jira watch) thay sync on-demand.
- Social / friend system, AI workflow.
