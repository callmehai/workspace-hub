# Sprints & Tickets — Workspace Hub

> Cập nhật 2026-06-21: đồng bộ lại toàn bộ theo Jira (export mới). Backlog Webhook/Jira cũ (SCRUM-39→46) đã **bị bỏ** khỏi Jira — số đó nay là việc khác (xem bảng). Sprint hiện hành: **Sprint 3**. Lịch sử quyết định: CHANGELOG.md.
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
> Webhook & Jira/Atlassian **không còn ticket** (ngoài scope đồ án, không nằm trong Jira nữa).

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

---

## Ngoài scope (KHÔNG còn ticket Jira)

Các ý tưởng dưới đây **không nằm trong Jira hiện tại** — chỉ là định hướng tương lai, đừng code, đừng gán số SCRUM (số 39–46 nay đã dùng cho việc khác):

- Webhook / push realtime (Gmail watch + Pub/Sub, Calendar/Drive watch) thay sync on-demand.
- Jira / Atlassian integration (OAuth cloudId, sync issue → Item(Ticket), write-back, webhook).
- Social / friend system, AI workflow.
