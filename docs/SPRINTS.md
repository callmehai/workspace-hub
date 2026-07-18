# Sprints & Tickets — Workspace Hub

> **Cập nhật 2026-07-16:** SCRUM-79 bổ sung Case 1 link-restrict (popup giống Drive). Sprint hiện hành **Sprint 4**. Nền tảng 2 chiều **đã xong** (write-back Google 37/38, scheduled email 30/31). **Phase Jira 54→60 đã code xong** (không còn "chưa code"). **Auth overhaul 62→64** (cookie/refresh/OTP) phát sinh ngoài board ban đầu, nay là ticket thật. Thêm ticket mới **67/68/69/72** (highlight unread, notifications, People API, cron sync định kỳ). Số SCRUM-39→46 KHÔNG phải Webhook/Jira (39=bỏ DB creds, 40=admin toggle, 41→50=FE, 51=deploy, 52=finalize, 53=defense). Lịch sử quyết định: CHANGELOG.md.
>
> **Cập nhật 2026-07-18 (ticketless):** ✅ **Multi-connection per integration** — 1 user kết nối **nhiều tài khoản Google** (Gmail/Calendar/Drive khác email). Mô hình B đã sẵn đa tài khoản → chỉ đổi OAuth `prompt=select_account` + mở UI (trang Kết nối liệt kê N account/service + "Thêm tài khoản"; bộ lọc tài khoản ở Inbox/Kanban). **Jira multi-site từng-grant-một** (callback chọn site CHƯA connect thay vì luôn `resources[0]` — mỗi site 1 grant riêng, token độc lập) + **callback UPSERT khi trùng service+account** (fix nút "Kết nối lại" xưa giờ 409). Chi tiết: CHANGELOG [2026-07-18].
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

> ⚠️ **Đánh số:** bỏ DB credentials = **SCRUM-39**, admin toggle = **SCRUM-40**, admin users+stats = **SCRUM-23**.
> **Jira/Atlassian = SCRUM-54→60 ĐÃ CODE XONG** (xem bảng "Phase Jira"). **Webhook** vẫn **ngoài scope** (không có ticket) — nhưng SCRUM-72 có **cron sync định kỳ** (khác webhook, xem bảng Sprint 4).

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
| SCRUM-37 | Item write-back: ghi ngược lên Google (Email + Event + File) | Vũ | ✅ **Done** — `PATCH /api/items/{id}` phân nhánh theo Type; Email modify (label/read/star/trash, KHÔNG sửa nội dung), Event update, File rename/trash; thêm `Items.ETag`. **Bổ sung:** Hỗ trợ lấy luồng hội thoại (`GetThreadAsync`), Forward, Reply, Reply All (`SendInThreadAsync`), và tải đính kèm gốc trực tiếp qua Gmail API. |
| SCRUM-39 | Bỏ DB credentials cho Integrations → config/env (`OAuth:`) | Khánh | ✅ Done — migration `RemoveClientCredentialsFromIntegration` (drop 2 cột encrypted), bỏ endpoint PUT /credentials + `SetCredentials`, code đọc `OAuth:{provider}:ClientId/Secret` |
| SCRUM-40 | Admin bật/tắt integration — `PATCH /api/admin/integrations/{key}/enable` | Khánh | ✅ Done — `AdminIntegrationsController`, `ToggleIntegrationAsync`, validator + DTOs |

> Lưu ý sau SCRUM-34: response `POST /api/connections/oauth/callback` đổi shape (trả list connections) — xem API.md; FE cập nhật khi wire.

## Sprint 3 — Hardening + write-back hoàn thiện + scheduled email + bắt đầu FE (Done)

