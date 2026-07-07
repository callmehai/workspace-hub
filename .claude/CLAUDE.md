# Workspace Hub — Context cho Claude Code

File này load tự động vào mọi Claude session khi mở repo. Bổ trợ cho `CLAUDE.md` (root) — root nói scope/phase/quy ước, file này nói cấu trúc repo thật, lệnh, và gotchas.

---

## TL;DR — project là gì

**Đồ án PRN232 Fullstack ASP.NET, nhóm 6 người, 60/40 BE-FE.**
App aggregator: gom **Gmail / Google Calendar / Drive + Jira** về 1 nơi.
Concept: `Item` (Email/Event/File/Note/**Ticket**) → kéo vào `Folder` (context) → Kanban 3 cột (Inbox/Doing/Done).
Đã xong phần lớn: **mô hình B** + **OAuth per-service** + **Google Sign-In** + **write-back Google** (Email/Event/File, ETag→409) + **scheduled email** (đã deploy prod) + **FE đầy đủ** (Inbox/Kanban/admin/tags) + **Jira/Atlassian tích hợp** (BE OAuth/sync/CRUD/metadata + FE Ticket write-back). Vài ticket "In Review" nhưng code đã merge.

Scope/phase chi tiết: đọc `CLAUDE.md` root. Status ticket: `docs/SPRINTS.md` (⚠️ dòng "Phase Jira chưa code" trong SPRINTS.md đã stale — Jira đã code). Deploy/ops: `docs/DEPLOY.md`.

Tech stack:
- BE: **ASP.NET Core 8** + EF Core 8 (SQL Server) + JWT + FluentValidation + Data Protection + Swagger
- FE: **Vite + React + TypeScript** + Tailwind + React Router + TanStack Query + axios + react-hot-toast

---

## Cấu trúc repo

```
/
├── CLAUDE.md                    # Context gốc (scope, phase, quy ước)
├── docs/                        # DATABASE / API / SPRINTS / CONVENTIONS / SETUP / CHANGELOG
├── backend/                     # ASP.NET Core 8 Web API — solution 4 project
│   ├── WorkspaceHub.sln
│   ├── src/
│   │   ├── WorkspaceHub.Domain/          # Entities (User, Connection, Item, ...), Enums
│   │   ├── WorkspaceHub.Application/     # Services, DTOs, interfaces, OAuth strategies, Validators
│   │   ├── WorkspaceHub.Infrastructure/  # AppDbContext, EF config, Repositories, Data/Migrations
│   │   └── WorkspaceHub.Api/             # Controllers, Program.cs, Middleware, appsettings
│   └── tests/WorkspaceHub.Tests/
└── frontend/                    # Vite React TS
    └── src/{pages, components, layouts, context, hooks, lib, types, router.tsx}
```

---

## Conventions (tóm tắt — đầy đủ ở docs/CONVENTIONS.md)

### Backend
- Layered: Controller → Service → Repository → DbContext. DTO tách Entity. DI mọi thứ.
- Namespace `WorkspaceHub.{Domain|Application|Infrastructure|Api}.*`. Route `/api/...` lowercase.
- Guid PK, enum lưu string, UTC `datetime2`, JSON `nvarchar(max)`.
- **Mô hình B:** mỗi service = 1 row `Connections`. KHÔNG cột Scopes/Permission — scope suy từ ServiceType trong code. Đừng tạo lại OAuthConnections/ServiceConnections cũ.
- Migration mới mỗi thay đổi schema, KHÔNG sửa migration đã commit. Hiện có 8: `InitialCreate`, `UsersMultiAuth`, `ModelBConnections`, `RemoveClientCredentialsFromIntegration`, `AddAtlassianIntegrationSeed`, `AddCreatedAtToScheduledEmails`, `AddUserPhoneOtp`, `AddTagUserNameUniqueIndex` (SCRUM-70 fix review — unique `Tags(UserId,Name)`).
- DB dev: SQL Server chạy Docker container `wh-sqlserver` (xem docs/SETUP.md). KHÔNG phải SQLite/Postgres.

### Frontend
- Page trong `src/pages/` hậu tố `Page`; component tái dùng trong `src/components/`.
- Server state: TanStack Query. KHÔNG `useEffect + fetch` thủ công. API qua axios instance (JWT interceptor).
- Style: Tailwind utility. Toast: react-hot-toast.

### Git
- Default branch `develop`, production `main`. Feature branch `feature/SCRUM-x-mo-ta`.
- Commit prefix `feat:` / `fix:` / `refactor:` / `docs:` / `chore:` (gắn mã ticket khi có: `SCRUM-37: ...`).
- PR vào develop, ≥1 approval.

---

## Status hiện tại (2026-07-07 — chi tiết: docs/SPRINTS.md)

- ✅ **Done (BE + FE):** nền tảng/auth/OAuth (5–14, 18–23, 32–36), exception middleware (24), sync on-demand Gmail/Calendar/Drive (15–17), write-back Google + ETag→409 (37 In Review / 38), scheduled email + cron (30/31), admin (toggle integration 40, users/stats 23, dashboard #71), OTP đăng ký Twilio (64), refresh token Redis (63), **Tags** (70), **Jira/Atlassian tích hợp** (54–59: OAuth/sync/CRUD/transition/metadata BE + FE Ticket write-back 46).
- ✅ **Đã deploy production** — AWS Lightsail + CI/CD (merge `develop` → auto-deploy). Xem `docs/DEPLOY.md`.
- ⏳ Còn lại: SCRUM-60 (Jira ImportantContacts + notification — xác nhận board), thêm redirect URI prod vào Google Console cho Google Sign-In, các ticket lẻ — xem `docs/SPRINTS.md`/board.

---

## Deployment & CI/CD (production) — chi tiết: `docs/DEPLOY.md`

App live: **https://app.workspace-hub.space** — AWS Lightsail (2GB, Singapore), Docker Compose (`docker-compose.prod.yml`): api (.NET 8) + web (Caddy auto-HTTPS, proxy `/api`) + mssql (cap RAM 1GB) + redis.

- **CD:** merge/push `develop` → GitHub Actions (`deploy.yml`) SSH vào Lightsail `git reset --hard origin/develop` + `docker compose up -d --build`. Cần 4 repo secrets `DEPLOY_HOST/USER/APP_DIR/SSH_KEY`.
- **CI:** mọi PR/push develop/main (`ci.yml`) → BE `dotnet build`+`test`, FE `npm install`+`lint`+`build` (`tsc`).
- **Redeploy tay:** SSH → `cd ~/workspace-hub && git pull && docker compose -f docker-compose.prod.yml up -d --build`.
- **Log / OTP:** `docker compose -f docker-compose.prod.yml logs -f api`; OTP đăng ký log console (Twilio để trống).
- **DB prod:** DBeaver qua SSH tunnel (docs/DEPLOY.md §6). **Secret prod ở `.env` trên server — KHÔNG commit.**
- **Gotcha:** FE Dockerfile/CI dùng `npm install` KHÔNG `npm ci` (lockfile đa nền tảng Vite 8/rolldown); Caddyfile KHÔNG global `email` (rỗng làm crash); dual-stack `curl -4`.

---

## Lệnh hay dùng

### Backend
```bash
cd backend
dotnet build && dotnet test
dotnet run --project src/WorkspaceHub.Api          # http://localhost:5118/swagger

# Migration (luôn kèm --startup-project, xem gotcha bên dưới)
dotnet ef migrations add <Name> --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api
dotnet ef database update       --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api
```

### Frontend
```bash
cd frontend
npm run dev                      # http://localhost:5173
npm run build && npm run lint
```

---

## Gotchas

1. **`dotnet ef` không có `--startup-project`** → design-time factory không thấy appsettings của Api, rơi về fallback `Trusted_Connection` → lỗi Kerberos trên macOS. Fix: luôn dùng `--startup-project src/WorkspaceHub.Api` hoặc set env `WORKSPACEHUB_CONNECTION`.
2. **Máy không có .NET 8 SDK**, dùng .NET 10 SDK build target `net8.0` — works fine (SDK forward-compatible, có `global.json`).
3. **SQL Server multiple cascade path:** FK `Items.ConnectionId` / `ScheduledEmails.ConnectionId` để NoAction ở DB; service layer set NULL/dọn trước khi xoá Connection.
4. **OAuth credentials:** đọc từ config section `OAuth:{provider}:ClientId/ClientSecret` (appsettings / user-secrets / env var `OAuth__google__ClientId`) — dùng chung cho dev lẫn prod. KHÔNG còn section `Dev:` và KHÔNG lưu credentials trong DB (2 cột `Integrations.ClientId/SecretEncrypted` đã drop ở migration `RemoveClientCredentialsFromIntegration`, SCRUM-47).
5. **Static plan cũ đã xoá** (index/prototype/timeline.html + netlify.toml — plan theo scope CŨ Nango/Outlook/Telegram). Spec hiện hành là `CLAUDE.md` + `docs/`. Site Netlify cũ nếu còn sống thì là bản outdated.

---

## Khi user yêu cầu code

- Bám phase hiện tại (CLAUDE.md root). Webhook realtime (ngoài scope) → hỏi trước; Jira đã có nền tảng, mở rộng thì đối chiếu code.
- Đối chiếu `docs/DATABASE.md` + `docs/API.md` trước khi tạo entity/endpoint.
- BE: Controller → Service → Repository. FE: page → component → hook → axios.
- Test build sau mỗi nhóm thay đổi lớn (`dotnet build` + `dotnet test` BE, `npm run build` FE).
- **Sau khi xong task code: cập nhật các file .md liên quan để phản ánh status mới của ticket/feature** (tối thiểu `docs/SPRINTS.md`; schema → DATABASE.md; endpoint → API.md; quyết định lớn → CHANGELOG.md).

## Mở rộng skill

Nếu cần skill chuyên dụng, tạo file `.claude/skills/<name>.md` với format:
```markdown
---
description: "ngữ cảnh khi nên invoke skill này"
---
[nội dung skill]
```
