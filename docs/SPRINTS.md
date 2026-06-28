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
| SCRUM-26 | Refactor services + clean architecture | Khánh | — | ⏳ To Do |
| SCRUM-27 | API testing + Postman collection | Huy | — | ⏳ To Do |
| SCRUM-28 | README backend + setup guide | Dũng | — | ⏳ To Do |
| SCRUM-29 | Unit test cho service chính | Hải | — | ⏳ To Do |
| SCRUM-30 | Scheduled email: tạo / list / cancel (theo Connections) | Vũ | 34, 36 | ⏳ To Do — `POST /api/scheduled-emails` (422 nếu Connection ≠ Gmail, 400 nếu sendAt quá khứ); list phân trang; cancel |
| SCRUM-31 | Cron process-scheduled: gửi qua Gmail (token từ Connections) | Hải | 30, 37 | ⏳ To Do |
| SCRUM-38 | Conflict resolution chung (ETag → 409) | Lộc | 37 | ✅ Done — `WriteBackGuard : IWriteBackGuard.EnsureNoConflict(storedEtag, providerEtag)` (chỉ so sánh, không I/O; lệch → `ConflictException` → 409 qua middleware; null/empty một bên → skip-check). Thay `TempWriteBackGuard` placeholder của Vũ, DI cập nhật. Test: `WriteBackGuardTests` (9 case). Log `LogWarning` khi conflict. |
| SCRUM-41 | FE: API layer (axios + JWT interceptor + TanStack Query) | Vũ | — | ⏳ To Do |
| SCRUM-42 | FE: Wire Login/Register vào API | Lộc | 41 | ⏳ To Do |
| SCRUM-43 | FE: Connections page (list/connect/disconnect per-service) | Khánh | 41 | ⏳ To Do |

**Phối hợp:** SCRUM-37 (Vũ) đang review; SCRUM-38 (Lộc) thống nhất interface `IWriteBackGuard` trước khi code. Scheduled email (30/31) viết theo mô hình B (`ScheduledEmails.ConnectionId` → Connection ServiceType=Gmail).

## Sprint 4 — Frontend đầy đủ + deploy + nghiệm thu

| Ticket | Việc | Assignee | Status |
|---|---|---|---|
| SCRUM-44 | FE: Inbox/Items view (list + filter + search + pagination) | Huy | ⏳ To Do |
| SCRUM-45 | FE: Kanban 3 cột (drag-drop) + Folder sidebar | Huy | ⏳ To Do |
| SCRUM-46 | FE: Write-back actions + xử lý 409 conflict | Vũ | ⏳ To Do |
| SCRUM-47 | FE: Scheduled email UI (compose/list/cancel) | Vũ | ⏳ To Do |
| SCRUM-48 | FE: Loading/error/toast chuẩn | Khánh | ⏳ To Do |
| SCRUM-49 | FE: Admin dashboard (users list + stats charts) | Huy | ⏳ To Do |
| SCRUM-50 | FE: Responsive polish + dashboard chart + dark mode | Dũng | ⏳ To Do |
| SCRUM-51 | Deploy: BE + DB + FE + OAuth prod config | Khánh | ⏳ To Do |
| SCRUM-52 | Finalize: Swagger + setup guide + E2E smoke test prod | Hải | ⏳ To Do |
| SCRUM-53 | Defense: slide + demo phần mỗi người | Lộc | ⏳ To Do |

## Phase Jira — Atlassian integration (SCRUM-54→60)

> **Cụm task BE cho tích hợp Jira (CRUD đầy đủ).** Đã lên kế hoạch + tạo ticket trên board (To Do, backlog) nhưng **chưa bắt đầu code** — current phase vẫn dừng ở SCRUM-38. Bắt đầu sau khi Sprint 3 (write-back Google + conflict) ổn định. Mô hình B áp dụng nguyên: Atlassian = 1 Integration, mỗi Jira account = 1 Connection (ServiceType=Jira). Quyết định + lưu ý kỹ thuật (ADF, version-token thay ETag): xem CHANGELOG.md.