| Ticket | Việc | Assignee | Dependency | Status |
|---|---|---|---|---|
| SCRUM-24 | Exception middleware + error format chuẩn `{ error, message, details[], traceId }` | Lộc | — | ✅ Done — map ValidationException→400 (details[] theo field), Unauthorized→401, Forbidden→403, NotFound→404, Conflict→409, BusinessRule→422, Csrf→400, còn lại→500; 500 không lộ stack ở prod; traceId mọi response. Test: `ExceptionMiddlewareTests` (9 case). |
| SCRUM-26 | Refactor services + clean architecture | Khánh | — | ✅ Done |
| SCRUM-27 | API testing + Postman collection | Huy | 24 | ✅ Done — Postman collection + environment ở `backend/postman/` (48 request, 10 nhóm: auth/folders/items/connections/scheduled-emails/important-contacts/admin/health). Có test script tự assert + tự capture token/id (chạy Collection Runner / Newman). Phủ 200/201/204/400/401/403/404/409/422; happy-path provider tách riêng. |
| SCRUM-28 | README backend + setup guide | Dũng | — | ✅ Done |
| SCRUM-29 | Unit test cho service chính | Hải | — | 🔄 In Progress |
| SCRUM-30 | Scheduled email: tạo / list / cancel (theo Connections) | Vũ | 34, 36 | ✅ Done — `POST /api/scheduled-emails` (422 nếu Connection ≠ Gmail, 400 nếu sendAt quá khứ); list phân trang; cancel |
| SCRUM-31 | Cron process-scheduled: gửi qua Gmail (token từ Connections) | Hải | 30, 37 | ✅ Done — `POST /api/internal/process-scheduled` (header `X-Cron-Secret`, không JWT). Quét batch Pending tới hạn → `IGmailGateway.SendMessageAsync` (MIME RFC2822, subject encoded-word UTF-8); thành công→Sent+SentAt, lỗi→Failed+RetryCount+LastError; trả `{total,sent,failed}`. Config `Cron:Secret`. 6 unit test (`ProcessScheduledEmailsServiceTests`). |
| SCRUM-38 | Conflict resolution chung (ETag → 409) | Lộc | 37 | ✅ Done — `WriteBackGuard : IWriteBackGuard.EnsureNoConflict(storedEtag, providerEtag)` (chỉ so sánh, không I/O; lệch → `ConflictException` → 409 qua middleware; null/empty một bên → skip-check). Thay `TempWriteBackGuard` placeholder của Vũ, DI cập nhật. Test: `WriteBackGuardTests` (9 case). Log `LogWarning` khi conflict. |
| SCRUM-41 | FE: API layer (axios + JWT interceptor + TanStack Query) | Dũng | — | ✅ Done |
| SCRUM-42 | FE: Wire Login/Register vào API | Lộc | 41 | ✅ Done — `Login.tsx` + `RegisterPage.tsx` redesign theo prototype (card + logo W + banner lỗi + inline field error + Google button). Login gọi `POST /api/auth/login` → lưu token qua `tokenStore` + `login()`, redirect `/`; lỗi 401 (sai mật khẩu/khoá) hiện ở banner. Register gọi `POST /api/auth/register` (thêm confirm-password client-side), 409 email trùng → banner. Refresh giữ session qua `AuthContext` (`/auth/me`); logout xoá token. `ApiError` type khớp error envelope SCRUM-24. **Google Sign-In FE:** nút "Đăng nhập/Đăng ký bằng Google" gọi `POST /api/auth/google/start` → redirect Google → callback route riêng `/auth/google/callback` (`GoogleCallback.tsx`) đổi code+state qua `POST /api/auth/google/callback` → login + redirect. Dùng config `Google:SignInRedirectUri` (= `/auth/google/callback`) tách khỏi `/oauth/callback` của connect-để-sync. **Logout:** nút ở user block cuối Sidebar gọi `authApi.logout()` (`POST /api/auth/logout`) + xoá token + clear query cache, redirect `/login`. |
| SCRUM-43 | FE: Connections page (list/connect/disconnect per-service) | Khánh | 41 | ✅ Done |

**Phối hợp:** SCRUM-37 (Vũ) + 38 (Lộc) đã done — write-back qua `IWriteBackGuard` (ETag → 409). Scheduled email (30/31) theo mô hình B (`ScheduledEmails.ConnectionId` → Connection ServiceType=Gmail).

## Sprint 4 — Frontend đầy đủ + deploy + nghiệm thu

