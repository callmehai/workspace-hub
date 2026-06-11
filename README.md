# Workspace Hub

Đồ án Fullstack ASP.NET (PRN232) · Nhóm 6 người · 60% Backend / 40% Frontend.

App gom **Gmail · Google Calendar · Google Drive** về 1 nơi, quản lý theo Folder context + Kanban 3 cột, đồng bộ **2 chiều** (đọc bằng cron polling, ghi ngược lên Google khi user thao tác). Jira/webhook là phase sau.

> Context cho dev + Claude Code: đọc [`CLAUDE.md`](CLAUDE.md) trước, chi tiết trong [`docs/`](docs/).

---

## 📚 Tài liệu

| File | Nội dung |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Scope, phase hiện tại, quy ước nền tảng |
| [`docs/DATABASE.md`](docs/DATABASE.md) | Schema (mô hình B — mỗi service 1 Connection) |
| [`docs/API.md`](docs/API.md) | Endpoint, request/response, status code |
| [`docs/SPRINTS.md`](docs/SPRINTS.md) | Ticket + assignee + status (đồng bộ Jira) |
| [`docs/CONVENTIONS.md`](docs/CONVENTIONS.md) | Coding style, naming, git |
| [`docs/SETUP.md`](docs/SETUP.md) | Chạy local, env, migration, OAuth setup |
| [`docs/CHANGELOG.md`](docs/CHANGELOG.md) | Lịch sử quyết định thiết kế |

Plan/prototype cũ (deploy Netlify — tham khảo, **không phải spec hiện tại**):
plan https://workspace-hub-plan.netlify.app/ · prototype `/prototype.html` · timeline `/timeline.html`

---

## 📁 Cấu trúc thư mục

```
workspace-hub-plan/
├── index.html, prototype.html, timeline.html   # Doc/plan cũ (Netlify deploy)
├── CLAUDE.md, docs/                            # Tài liệu hiện hành
├── backend/                                    # ASP.NET Core 8 Web API
│   ├── WorkspaceHub.sln
│   ├── src/
│   │   ├── WorkspaceHub.Domain/          # Entities, Enums
│   │   ├── WorkspaceHub.Application/     # Services, DTOs, interfaces, OAuth, Validators
│   │   ├── WorkspaceHub.Infrastructure/  # DbContext, EF config, Repositories, Migrations
│   │   └── WorkspaceHub.Api/             # Controllers, Program.cs, appsettings
│   └── tests/WorkspaceHub.Tests/
└── frontend/                                   # Vite + React + TypeScript
    └── src/{pages, components, layouts, context, lib, services, types}
```

---

## 🛠 Yêu cầu môi trường

| Tool | Version |
|---|---|
| .NET SDK | 8.x (hoặc 10.x build target `net8.0` — có `global.json`) |
| SQL Server | 2022 (dev chạy Docker) |
| Node.js | ≥ 20.x |

---

## 🚀 Cách chạy local

Đầy đủ (Windows + macOS, Docker SQL Server, user-secrets, OAuth): xem [`docs/SETUP.md`](docs/SETUP.md).

```bash
# SQL Server dev (1 lần)
docker run -d --name wh-sqlserver -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD='Workspace#2026Dev' -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest

# Backend
cd backend
dotnet restore
dotnet ef database update --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api
dotnet run --project src/WorkspaceHub.Api    # http://localhost:5118/swagger

# Frontend (terminal khác)
cd frontend
npm install
npm run dev                                   # http://localhost:5173
```

Health check: `GET /api/health` → `{api:"ok", db:"ok"}`.

---

## 🗂 Tech stack

### Backend (.NET 8)
- ASP.NET Core 8 Web API — clean/layered 4 project (Domain/Application/Infrastructure/Api)
- EF Core 8 + **SQL Server** (dev + prod)
- JWT Bearer + BCrypt + Google Sign-In · Role string trên Users (Admin/User)
- ASP.NET Data Protection (mã hoá OAuth token)
- FluentValidation · Swagger
- OAuth Google trực tiếp (strategy pattern theo provider — không dùng Nango)

### Frontend
- Vite + React + TypeScript
- React Router · TanStack Query (server state) · axios (JWT interceptor)
- Tailwind CSS · react-hot-toast

---

## 👥 Team

| Tên | Vai trò |
|---|---|
| Hải | Lead — foundation, schema/migration, optimize |
| Lộc | Auth, conflict resolution, scheduled cron |
| Khánh | OAuth flow, scope |
| Vũ | Sync + write-back Google, scheduled email |
| Huy | Folders/Items/filter/Admin |
| Dũng | Frontend |

---

## 🌿 Git workflow

```
main      ← production
develop   ← branch tích hợp (default)
feature/* ← feature/SCRUM-x-mo-ta
```

1. Branch từ `develop` → code + commit (`feat:` / `fix:` / `refactor:` / `docs:` / `chore:`, gắn mã ticket khi có)
2. PR vào `develop`, ≥1 approve → merge

---

## 📋 Trạng thái (2026-06-11 — chi tiết & cập nhật: [`docs/SPRINTS.md`](docs/SPRINTS.md))

### ✅ Done
- Nền tảng: solution + clean architecture, EF schema + migrations, Data Protection, repo/branching (SCRUM-5→8)
- Auth: register/login BCrypt+JWT, middleware + `/api/auth/me`, role-based authorization (SCRUM-9→11)
- OAuth Google: start flow + callback + token encrypted (SCRUM-12/13)
- Workspace: Folder CRUD, Items list + filter + pagination + search (SCRUM-18/19)
- FE: setup routing/layout/protected route (SCRUM-21)
- **Sprint 4:** Users multi-auth + Google Sign-In (SCRUM-32/33), migration mô hình B — bảng `Connections`, `Items.ConnectionId` + `ETag` (SCRUM-34)

### ⏳ Đang tới
- SCRUM-35/36: OAuth per-service theo mô hình B (Khánh)
- SCRUM-37: write-back Gmail/Calendar/Drive (Vũ) + SCRUM-38: conflict ETag → 409 (Lộc)
- SCRUM-30/31: scheduled email theo Connections
- Nợ phase 1: SCRUM-14 (viết lại theo Connections), SCRUM-22, các ticket còn lại

---

## ❓ Gặp vấn đề?

```bash
# BE không build
cd backend && dotnet clean && dotnet restore --force && dotnet build

# FE lỗi module
cd frontend && rm -rf node_modules package-lock.json && npm install

# Port bị chiếm
lsof -ti:5118 | xargs kill -9
lsof -ti:5173 | xargs kill -9

# dotnet ef lỗi Kerberos / không thấy DB
# → thiếu --startup-project src/WorkspaceHub.Api (xem docs/SETUP.md mục gotcha)
```

Repo: https://github.com/callmehai/workspace-hub-plan · PR review: tag ae trong nhóm.