| Ticket | Việc | Assignee | Dependency | Status |
|---|---|---|---|---|
| SCRUM-54 | Jira: Atlassian Integration + OAuth 3LO (cloudId), mô hình B | Khánh | — | ✅ Done — `JiraStrategy` + `JiraScopes` (scope `read:jira-work write:jira-work manage:jira-project read:jira-user read:me offline_access`); seed Integration `atlassian` (IsEnabled=false, migration `AddAtlassianIntegrationSeed`); `JiraTokenResponse`; DI đăng ký `JiraStrategy`; `CursorType.JqlUpdated` + `ItemType.Ticket` thêm vào enum. OAuth flow tái dùng `ConnectionsService` + `ProviderStrategyContext` hiện có (`/api/connections/oauth/start` + `/oauth/callback`). |
| SCRUM-55 | Jira: client + đọc/sync issue → Item(Type=Ticket) | Lộc | 54 | ✅ Done — `IJiraGateway` + `JiraGateway` (search JQL qua `POST /rest/api/3/search/jql`, phân trang `nextPageToken`, base URL theo cloudId); `IJiraSyncService` + `JiraSyncService` (sync on-demand, JQL `assignee/reporter = currentUser()` + `updated >= cursor`, dedupe UNIQUE(ConnectionId,ExternalId), cursor `JqlUpdated` = mốc `fields.updated` max); `IJiraItemMapper` + `JiraItemMapper` (issue→Item Type=Ticket, ETag=`fields.updated`, metadata đủ); `AdfConverter` (ADF→plain text cho Snippet); `IAtlassianTokenService` + `AtlassianTokenService` (refresh offline_access, rotate refresh token); wired vào `ConnectionSyncDispatcher` (`ServiceType.Jira`). Unit test: `JiraSyncServiceTests`, `JiraItemMapperTests`, `AdfConverterTests`. |
| SCRUM-56 | Jira: tạo issue (`POST /api/items/ticket`) | Lộc | 55, 59 | ✅ Done — `POST /api/items/ticket` (`CreateTicketRequest` + `CreateTicketRequestValidator`); `ItemWriteBackService.CreateTicketAsync` (validate connection Jira + Active + đúng owner, gọi `IJiraGateway.CreateIssueAsync` → `POST /rest/api/3/issue` → fetch lại qua `GetIssueAsync` → map `JiraItemMapper` → Item Type=Ticket local kèm issueKey/metadata); `AdfConverter.FromPlainText` (text→ADF cho description); Jira 400 (field/project/issueType sai) → 422, thiếu scope → 403, provider lỗi → 502. Unit test: `ItemWriteBackServiceCreateTicketTests`, `CreateTicketRequestValidatorTests`, `AdfConverterTests.FromPlainText`. *(Dependency SCRUM-59 chưa làm: chưa validate project/issueType trước khi gọi — để Jira reject → 422.)* |
| SCRUM-57 | Jira: write-back update (`PATCH /api/items/{id}`, Type=Ticket) qua `IWriteBackGuard` | Lộc | 55, 38 | ⏳ To Do |
| SCRUM-58 | Jira: xoá issue (`DELETE /api/items/{id}`, Type=Ticket) | Lộc | 55 | ⏳ To Do |
| SCRUM-59 | Jira: metadata helpers (projects / issue-types / transitions / assignable-users / priorities) | Lộc | 54 | ⏳ To Do |
| SCRUM-60 | Jira: ImportantContacts `JiraAccount` + Notification type (optional) | Lộc | 54 | ⏳ To Do |

**Phối hợp:** 54 mở đường (Integration + OAuth + cloudId) cho tất cả. 57 tái dùng `IWriteBackGuard` của SCRUM-38 (Jira không có HTTP ETag → dùng `fields.updated` làm version-token lưu trong `Items.ETag`). 56 cần 59 (metadata để chọn project/issue-type/priority khi tạo). Khác Gmail: nội dung Jira (summary/description) **sửa được**, không immutable.

---

## Ngoài scope (KHÔNG có ticket Jira)

Các ý tưởng dưới đây **không nằm trong Jira hiện tại** — chỉ là định hướng tương lai, đừng code, đừng gán số SCRUM (số 39–46 nay đã dùng cho việc khác; Jira giờ là 54→60):

- Webhook / push realtime (Gmail watch + Pub/Sub, Calendar/Drive/Jira watch) thay sync on-demand.
- Social / friend system, AI workflow.