| Ticket | Việc | Assignee | Status |
|---|---|---|---|
| SCRUM-44 | FE: Inbox/Items view (list + filter + search + pagination) | Huy | ✅ Done — `frontend/src/pages/Inbox.tsx` rebuild hoàn toàn theo `docs/prototype/` (design system slate-50/indigo-600, Inter font); TanStack Query `useQuery` key `['items', {status,type,isImportant,search,page,limit}]` gọi `itemsApi.getItems(params)`; filter chips Status (Inbox/Doing/Done) + Type (Email/Event/File/Note) + Important toggle; debounce search 350ms; numbered pagination + Prev/Next (reset page khi đổi filter); skeleton loading / empty-state / error-state; type icon tile màu (blue/amber/emerald/slate); status chip màu; `selectedId` local state cho active row; build TypeScript clean + eslint Inbox clean. |
| SCRUM-45 | FE: Kanban 3 cột (drag-drop) + Folder sidebar | Huy | ✅ Done — rebuild `frontend/src/pages/KanbanBoard.tsx` theo prototype (Light mode slate-50/indigo-600); implement HTML5 Drag & Drop với `onMutate` optimistic cache update; tích hợp Folder sidebar filter (sync URL query `?folder=`); fix TS errors. |
| SCRUM-46 | FE: Write-back actions + xử lý 409 conflict | Vũ | ✅ Done — Hỗ trợ Email (star/read/label/trash, **kèm giao diện Thread View + Reply/Forward/đính kèm + preview/download Attachment + gộp thread ở list**), Event (CRUD), File (rename/trash), Ticket (assignee/priority/transition/comment) kèm ETag/version conflict resolution (Fix lỗi update assignee lần đầu tiên); Thêm Project filter và thông tin project vào Ticket. **Ticket overhaul:** bỏ form "Sửa ticket" lớn → **sửa INLINE ngay tại từng field** (`JiraTicketPanel`): bút chì cho Summary/Description/Labels, dropdown cho Ưu tiên/Assignee(search)/Trạng thái(transition) — chọn/lưu là PATCH 2 chiều luôn. Thêm **comment CRUD** (list/add/edit/delete) + **attachment thật** (list/tải xuống/upload ≤25MB/xoá) 2 chiều Jira qua `IJiraTicketService` + endpoints `/api/items/{id}/comments|attachments`. |
| SCRUM-47 | FE: Scheduled email UI (compose/list/cancel) | Khánh | ✅ Done — Giao diện 2 cột, validation client, OData filter/pagination, modal HTML. |
| SCRUM-48 | FE: Loading/error/toast chuẩn | Khánh | ✅ Done |
| SCRUM-49 | FE: Admin dashboard (users list + stats charts) + Lock/Unlock user | Huy | ✅ Done — Hoàn thành trang Admin Dashboard hiển thị thống kê tổng quan (biểu đồ tròn Connections) và danh sách User phân trang. Bổ sung tính năng Khóa / Mở khóa người dùng (User Lock/Unlock) qua API POST /api/admin/users/{id}/toggle-active kèm bảo vệ tự khóa tài khoản Admin. |
| SCRUM-50 | FE: Responsive polish + dashboard chart + dark mode | Dũng | ⏳ To Do |
| SCRUM-51 | Deploy: BE + DB + FE + OAuth prod config | Khánh | ⏳ To Do (board) — **thực tế đã deploy** lên AWS Lightsail (`app.workspace-hub.space`, Docker Compose + CI/CD auto-deploy develop); còn lại: chốt OAuth redirect prod cho từng account, đóng ticket. Xem `docs/DEPLOY.md`. |
| SCRUM-52 | Finalize: Swagger + setup guide + E2E smoke test prod | Hải | ⏳ To Do |
| SCRUM-53 | Defense: slide + demo phần mỗi người | Lộc | ⏳ To Do |
| SCRUM-61 | FE: Admin bật/tắt integration (wire `PATCH /api/admin/integrations/{key}/enable`) | Khánh | 🔄 In Progress — `GET /api/admin/integrations` + `adminApi.getIntegrations`/`toggleIntegration`; section **Tích hợp dịch vụ** trên `AdminDashboard` (toggle + ConfirmDialog bật/tắt Google/Atlassian). |
| SCRUM-65 | Implement CRUD Folder & Assign Items to Folder | Huy | ✅ Done |
| SCRUM-67 | FE: Highlight email chưa đọc (đồng bộ trạng thái read/unread với Gmail; fix payload sai → 409) — **không thêm cột DB** | Vũ | ⏳ To Do |
| SCRUM-68 | Notifications in-app (chuông + badge unread + dropdown mark-as-read; BE API list phân trang + cập nhật trạng thái đọc) | Khánh | 🔄 In Progress |
| SCRUM-69 | Tích hợp Google People API gợi ý contact khi soạn email: sync **Contact + OtherContact** vào DB; autocomplete To/Cc/Bcc **cả hai nguồn**; debounce, chip; fallback nhập tay khi API lỗi | Khánh | ✅ Done — migration `AddGoogleContacts`; optional scopes `contacts.readonly` + `contacts.other.readonly`; `IPeopleGateway`/`PeopleGateway` (list phân trang); sync best-effort trong `GmailSyncService` (kéo kèm mỗi lần sync Gmail — cron định kỳ / manual / lazy); `GET /api/emails/contacts/suggest`; FE `EmailChipsInput` debounce + dropdown; wire `SendEmail` + `ScheduledEmails`. Test: `GmailSyncServiceTests`, `SendEmailServiceSuggestContactsTests`. |
| SCRUM-76 | **Google Contacts write-back 2 chiều + trang `/contacts`** — CRUD contact đã lưu (`Source=Contact`) ghi ngược People API; OtherContact read-only; conflict etag 409; trang `/contacts`. **Phụ thuộc SCRUM-69.** Spec: `docs/CONTACTS_WRITEBACK.md` | Khánh | ⏳ To Do |
| SCRUM-72 | **[BE+FE] Cron sync connection định kỳ + FE auto-refresh** — `POST /api/internal/process-sync` (X-Cron-Secret, exempt CSRF) quét Connection Active + refresh token + dispatch sync theo ServiceType; FE polling Inbox/Kanban/Integrations (tắt khi tab hidden). **BỔ SUNG** sync định kỳ ngoài on-demand, KHÔNG phải webhook. | Dũng | ⏳ To Do |
| SCRUM-70 | **BE: Tag management** — CRUD tag + gắn/gỡ tag khỏi item | Lộc | ✅ Done — `TagsController` (`GET /api/tags`, `POST`, `PUT /{id}`, `DELETE /{id}`, `POST /{id}/items`, `DELETE /{id}/items/{itemId}`); `ITagService`/`TagService` (owner-scoped CRUD; tên tag unique **trong 1 user** → 409; assign/unassign junction `TagAssignment`, cả tag lẫn item phải thuộc user, trùng gắn → 409); `ITagRepository`/`TagRepository` (list kèm ItemCount, name-exists, assignment CRUD); validators (name ≤100, color hex). Dùng entity `Tag`/`TagAssignment` **có sẵn**. DI đăng ký. **Fix code-review PR #70:** (1) unique index `IX_Tags_UserId_Name` (migration `AddTagUserNameUniqueIndex`, đã apply DB) đóng TOCTOU race, `DbUpdateException`→409 qua `SaveOrThrowConflictAsync`; (2) `UpdateAsync` dùng `GetItemCountAsync` (1 COUNT) thay vì quét toàn bộ tag của user. Unit test: `TagServiceTests` (13). Build + 255 test pass. |
| SCRUM-71 | **FE: Tag UI** — quản lý tag (list/create/edit/delete) + chip tag + gắn/gỡ tag trên item + filter theo tag | Huy | ✅ Done — `tagsApi` (wire 6 endpoint SCRUM-70); `TagManagerModal` (CRUD tag + palette màu + itemCount), `TagChip` (chip màu tint + nút gỡ); ItemDetail: section gắn/gỡ tag (dropdown chọn tag chưa gắn + link quản lý); chip tag hiển thị list (Inbox) + board (Kanban) + drawer; filter theo tag ở `WorkspaceToolbar` (chip tag + nút quản lý) wire vào query cả 2 view; i18n VI/EN đủ. **BE bổ sung (không migration):** `ItemResponse.Tags` (join `TagAssignment.Tag`, Include ở repo paged + byId), `GetItemsRequest.TagId` + filter `TagAssignments.Any`. Build BE (256 test pass) + FE (lint sạch). |
| SCRUM-73 | **[BE+FE] Email Drafts** — Tạo, Cập nhật, Gửi và Xoá nháp (Trash) qua Gmail; tự động lưu nháp debounced 2.0s; hỗ trợ inline reply draft và compose draft độc lập | Vũ | — | ✅ Done — Backend endpoints, frontend compose, inline reply draft, auto-save/discard, and bulk/permanent deletion in Trash/Spam. |
| SCRUM-79 | **Google Drive: tạo folder + chia sẻ** — `POST /api/drive/folders`; permissions CRUD + link sharing (`/api/drive/items/{id}/permissions`, `link-sharing`); sync metadata `isFolder`/`parents`; FE dialog share + modal tạo folder + wire Integrations/Toolbar/ItemDetail | Dũng | ✅ Done — BE/FE SCRUM-79 gốc. **Bổ sung 2026-07-16:** Case 1 tắt link giống Drive (`DetectLinkRestrictConflictAsync`, GET `restrict-conflict`, PUT `confirmRestrictParent`, FE `DriveLinkRestrictDialog`); Case 2 không popup. Spec: `docs/DRIVE_FOLDER_SHARING.md`. |

