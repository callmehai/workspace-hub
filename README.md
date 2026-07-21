# Workspace Hub

> Gom **Gmail · Google Calendar · Google Drive** (và **Jira**) về một nơi, quản lý theo **Folder context** với giao diện **Kanban 3 cột**, đồng bộ **2 chiều**: đọc on-demand, ghi ngược lên provider ngay khi thao tác.

Đồ án Fullstack ASP.NET (**PRN232**, FPT University) — nhóm 6 người, ~60% Backend / 40% Frontend. Dự án đã hoàn thành & đóng; repo mở nguồn theo giấy phép [MIT](LICENSE) để tham khảo / fork.

<p>
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-512BD4">
  <img alt="React" src="https://img.shields.io/badge/React-18-61DAFB">
  <img alt="TypeScript" src="https://img.shields.io/badge/TypeScript-5-3178C6">
  <img alt="SQL Server" src="https://img.shields.io/badge/SQL%20Server-2022-CC2927">
  <img alt="License" src="https://img.shields.io/badge/license-MIT-green">
</p>

> ℹ️ Bản demo online đã tắt sau khi nghiệm thu. Chạy local theo [Quick Start](#-quick-start-5-phút) bên dưới.

---

## Tính năng

- **Aggregator 2 chiều** — kéo Email / Event / File / Ticket về app; thao tác trên app **ghi ngược** lên provider ngay (label/read/star/trash + gửi email, CRUD event, rename/trash file, CRUD Jira issue). Chống ghi đè bằng ETag → `409 Conflict`.
- **Folder context + Kanban** — kéo item vào folder (dự án/khách hàng/chủ đề), 3 cột *Cần xem / Đang xử lý / Done*, gắn **Tag**.
- **Chia sẻ Folder** cho bạn bè trong app (Viewer / Editor) — người được share đọc item qua connection của owner.
- **Hẹn giờ gửi email** — tạo lịch gửi, cron gửi qua Gmail.
- **Thông báo realtime** (SignalR) — item quan trọng mới, lời mời chia sẻ…
- **Bạn bè nội bộ** — kết bạn theo email, mời qua link.
- **Admin dashboard** — thống kê, khoá/mở user, bật/tắt integration.
- **Auth** — đăng ký OTP qua email, đăng nhập bằng mật khẩu hoặc **Google Sign-In**; access token trong **HttpOnly cookie + CSRF**, refresh token luân chuyển qua Redis.

Sơ đồ kiến trúc / ERD / class & sequence theo use case: **[`docs/diagrams/`](docs/diagrams/)** (nguồn `.drawio` + ảnh `.png`/`.svg`).

---

## Tech stack

| | |
|---|---|
| **Backend** | ASP.NET Core 8 Web API · Clean/Layered 4 project (Domain / Application / Infrastructure / Api) · EF Core 8 |
| **Database** | SQL Server 2022 · Redis (refresh token, OTP, OAuth state) |
| **Auth & bảo mật** | JWT (HttpOnly cookie) + CSRF + refresh rotation · Google Sign-In · **ASP.NET Data Protection** mã hoá OAuth token (không tự viết AES, không lưu key trong DB) |
| **Tích hợp** | Google OAuth (Gmail/Calendar/Drive) · Atlassian OAuth 3LO (Jira) · Resend (email OTP) · Cloudflare R2 (avatar) |
| **Realtime** | SignalR |
| **Frontend** | Vite + React 18 + TypeScript · React Router · TanStack Query · axios · Tailwind CSS · react-hot-toast |
| **Chất lượng** | xUnit + Moq + FluentAssertions (962 test) · Postman collection phủ 123/123 endpoint |

---

## 🚀 Quick Start (5 phút)

### Yêu cầu

| Tool | Version |
|---|---|
| .NET SDK | **8.x** (`global.json` pin 8.0.100; SDK 10 build `net8.0` cũng chạy) |
| Node.js | **≥ 20** |
| Docker | để chạy SQL Server + Redis (hoặc tự cài SQL Server 2022 + Redis) |

> Chạy tối thiểu **không cần** tài khoản Google/Jira/Resend: đăng ký/đăng nhập hoạt động, OTP in ra console, chỉ phần *connect Gmail/Calendar/Drive/Jira* mới cần OAuth credentials (xem [Bật tích hợp](#bật-tích-hợp-tuỳ-chọn)).

### 1. Clone + hạ tầng

```bash
git clone https://github.com/callmehai/workspace-hub-plan.git
cd workspace-hub-plan

cp .env.example .env                 # MSSQL_SA_PASSWORD cho SQL Server dev
docker compose up -d                 # SQL Server (1433) + Redis (6379)
```

### 2. Backend

```bash
cd backend
cp src/WorkspaceHub.Api/appsettings.Development.json.example \
   src/WorkspaceHub.Api/appsettings.Development.json
# File example đã trỏ sẵn tới SQL Server + Redis của docker-compose — chạy được ngay.

dotnet restore

dotnet ef database update \
  --project src/WorkspaceHub.Infrastructure \
  --startup-project src/WorkspaceHub.Api        # tạo DB + 19 migration

dotnet run --project src/WorkspaceHub.Api        # → http://localhost:5118/swagger
```

Kiểm tra: `GET http://localhost:5118/api/health` → `{ "status": "Healthy", "database": "Connected", ... }`.

> ⚠️ **Luôn kèm `--startup-project src/WorkspaceHub.Api`** khi chạy `dotnet ef`. Thiếu nó → design-time factory không thấy appsettings, rơi về fallback `Trusted_Connection` → lỗi Kerberos trên macOS/Linux.

### 3. Frontend (terminal khác)

```bash
cd frontend
npm install
npm run dev                          # → http://localhost:5173
```

Dev **không cần** set env — Vite proxy `/api` → `http://localhost:5118` (xem `vite.config.ts`). Xong: mở http://localhost:5173, đăng ký tài khoản (OTP hiện trong log của backend), đăng nhập.

---

## Bật tích hợp (tuỳ chọn)

Điền credential vào `backend/src/WorkspaceHub.Api/appsettings.Development.json` (đã gitignored) rồi restart backend. Không có credential thì app vẫn chạy, chỉ không connect được service tương ứng.

| Tính năng | Config cần điền | Lấy ở đâu |
|---|---|---|
| **Connect Gmail / Calendar / Drive** | `OAuth:google:ClientId` / `ClientSecret` | [Google Cloud Console](https://console.cloud.google.com) → bật Gmail/Calendar/Drive API → OAuth client (Web) |
| **Connect Jira** | `OAuth:atlassian:ClientId` / `ClientSecret` | [developer.atlassian.com](https://developer.atlassian.com) → OAuth 2.0 (3LO) app |
| **Gửi OTP email thật** | `Email:Resend:ApiKey` / `FromAddress` | [resend.com](https://resend.com) (để trống ⇒ OTP log ra console) |
| **Upload avatar** | `R2:*` | Cloudflare R2 bucket + API token |

**Redirect URI** phải khai báo trùng ở phía provider:
- Google connect / Jira: `http://localhost:5173/oauth/callback`
- Google Sign-In: `http://localhost:5173/auth/google/callback`

**Scope Google** (mô hình B — mỗi service xin riêng full scope của nó):

| Mục đích | Scope |
|---|---|
| Google Sign-In (đăng nhập app) | `openid email profile` — *không tạo Connection* |
| Connect Gmail | `gmail.modify gmail.send` — *không sửa nội dung (Gmail immutable)* |
| Connect Calendar | `calendar` — *CRUD event đầy đủ* |
| Connect Drive | `drive.file` — *rename / trash* |

> **Google Sign-In ≠ Connect service.** Sign-In chỉ đăng nhập app, **không** tạo Connection. Connect service mới cấp quyền đọc/ghi và tạo Connection. Disconnect service **không** làm logout.

Danh sách đầy đủ config + biến môi trường prod: [`docs/SETUP.md`](docs/SETUP.md) · [`deploy/.env.prod.example`](deploy/.env.prod.example).

---

## Cấu trúc repo

```
workspace-hub-plan/
├── README.md · LICENSE · CLAUDE.md         # tài liệu gốc + context
├── docker-compose.yml                      # hạ tầng dev: SQL Server + Redis
├── docker-compose.prod.yml · deploy/       # production (Docker + Caddy)
├── docs/                                    # SETUP · DATABASE · API · CONVENTIONS · CHANGELOG · diagrams/
├── backend/                                 # ASP.NET Core 8 — solution 4 project
│   ├── WorkspaceHub.sln
│   ├── src/
│   │   ├── WorkspaceHub.Domain/            # Entities, Enums — không phụ thuộc gì
│   │   ├── WorkspaceHub.Application/       # Services, DTOs, Interfaces, OAuth strategies, Validators
│   │   ├── WorkspaceHub.Infrastructure/    # AppDbContext, EF config, Repositories, Gateways, Migrations
│   │   └── WorkspaceHub.Api/               # Controllers, Program.cs, Middleware, Hubs, appsettings
│   ├── tests/WorkspaceHub.Tests/           # 962 unit test
│   └── postman/                            # collection phủ 123/123 endpoint
└── frontend/                               # Vite + React + TS
    └── src/{pages, components, layouts, context, hooks, lib, types, i18n}
```

**Chiều phụ thuộc:** `Api → Application → Domain`, `Infrastructure → Application/Domain`. Luồng request: **Controller (mỏng) → Service → Repository → DbContext**; DTO tách Entity; DI toàn bộ.

**Mô hình connection B:** mỗi service (Gmail/GCal/Drive/Jira) = 1 row `Connections` độc lập, token riêng, authorize riêng. Không có cột `Scopes`/`Permission` — scope suy từ `ServiceType` trong code.

Quy ước nền tảng: PK `Guid` · enum lưu **string** · datetime `datetime2` **UTC** · JSON `nvarchar(max)` · **không soft-delete** (dùng `IsArchived`).

---

## Tài liệu

| File | Nội dung |
|---|---|
| [`docs/SETUP.md`](docs/SETUP.md) | Chạy local đầy đủ (Windows + macOS), user-secrets, migration, OAuth |
| [`docs/DATABASE.md`](docs/DATABASE.md) | Schema đầy đủ (mô hình B), quan hệ, constraint |
| [`docs/API.md`](docs/API.md) | Endpoint, request/response, status code |
| [`docs/DEPLOY.md`](docs/DEPLOY.md) | Hạ tầng production, CI/CD, vận hành |
| [`docs/diagrams/`](docs/diagrams/) | Kiến trúc BE/FE · ERD · class & sequence theo use case (.drawio + .png + .svg) |
| [`docs/CONVENTIONS.md`](docs/CONVENTIONS.md) · [`docs/CHANGELOG.md`](docs/CHANGELOG.md) | Coding style, git · lịch sử quyết định thiết kế |
| [`CLAUDE.md`](CLAUDE.md) | Context cho AI coding assistant (scope, phase, gotcha) |

---

## Lệnh hay dùng

```bash
# Backend
cd backend
dotnet build && dotnet test                                   # build + 962 test
dotnet run --project src/WorkspaceHub.Api                     # http://localhost:5118/swagger
dotnet ef migrations add <Name> \
  --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api

# Frontend
cd frontend
npm run dev            # dev server
npm run build          # tsc + vite build
npm run lint           # ESLint
```

---

## Xử lý sự cố nhanh

```bash
# BE không build
cd backend && dotnet clean && dotnet restore --force && dotnet build

# FE lỗi module
cd frontend && rm -rf node_modules package-lock.json && npm install

# Port bị chiếm (5118 BE / 5173 FE / 1433 SQL / 6379 Redis)
lsof -ti:5118 | xargs kill -9        # macOS/Linux

# dotnet ef lỗi Kerberos / không thấy DB  →  thiếu --startup-project src/WorkspaceHub.Api
# SQL Server chưa sẵn sàng  →  docker compose logs wh-sqlserver  (chờ ~15s sau khi up)
```

---

## Đóng góp & giấy phép

Git: default branch `develop`, production `main`, feature branch `feature/<mô-tả>`. Commit prefix `feat:` / `fix:` / `refactor:` / `docs:` / `chore:`. PR vào `develop`, ≥ 1 approve.

Mã nguồn phát hành theo giấy phép **[MIT](LICENSE)** — fork / dùng / sửa thoải mái. Đây là đồ án môn học: một số quyết định ưu tiên "đủ dùng cho phạm vi môn học" hơn chuẩn production (xem [`docs/OPTIMIZATIONS.md`](docs/OPTIMIZATIONS.md) cho các điểm chưa tối ưu đã biết).
