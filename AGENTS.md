# AGENTS.md — Workspace Hub

> Context cho AI coding agents (Cursor, Claude Code, …). Nguồn chi tiết: `docs/CLAUDE.md`, `CLAUDE.md`. **Luôn đọc file này trước**, rồi mở `docs/` theo task.

## Project

Web app gom **email / calendar / file / note** từ **Google (Gmail, Calendar, Drive)** về một nơi, quản lý theo **Folder** với **Kanban 3 cột** (Cần xem / Đang xử lý / Done). Có chia sẻ folder (Viewer), hẹn giờ gửi email, admin dashboard.

Đồ án môn học PRN232 — Fullstack ASP.NET (~60% BE / ~40% FE).

## Repo layout

```
backend/
  src/WorkspaceHub.Api/           # Controllers, middleware, Program.cs
  src/WorkspaceHub.Application/   # Services, DTOs, validators, interfaces
  src/WorkspaceHub.Domain/        # Entities, enums
  src/WorkspaceHub.Infrastructure/  # EF Core, repos, migrations, Google clients
  tests/WorkspaceHub.Tests/
frontend/                         # Vite + React + TypeScript + Tailwind
docs/                             # Source of truth — đọc trước khi code
  SPRINTS.md    ticket + status (đồng bộ Jira)
  API.md        endpoint spec
  DATABASE.md   schema
  CONVENTIONS.md coding style
  SETUP.md      chạy local, env, migration
  CHANGELOG.md  quyết định thiết kế
```

## Đọc tài liệu theo task

| Task | Đọc |
|---|---|
| Ticket / scope | `docs/SPRINTS.md` |
| Endpoint mới / sửa API | `docs/API.md` |
| Schema / migration | `docs/DATABASE.md`, `docs/CONVENTIONS.md` |
| Chạy local / env | `docs/SETUP.md`, `backend/README.md` |
| Lý do thiết kế | `docs/CHANGELOG.md` |

## Sprint hiện hành (Sprint 3)

Status đầy đủ: `docs/SPRINTS.md`.

**Đã xong (Sprint 1–2):** auth JWT + Google Sign-In, mô hình connection B, sync on-demand Gmail/Calendar/Drive, folders/items/kanban, admin users+stats, exception middleware, logging.

**Đang làm (Sprint 3):**
- SCRUM-37 write-back Google — **In Review**
- SCRUM-38 conflict ETag → 409 — To Do
- SCRUM-30 scheduled email API — **Done** (POST/GET OData/cancel)
- SCRUM-31 cron `process-scheduled` — To Do
- SCRUM-26→29 hardening/tests — To Do
- SCRUM-41→43 bắt đầu FE — To Do
- SCRUM-69 Google Contacts suggest (sync cache + OData `GET /api/Contacts?connectionId=`) — **Done**

**Sprint 4:** FE đầy đủ + deploy + nghiệm thu (SCRUM-44→53).

### Mô hình sync — đừng nhầm

- **Đọc = cron định kỳ (SCRUM-72, ~60s)** quét connection Active + sync theo ServiceType; **bổ sung** lazy sync khi mở list items (`ConnectionHealthChecker`, debounce). Không webhook trong MVP.
- **Contact cache (SCRUM-69):** kéo kèm mỗi lần sync Gmail — không gọi Google lúc gõ suggest.
- **Ghi = write-back synchronous** khi user thao tác (SCRUM-37).
- **Cron gửi scheduled email** (`POST /api/internal/process-scheduled`, SCRUM-31).

### NGOÀI scope — dừng và hỏi

Webhook realtime, Jira/Atlassian, social/AI workflow. Số SCRUM-39→46 **không** còn là webhook/Jira (xem SPRINTS.md).

## Tech stack

| Layer | Stack |
|---|---|
| Backend | ASP.NET Core 8, EF Core, SQL Server |
| Frontend | Vite, React, TypeScript, Tailwind, TanStack Query, axios |
| Auth | JWT Bearer + Google Sign-In (tách khỏi connect-sync) |
| Token user | Data Protection (`IDataProtectionProvider`) — không tự viết AES |

## Kiến trúc

**Controller → Service → Repository.** Controller mỏng; business logic trong Service; DTO tách Entity; async/await; DI everywhere.

- Base controller: `ApiControllerBase` (`CurrentUserId`, route `api/[controller]`).
- Validation: FluentValidation + `ValidateAndThrowAsync` → `ExceptionMiddleware` format 400.
- Lỗi: throw custom exception (`NotFoundException`, `BusinessRuleException`, …) — không tự format trong controller.

## Hai khái niệm dễ nhầm

1. **Google Sign-In** — đăng nhập app (openid/email/profile). Không tạo Connection.
2. **Connect service** — OAuth per-service (mô hình B): 1 row `Connections` / service, full scope hardcode theo `ServiceType`.

## Quy ước nền tảng (bắt buộc)

- `Guid` PK · enum lưu string · datetime **UTC** (`datetime2`)
- Không soft delete — dùng `IsArchived`; delete = xóa thật
- JSON → `nvarchar(max)` (ToJson, MetadataJson, …)
- `Items.ConnectionId` / `ScheduledEmails.ConnectionId` → **NoAction** FK; disconnect xử lý ở service layer
- Connections mô hình B: không cột `Scopes`, không `Permission`/`AccessLevel`

## Quy tắc cho agent

1. **Bám Sprint 3** — không implement webhook/Jira/phase sau trừ khi được yêu cầu rõ.
2. **Đọc code + docs hiện có** — reuse pattern (Folders, Items, Connections).
3. **Thay đổi tối thiểu** — không refactor ngoài scope task.
4. **Validate input** — status code đúng `docs/API.md` (422 business rule, 409 conflict, 502 provider).
5. **Không hardcode secret** — config/env/user-secrets.
6. **Sau khi hoàn thành task code:** cập nhật `docs/SPRINTS.md` (và API.md / DATABASE.md nếu liên quan). User có thể chỉ muốn SPRINTS.md — hỏi hoặc làm theo yêu cầu cụ thể.
7. **Không commit** trừ khi user yêu cầu.

## Lệnh thường dùng

```bash
# Backend
cd backend
dotnet restore
dotnet build
dotnet test
dotnet run --project src/WorkspaceHub.Api
dotnet ef database update --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api

# Frontend
cd frontend
npm install
npm run dev
```

Chi tiết env, Docker SQL Server, OAuth: `docs/SETUP.md`.

## Ghi chú implement nhanh

- **List lớn:** envelope `{ items, total, page, limit }` (Items, Admin users).
- **Folders / Scheduled emails GET:** OData `[EnableQuery]` in-memory — `$top`, `$skip`, `$filter`, `$orderby`, `$count=true`; response `{ value, @odata.count? }`.
- **Scheduled email `sendAt`:** bắt buộc UTC (`...Z`); validator `DateTimeKind.Utc`.
- **Write-back email:** không sửa nội dung Gmail — chỉ label/read/star/trash + gửi mới.
