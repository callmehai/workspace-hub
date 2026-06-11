# Setup Guide — Workspace Hub

## Yêu cầu

- .NET SDK 8.0+ (SDK 10 build target `net8.0` cũng chạy được)
- **SQL Server 2022** (dev chạy Docker, xem bên dưới)
- Node.js ≥ 20 (frontend Vite + React)
- Google Cloud project (OAuth credentials) — xem phần OAuth bên dưới

## SQL Server dev (Docker)

```bash
docker run -d --name wh-sqlserver \
  -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Workspace#2026Dev' \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```
Connection string dev tương ứng (đã có trong `appsettings.Development.json` mẫu):
`Server=localhost,1433;Database=WorkspaceHub;User Id=sa;Password=Workspace#2026Dev;TrustServerCertificate=True`

## Chạy backend local

```bash
cd backend

# 1. Restore
dotnet restore

# 2. Cấu hình secret (KHÔNG commit)
dotnet user-secrets init --project src/WorkspaceHub.Api
dotnet user-secrets set "ConnectionStrings:Default" "<connection-string>" --project src/WorkspaceHub.Api
dotnet user-secrets set "Jwt:Secret" "<random-32+-chars>" --project src/WorkspaceHub.Api
dotnet user-secrets set "Google:ClientId" "<client-id>" --project src/WorkspaceHub.Api
dotnet user-secrets set "Google:ClientSecret" "<client-secret>" --project src/WorkspaceHub.Api
dotnet user-secrets set "Cron:Secret" "<cron-secret>" --project src/WorkspaceHub.Api

# 3. Apply migrations (tạo DB) — hiện có 3 migration: InitialCreate, UsersMultiAuth, ModelBConnections
dotnet ef database update --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api

# 4. Run
dotnet run --project src/WorkspaceHub.Api
# Swagger: http://localhost:5118/swagger (https: 7010)
```

> **Gotcha `dotnet ef`:** design-time factory đọc connection string theo thứ tự env `WORKSPACEHUB_CONNECTION` → appsettings của thư mục hiện tại. Nếu chạy `dotnet ef` từ thư mục Infrastructure (không có `--startup-project`), nó không thấy appsettings của Api và rơi về fallback `Trusted_Connection` (lỗi Kerberos trên macOS). Cách chắc nhất: luôn dùng `--startup-project src/WorkspaceHub.Api`, hoặc set env `WORKSPACEHUB_CONNECTION`.

> **OAuth dev:** `ConnectionsService` dev đọc credential plaintext từ `Dev:google:ClientId` / `Dev:google:ClientSecret` trong `appsettings.Development.json` để test nhanh (prod mới decrypt từ DB qua Data Protection).

## Biến môi trường / config cần thiết

| Key | Mục đích |
|---|---|
| `ConnectionStrings:Default` | DB connection |
| `Jwt:Secret` | ký JWT (≥32 ký tự) |
| `Jwt:ExpiresIn` | mặc định 3600s |
| `Google:ClientId` / `Google:ClientSecret` | OAuth Google |
| `Google:RedirectUri` | callback URL |
| `Cron:Secret` | bảo vệ /api/internal/process-scheduled (X-Cron-Secret) |

## Tạo migration mới

```bash
dotnet ef migrations add <TenMigration> \
  --project src/WorkspaceHub.Infrastructure \
  --startup-project src/WorkspaceHub.Api
```

## Setup Google OAuth (cho SCRUM-12/13)

1. Google Cloud Console → tạo project.
2. APIs & Services → bật Gmail API, Calendar API, Drive API.
3. OAuth consent screen → cấu hình (External, scope readonly).
4. Credentials → tạo OAuth Client ID (Web application).
5. Authorized redirect URIs → thêm `https://localhost:5001/oauth/callback` (dev) và URL prod.
6. Copy Client ID + Secret vào user-secrets.

Scope dùng (2 chiều, mô hình B — mỗi service xin riêng full scope):
- Gmail: `gmail.modify gmail.send`
- Calendar: `calendar`
- Drive: `drive.file`
- Google Sign-In (đăng nhập): `openid email profile` (riêng, không tạo Connection)

> Connection cũ connect bằng scope readonly (mô hình A) sau migration SCRUM-34 vẫn giữ token cũ → phải **reconnect** mới dùng được write-back.

## Cron cho scheduled email (SCRUM-31)

Dùng cron ngoài (vd cron-job.org) gọi mỗi 5 phút:
```
POST https://<domain>/api/internal/process-scheduled
Header: X-Cron-Secret: <cron-secret>
```

## Frontend (Vite + React + TypeScript)

```bash
cd frontend
npm install
npm run dev        # http://localhost:5173
```
Cấu hình base URL API trong `.env` (vd `VITE_API_URL=http://localhost:5118`).

## Deploy

<!-- CHỐT: Render / Vercel / Azure -->
- Backend: <!-- chưa chốt -->
- Frontend: <!-- chưa chốt -->
- DB: SQL Server (dev + prod cùng engine) — host prod chưa chốt
