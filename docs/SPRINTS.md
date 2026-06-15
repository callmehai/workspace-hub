# Sprints & Tickets — Workspace Hub

> Cập nhật 2026-06-11: status đồng bộ theo Jira. Đợt hiện tại (Sprint 4): mô hình B + write-back + Google Sign-In (SCRUM-32→38). Lịch sử quyết định: CHANGELOG.md.
>
> **Quy tắc:** sau khi hoàn thành task code nào, cập nhật status ticket đó trong file này (và các .md liên quan).

## Team
| Tên | Vai trò |
|---|---|
| Hải | Lead — foundation, schema/migration, optimize |
| Lộc | Auth, conflict resolution, scheduled cron |
| Khánh | OAuth flow, scope |
| Vũ | Sync + write-back Google, scheduled email |
| Huy | Folders/Items/filter/Admin |
| Dũng | Frontend |

---

## Phase 1 — Nền tảng (status thật theo Jira 2026-06-11)

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
| SCRUM-13 | OAuth callback + lưu token encrypted | Khánh | ✅ Done |
| SCRUM-14 | List/disconnect/refresh connection | — | ⏳ Pending — **viết lại theo mô hình B** (Connections, không còn ServiceConnections) |
| SCRUM-18 | Folder CRUD | Huy | ✅ Done |
| SCRUM-19 | Items list + filter + pagination + search | Huy | ✅ Done |
| SCRUM-21 | Frontend setup: routing, layout, protected route | Dũng | ✅ Done |
| SCRUM-20 | Kanban Status + Note CRUD + ItemFolders (Backend & UI) | Antigravity | SCRUM-18, 19 | ✅ Done |
| SCRUM-22 | Auth pages connected to API | Dũng | ⏳ Pending |

> Các ticket phase 1 còn lại (15–17, 23–29: sync Gmail, Admin dashboard, ...) chưa done — xem Jira. Bản cũ của file này ghi "SCRUM-5→29 đã done" là **sai**, đã sửa theo Jira.

## Sprint 4 (đợt hiện tại) — Mô hình B + Write-back + Google Sign-In

| Ticket | Việc | Assignee | Dependency | Status |
|---|---|---|---|---|
| SCRUM-32 | Migration: Users multi-auth (PasswordHash null, GoogleSub, AuthProvider) | Lộc | SCRUM-6 | ✅ Done |
| SCRUM-33 | Google Sign-In (đăng nhập Google, auto-link) | Lộc | 32, 9 | ✅ Done |
| SCRUM-34 | Migration mô hình B: gộp Connections, Items.ConnectionId + ETag | Hải | SCRUM-6 | ✅ Done 2026-06-11 — migration `ModelBConnections`, đã apply DB dev |
| SCRUM-35 | OAuth start flow theo mô hình B (mỗi service 1 connection) | Khánh | 34 | ✅ Done 2026-06-12 — `InitiateConnectionAsync` nhận `serviceType`, cache vào state; `ProviderStrategyContext` + `BuildAuthUrlAsync` per-service |
| SCRUM-36 | OAuth callback theo mô hình B (per-service) + scope read-write | Khánh | 35 | ✅ Done 2026-06-12 — `CompleteConnectionAsync` đọc `serviceType` từ state, `ValidateAndExtract` chỉ check scope của service đó; scope đã là read-write (gmail.modify+send, calendar, drive) |
| SCRUM-37 | Write-back Google: Email + Event + File (PATCH/POST/DELETE items) | Vũ | 36 | ⏳ Not started |
| SCRUM-38 | Conflict detection (ETag → 409) cho mọi write-back | Lộc | 37 — **chốt interface `IWriteBackGuard` với Vũ trước khi code** | ⏳ Not started |
| SCRUM-30 | Scheduled email tạo/list/cancel (đổi ConnectionId) | Vũ | 34, 36 | ⏳ Not started |
| SCRUM-31 | Cron process-scheduled (token từ Connections) | Lộc | 30, 37 | ⏳ Not started |

**Execution order:** 34 ✅ → 35 → 36 (Khánh) → 37 (Vũ) và 38 (Lộc) song song → rồi 30/31.
**Phối hợp:** Vũ (37) + Lộc (38) thống nhất interface `IWriteBackGuard` trước khi code.
**Lưu ý sau SCRUM-34:** response của `POST /api/connections/oauth/callback` đã đổi shape (trả list connections) — xem API.md; FE (Dũng) cập nhật khi wire.
**Huy** đợt này: cập nhật GET /api/items trả ETag (phục vụ 37/38) + viết lại SCRUM-14 theo Connections, hoặc test write-back.

## Cleanup / tech-debt (chốt 2026-06-12 — xem CHANGELOG)

| Ticket | Việc | Assignee | Dependency | Status |
|---|---|---|---|---|
| SCRUM-47* | Bỏ DB credentials cho Integrations: drop 2 cột encrypted, xoá PUT /credentials, đổi section config `Dev:` → `OAuth:` | đề xuất Hải (migration) | làm SAU SCRUM-36 (cùng đụng ConnectionsService với Khánh) | ⏳ Chưa tạo Jira |
| SCRUM-48* | Admin toggle integration: PATCH /api/admin/integrations/{key}/enable | Khánh | — | ✅ Done 2026-06-15 — `AdminIntegrationsController`, `ToggleIntegrationAsync`, validator + DTOs |

\* Số ticket tạm — sửa lại theo số Jira cấp khi tạo issue.

## Phase sau — BACKLOG, CHƯA LÀM (đừng code)
| Ticket | Việc |
|---|---|
| SCRUM-39 | Webhook Gmail (watch + Pub/Sub) thay polling đọc |
| SCRUM-40 | Webhook Calendar (events.watch) + renew |
| SCRUM-41 | Bảng WebhookChannels + cron renew |
| SCRUM-42 | Jira: Integration Atlassian + OAuth (cloudId) |
| SCRUM-43 | Jira: sync issue → Item(Ticket) |
| SCRUM-44 | Jira: write-back (transition/assign/comment) |
| SCRUM-45 | Jira webhook (issue created/updated) |
| SCRUM-46 | ImportantContacts: khôi phục JiraAccount |
