# Workspace Hub — Context cho Claude Code

File này load tự động vào mọi Claude session khi mở repo. Bổ trợ cho `CLAUDE.md` (root) — root nói scope/phase/quy ước, file này nói cấu trúc repo thật, lệnh, và gotchas.

---

## TL;DR — project là gì

**Đồ án PRN232 Fullstack ASP.NET, nhóm 6 người, 60/40 BE-FE.**
App aggregator: gom **Gmail / Google Calendar / Drive** về 1 nơi (Jira ở phase sau).
Concept: `Item` (Email/Event/File/Note) → kéo vào `Folder` (context) → Kanban 3 cột (Inbox/Doing/Done).
Phase hiện tại (Sprint 4): **mô hình B** (mỗi service 1 Connection, token riêng — ✅ đã migrate, SCRUM-34) + **write-back 2 chiều lên Google** (⏳ SCRUM-35→38) + **Google Sign-In** (✅).

Scope/phase chi tiết: đọc `CLAUDE.md` root. Status ticket: `docs/SPRINTS.md` (đồng bộ Jira).

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
    └── src/{pages, components, layouts, context, lib, services, types, router.tsx}
```

---

## Conventions (tóm tắt — đầy đủ ở docs/CONVENTIONS.md)

### Backend
- Layered: Controller → Service → Repository → DbContext. DTO tách Entity. DI mọi thứ.
- Namespace `WorkspaceHub.{Domain|Application|Infrastructure|Api}.*`. Route `/api/...` lowercase.
- Guid PK, enum lưu string, UTC `datetime2`, JSON `nvarchar(max)`.
- **Mô hình B:** mỗi service = 1 row `Connections`. KHÔNG cột Scopes/Permission — scope suy từ ServiceType trong code. Đừng tạo lại OAuthConnections/ServiceConnections cũ.
- Migration mới mỗi thay đổi schema, KHÔNG sửa migration đã commit. Hiện có 3: `InitialCreate`, `UsersMultiAuth`, `ModelBConnections`.
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

## Status hiện tại (2026-06-11 — chi tiết: docs/SPRINTS.md)

- ✅ Done: SCRUM-5→13, 18, 19, 21 (nền tảng, auth, OAuth start/callback, Folder CRUD, Items filter, FE setup) + SCRUM-32/33 (multi-auth + Google Sign-In) + **SCRUM-34** (migration mô hình B).
- ⏳ Kế tiếp: SCRUM-35/36 (OAuth per-service, Khánh) → SCRUM-37 (write-back, Vũ) + SCRUM-38 (conflict ETag, Lộc) → SCRUM-30/31 (scheduled email).
- ⏳ Còn nợ phase 1: SCRUM-14 (list/disconnect/refresh — viết lại theo Connections), SCRUM-22 (auth pages wire API), và các ticket 15–17, 20, 23–29.

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
4. **OAuth dev credentials:** `ConnectionsService` đọc plaintext `Dev:google:ClientId/ClientSecret` từ `appsettings.Development.json` (prod mới decrypt từ DB).
5. **Static plan cũ đã xoá** (index/prototype/timeline.html + netlify.toml — plan theo scope CŨ Nango/Outlook/Telegram). Spec hiện hành là `CLAUDE.md` + `docs/`. Site Netlify cũ nếu còn sống thì là bản outdated.

---

## Khi user yêu cầu code

- Bám phase hiện tại (CLAUDE.md root). Webhook/Jira → hỏi trước.
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