## Phase Jira — Atlassian integration (SCRUM-54→60)

> **Cụm task BE cho tích hợp Jira (CRUD đầy đủ) — ĐÃ CODE XONG (55→60 Done).** Mô hình B: Atlassian = 1 Integration, mỗi Jira account = 1 Connection (ServiceType=Jira). Migration `EnableJiraIntegration` bật `atlassian` IsEnabled=true (prod tự apply khi deploy). Quyết định + lưu ý kỹ thuật (ADF 2 chiều, `fields.updated` làm version-token thay ETag HTTP): xem CHANGELOG.md. *Board vẫn đánh SCRUM-54 "To Do" nhưng JiraStrategy/OAuth 3LO/cloudId đã có trong code — cần đóng ticket 54 trên Jira cho khớp.*

| Ticket | Việc | Assignee | Dependency | Status |
|---|---|---|---|---|
| SCRUM-54 | Jira: Atlassian Integration + OAuth 3LO (cloudId), mô hình B | Khánh | — | ✅ Done — `JiraStrategy` + `JiraScopes` (scope `read:jira-work write:jira-work manage:jira-project read:jira-user read:me offline_access`); seed Integration `atlassian` (IsEnabled=false, migration `AddAtlassianIntegrationSeed`); `JiraTokenResponse`; DI đăng ký `JiraStrategy`; `CursorType.JqlUpdated` + `ItemType.Ticket` thêm vào enum. OAuth flow tái dùng `ConnectionsService` + `ProviderStrategyContext` hiện có (`/api/connections/oauth/start` + `/oauth/callback`). |
| SCRUM-55 | Jira: client + đọc/sync issue → Item(Type=Ticket) | Lộc | 54 | ✅ Done — `IJiraGateway` + `JiraGateway` (search JQL qua `POST /rest/api/3/search/jql`, phân trang `nextPageToken`, base URL theo cloudId); `IJiraSyncService` + `JiraSyncService` (sync on-demand, JQL kéo **toàn bộ** issue account thấy — `ORDER BY updated ASC` + `updated >= cursor`, bỏ giới hạn `currentUser` để lọc theo user trong app; batch 250; dedupe UNIQUE(ConnectionId,ExternalId), cursor `JqlUpdated` = mốc `fields.updated` max); `IJiraItemMapper` + `JiraItemMapper` (issue→Item Type=Ticket, ETag=`fields.updated`, metadata đủ); `AdfConverter` (ADF→plain text cho Snippet); `IAtlassianTokenService` + `AtlassianTokenService` (refresh offline_access, rotate refresh token); wired vào `ConnectionSyncDispatcher` (`ServiceType.Jira`). Unit test: `JiraSyncServiceTests`, `JiraItemMapperTests`, `AdfConverterTests`. |
| SCRUM-56 | Jira: tạo issue (`POST /api/items/ticket`) | Lộc | 55, 59 | ✅ Done — `POST /api/items/ticket` (`CreateTicketRequest` + `CreateTicketRequestValidator`); `ItemWriteBackService.CreateTicketAsync` (validate connection Jira + Active + đúng owner, gọi `IJiraGateway.CreateIssueAsync` → `POST /rest/api/3/issue` → fetch lại qua `GetIssueAsync` → map `JiraItemMapper` → Item Type=Ticket local kèm issueKey/metadata); `AdfConverter.FromPlainText` (text→ADF cho description); Jira 400 (field/project/issueType sai) → 422, thiếu scope → 403, provider lỗi → 502. Unit test: `ItemWriteBackServiceCreateTicketTests`, `CreateTicketRequestValidatorTests`, `AdfConverterTests.FromPlainText`. *(Dependency SCRUM-59 chưa làm: chưa validate project/issueType trước khi gọi — để Jira reject → 422.)* |
| SCRUM-57 | Jira: write-back update (`PATCH /api/items/{id}`, Type=Ticket) qua `IWriteBackGuard` | Lộc | 55, 38 | ✅ Done — nhánh Type=Ticket trong `ItemWriteBackService.PatchItemAsync` → `PatchTicketAsync`; body `{summary?, description?, assignee?, priority?, statusTransition?, labels?[], comment?}` (mở rộng `PatchItemRequest` + validator). Field sửa được (khác Email): summary/description/priority/labels qua `PUT /issue` (`UpdateIssueAsync`, description→ADF); reassign qua `PUT /issue/{key}/assignee`; **status qua transition** (`GetTransitionsAsync` tra tên→id, không khả dụng → 422; `TransitionIssueAsync`); comment qua `POST /comment` (ADF). Conflict: Jira KHÔNG có HTTP ETag → version-token = `fields.updated`, đi qua **cùng `IWriteBackGuard` (SCRUM-38)** → lệch 409. Ghi xong fetch lại + cập nhật ETag/metadata. Reject field Google trên ticket → 422; thiếu scope → 403; provider lỗi → 502; Jira 400 → 422. Unit test: `ItemWriteBackServicePatchTicketTests` (9), `PatchItemRequestValidatorTests` (8). |
| SCRUM-58 | Jira: xoá issue (`DELETE /api/items/{id}`, Type=Ticket) | Lộc | 55 | ✅ Done — nhánh `ItemType.Ticket` trong `ItemWriteBackService.DeleteItemAsync` → `IJiraGateway.DeleteIssueAsync` (`DELETE /rest/api/3/issue/{key}?deleteSubtasks=true`) rồi mới `Remove` local. Owner check qua `GetByIdAndUserAsync` (không phải owner → 404). Jira thiếu quyền → 403, provider lỗi → 502 — exception bay lên **trước** khi xoá local nên Item giữ nguyên (KHÔNG nuốt lỗi, KHÔNG xoá lệch). Unit test: `ItemWriteBackServiceDeleteTicketTests` (4). |
| SCRUM-59 | Jira: metadata helpers (projects / issue-types / transitions / assignable-users / priorities) | Lộc | 54 | ✅ Done — `JiraController` (`/api/jira/*`, `?connectionId=`): `GET /projects`, `/issue-types?projectKey=`, `/priorities`, `/assignable-users?projectKey=&query=`, `/transitions?itemId=`. `IJiraMetadataService` + `JiraMetadataService` (validate connection thuộc user + Jira + Active; **cache nhẹ TTL 5'** cho project/issue-type/priority qua `IMemoryCache`; transitions/assignable-users không cache vì phụ thuộc state). Gateway thêm `GetProjectsAsync`/`GetIssueTypesAsync`/`GetPrioritiesAsync`/`GetAssignableUsersAsync` (+ `GetTransitionsAsync` dùng lại từ SCRUM-57). 404 connection (cả của user khác), 422 không phải Jira / projectKey thiếu, 502 provider. Unit test: `JiraMetadataServiceTests` (13). |
| SCRUM-60 | Jira: ImportantContacts `JiraAccount` + Notification type (optional) | Lộc | 54 | ✅ Done — thêm `ImportantContactType.JiraAccount` (enum lưu string, KHÔNG cần migration); CRUD `ImportantContactsController` (`GET /api/importantcontacts?type=`, `POST`, `DELETE /{id}`) + `IImportantContactService`/`ImportantContactService` + repo (`GetByUserAsync`/`ExistsAsync`/`GetByIdAndUserAsync`) + validator (Email→email hợp lệ, JiraAccount→accountId tự do). UNIQUE(UserId,Type,Identifier) → 409 trùng; owner-only → 404. Unit test: `ImportantContactServiceTests` (6), `CreateImportantContactRequestValidatorTests` (5). **Notification type cho Jira: bỏ qua** (optional + chưa có nguồn sync-event Jira → tránh dead code). |

**Phối hợp:** 54 mở đường (Integration + OAuth + cloudId) cho tất cả. 57 tái dùng `IWriteBackGuard` của SCRUM-38 (Jira không có HTTP ETag → dùng `fields.updated` làm version-token lưu trong `Items.ETag`). 56 cần 59 (metadata để chọn project/issue-type/priority khi tạo). Khác Gmail: nội dung Jira (summary/description) **sửa được**, không immutable.

---

## Auth overhaul — cookie + refresh/Redis + OTP (SCRUM-62→64)

> ⚠️ **Phát sinh ngoài board (yêu cầu owner 2026-06-30), VƯỢT SCOPE SCRUM-42, ĐẢO nhiều quyết định nền tảng auth** (xem CHANGELOG mục [2026-06-30]). 62/63/64 **nay là ticket Jira thật** (assignee Lộc). Thứ tự phụ thuộc 62 → 63 → 64. **Board:** 62 Done, 63 To Do, 64 In Progress (status board lag — **code 64 đã xong**). Code 62/63 xong trên nhánh (`feat/SCRUM-62/63...`); **64 đã đổi hướng sang Email/Resend + code xong** trên nhánh `fix/login-ux` (xem CHANGELOG [2026-07-16]). **Báo team trước khi merge** vì đụng auth chung.

| Ticket | Việc | Assignee | Dependency | Status |
|---|---|---|---|---|
| SCRUM-62 | Access token → **HttpOnly cookie** + CSRF (BE Set-Cookie + đọc JWT từ cookie; FE bỏ localStorage, withCredentials, CSRF header) | — | — | ✅ Done (nhánh `feat/SCRUM-62-httponly-cookie-auth`) — BE: `AuthCookieService` set cookie `wh_access` (HttpOnly) + `wh_csrf` (double-submit); `AuthController` trả `AuthResultDto` (bỏ token khỏi body) + logout xoá cookie; JwtBearer `OnMessageReceived` đọc token từ cookie (fallback Bearer cho Swagger/Postman); `CsrfMiddleware` bắt header `X-CSRF-Token` trên request mutating có cookie; CORS opt-in `Cors:AllowedOrigins` + `AllowCredentials` (prod), `Auth:CrossSiteCookies` cho SameSite=None. FE: `api.ts` `withCredentials` + interceptor gắn CSRF header, bỏ `tokenStore`/Bearer; `AuthContext.login(user)` (không nhận token); `/auth/me` luôn gọi (cookie quyết định). Build BE + FE pass. |
| SCRUM-63 | **Refresh token + Redis** (rotation, `/auth/refresh`, logout stateful, docker-compose `wh-redis`, `AddStackExchangeRedisCache`; FE auto-refresh single-flight) | — | 62 | ✅ Done (nhánh `feat/SCRUM-63-refresh-token-redis`) — BE: `IRefreshTokenService`/`RefreshTokenService` (JWT+`jti`, Redis `refresh:{jti}` + `refreshfam:{fam}`, rotation, reuse→revoke family, inactive user→reject); `AuthCookieService.IssueRefreshCookie` cookie `wh_refresh` (HttpOnly, Path=/api/auth/refresh) + ClearAuthCookies xoá cả refresh; `AuthController` issue refresh khi login/register/google + `POST /api/auth/refresh` (rotate) + logout revoke; `AddStackExchangeRedisCache` (fallback in-memory + warning nếu thiếu `ConnectionStrings:Redis`); `docker-compose.yml` (`wh-redis` + `wh-sqlserver`); access TTL 15' (`Jwt:ExpiresIn=900`), refresh 7d (`Jwt:RefreshExpiresIn`). FE: `api.ts` interceptor 401 → `/auth/refresh` single-flight → retry, fail → /login. Test: `RefreshTokenServiceTests` (8). Build BE+FE pass, 223 test pass. |
| SCRUM-64 | **OTP đăng ký qua Email (Resend)** (rename `PhoneVerified`→`EmailVerified` + drop `Phone` + migration, sender hệ thống Resend, `/auth/send-otp` + `/auth/verify-otp`, login chặn chưa verify; FE bỏ field SĐT + màn OTP theo email) | Lộc | 63 | ✅ Done (nhánh `fix/login-ux`) — **Đổi hướng SMS/Firebase → Email/Resend** (xem CHANGELOG [2026-07-16]); đã merge develop + gỡ sạch Firebase. BE: `ISystemEmailSender` (tên tránh nhầm `IGmailGateway`) + `ResendEmailSender` (HTTP `api.resend.com/emails`) + dev fallback `LogEmailSender` (log text body); `OtpService` đổi kênh SMS→Email, GIỮ NGUYÊN logic Redis (HMAC hash, cooldown atomic `SET NX`, TTL 5', tối đa 5 lần sai); migration `RenamePhoneVerifiedToEmailVerified` dùng `RenameColumn` (giữ trạng thái verify) + drop `Phone`; `RegisterAsync` bọc try/catch gửi OTP (provider lỗi không kẹt user, vẫn 201); rate limit theo IP (policy `otp`, 5 req/phút → 429) cho `/auth/register` + `/auth/send-otp`. Register tạo user `EmailVerified=false` (trả `RegisterResult`, KHÔNG phát token); `POST /auth/verify-otp` (mã 6 số → `EmailVerified=true` → đăng nhập); login chặn chưa verify → 403 `EMAIL_NOT_VERIFIED`. FE: bỏ field SĐT + PHONE_RE, màn OTP theo email, i18n VI+EN. Test: `OtpServiceTests` + `GoogleSignInTests` cập nhật; Postman collection + README theo flow mới. Build BE + 338 test pass, FE tsc/lint/build pass. ⏳ Vận hành còn lại: verify domain `workspace-hub.space` trên Resend (thêm DNS SPF/DKIM/MX ở Namecheap) — chưa verify thì dev dùng `LogEmailSender` (OTP ra log) hoặc `onboarding@resend.dev`. |

**Phối hợp / lưu ý:**
- 62 đổi **hợp đồng response auth** (bỏ `accessToken` khỏi body) → mọi nơi FE đọc token phải sửa; báo Dũng (FE) + Lộc (auth).
- 63 đổi `AddDistributedMemoryCache` → Redis: ảnh hưởng cả `ConnectionsService` (đang dùng `IDistributedCache` cho OAuth state) — verify state OAuth vẫn chạy trên Redis.
- 64 migration `EmailVerified` giữ default **true** cho user cũ (không phá login hiện có); chỉ user đăng ký mới sau migration mới phải verify. Rename dùng `RenameColumn` (đã verify) — KHÔNG được để thành `Drop+Add` vì sẽ set user chưa-verify thành `true` (bypass cổng verify).
- Dev fallback `LogEmailSender` (OTP ra log) khi chưa cấu hình `Email:Resend:*`. Prod **bắt buộc verify domain** trên Resend (test mode chỉ gửi tới chủ tài khoản Resend).

---

## UI polish + Theme + i18n + Profile (đề xuất — CHƯA có trên Jira, số tạm SCRUM-81, 74, 75)

> Phát sinh từ owner 2026-07-07: review UI (fix lệch tông màu brand blue↔indigo, Header nền, avatar) + **theme Sáng/Tối**, **song ngữ VI/EN** (đổi KHÔNG remount), **trang Profile** (chuẩn bị avatar/R2). 4 task này **chưa có trên board** — draft đầy đủ + CSV import ở `docs/tickets-ui-i18n-theme-profile.md`; quyết định kỹ thuật: `docs/CHANGELOG.md` [2026-07-07]. **Theme Done** gộp theo CHANGELOG / SCRUM-50 — **không dùng SCRUM-76** (số 76 chốt cho Contacts write-back).

| Ticket (tạm) | Việc | Labels | Status |
|---|---|---|---|
| SCRUM-81 | FE: Song ngữ VI/EN (i18n tự viết, `useI18n().t()`, đổi lang KHÔNG remount) | frontend, i18n | 🔄 In Progress — hạ tầng + shell/auth/profile/toolbar + nhãn chính Inbox/Kanban/Integrations xong; ScheduledEmails/SendEmail/Admin/ItemDetail/modals mở rộng dần |
| SCRUM-74 | FE: Trang Hồ sơ người dùng `/profile` (info + tuỳ chọn theme/ngôn ngữ; link Header+Sidebar) | frontend, profile | ✅ Done — vùng avatar chừa chỗ cho SCRUM-75 |
| SCRUM-75 | Profile CRUD đầy đủ: avatar upload/xoá + lưu trữ **Cloudflare R2**, đổi họ tên, đổi mật khẩu | backend, frontend, storage, r2 | ✅ Done — `IFileStorageService`/`R2FileStorageService` (AWSSDK.S3, S3-compatible; đã vá 2 lỗi tương thích SigV4 streaming/checksum trailer riêng của R2), `UserProfileService` (avatar validate JPEG/PNG/WebP ≤5MB; `UpdateProfileAsync` đổi FullName; `ChangePasswordAsync` verify BCrypt, chặn tài khoản Google-only), `UsersController` (`PATCH /api/users/me`, `POST /api/users/me/change-password`, `POST/DELETE /api/users/me/avatar`). `UserDto` thêm `avatarUrl` + `authProvider`. FE: `usersApi` đủ 4 action, ProfilePage sửa tên inline + đổi mật khẩu (ẩn nếu Google-only) + lightbox xem avatar cỡ lớn (click ảnh, Esc/click nền để đóng), Header/Sidebar hiện avatar. `Users.AvatarUrl` đã có sẵn từ InitialCreate — KHÔNG cần migration mới. |
| *(Theme Sáng/Tối)* | FE: Theme (toggle, persist `wh-theme`, class `.dark`, no remount, chống FOUC) | frontend, theme | ✅ Done — xem CHANGELOG [2026-07-07]; **gộp SCRUM-50**, không dùng key Jira riêng |

---

Các ý tưởng dưới đây **không nằm trong Jira hiện tại** — chỉ là định hướng tương lai, đừng code, đừng gán số SCRUM (số 39–46 nay đã dùng cho việc khác; Jira giờ là 54→60):

- Webhook / push realtime (Gmail watch + Pub/Sub, Calendar/Drive/Jira watch) thay sync on-demand.
- AI workflow.

---

## Friend system nội bộ app & Folder Sharing (2026-07-10 — VÀO scope)

> Owner quyết định 2026-07-10: **bỏ hướng Google Contacts/People API (PR #100)** — bạn bè chỉ có ý nghĩa trong app, không dùng bên thứ 3. Nền tảng ĐÃ CODE XONG trên nhánh `feature/friends-system` (chi tiết: CHANGELOG [2026-07-10], DATABASE.md §Friendships, API.md §Friends).

| Việc | Status |
|---|---|
| BE: Friendships + FriendInvites (migration `AddFriendSystem`), FriendService (kết bạn theo email, invite link, consume khi đăng ký, notification FriendRequest/FriendAccepted), `/api/friends/*`, unit test | ✅ Done |
| FE: trang `/friends` (kết bạn, accept/decline, bạn thân ⭐, copy link mời), sidebar, RegisterPage banner+prefill từ `?inviteToken=` | ✅ Done |
| BE+FE: Folder Sharing: chia sẻ folder cho bạn bè theo role (Viewer/Editor), quản lý quyền truy cập, chấp nhận/từ chối lời mời và rời thư mục | ✅ Done |
| Review + hoàn thiện Folder Sharing (2026-07-18, Lộc) — **BE phân quyền:** fix desync xoá thread email, Editor thao tác Jira comment/attachment, Editor mark important, Editor reply/gửi email trong folder share (helper `CanAccessDraftAsync`), message 403 dễ hiểu cho Viewer, thêm `ItemResponse.isOwner` + `EmailThreadResponse.ownerEmail`. **FE:** i18n hoá dialog chia sẻ (31 key `share.*`), `FriendMultiSelect` (search + chọn nhiều + chọn nhanh bạn thân), `Select` render qua portal (hết bị dialog cắt), 403 không đá sang `/integrations`, ẩn nút "mở trên provider" với người được share. **Bug ngoài phạm vi:** sync không còn ghi đè cờ quan trọng (Jira/Drive/Gmail), reply/send không mất attachment, bỏ auto-save nháp gây nhiễu. Build 0 warning, 370/370 test pass. Chi tiết: `docs/FOLDER-SHARING-REVIEW.md`, CHANGELOG [2026-07-18] | ✅ Done |
| Tương lai: tạo event cùng bạn, nhóm bạn tuỳ biến | ⏳ chưa làm |
