# Setup Guide — Workspace Hub

> Placeholder có dấu `<!-- CHỐT -->` cần điền theo lựa chọn thật của nhóm.

## Yêu cầu

- .NET SDK 8.0+
- SQL Server 2022 (hoặc Express / LocalDB / Docker `mcr.microsoft.com/mssql/server`)
- Node.js 18+ (cho frontend) <!-- CHỐT nếu dùng React/Next -->
- Google Cloud project (OAuth credentials) — xem phần OAuth bên dưới

## SQL Server bằng Docker (macOS / Linux — đã test)

macOS không chạy SQL Server native → dùng Docker. Apple Silicon (arm64) cần `--platform linux/amd64` (Rosetta).

```bash
docker run -d --name wh-sqlserver --platform linux/amd64 \
  -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Workspace#2026Dev" -e "MSSQL_PID=Developer" \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest

# Connection string tương ứng (set qua user-secrets, KHÔNG commit password):
# Server=localhost,1433;Database=WorkspaceHub;User Id=sa;Password=Workspace#2026Dev;TrustServerCertificate=True
```
Windows có SQL Server / LocalDB sẵn thì dùng `Trusted_Connection=True` như mục dưới.

## Chạy backend local

```bash
# 1. Restore
dotnet restore

# 2. Cấu hình secret (KHÔNG commit)
dotnet user-secrets init --project src/WorkspaceHub.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Database=WorkspaceHub;Trusted_Connection=True;TrustServerCertificate=True" --project src/WorkspaceHub.Api
dotnet user-secrets set "Jwt:Secret" "<random-32+-chars>" --project src/WorkspaceHub.Api
dotnet user-secrets set "Google:ClientId" "<client-id>" --project src/WorkspaceHub.Api
dotnet user-secrets set "Google:ClientSecret" "<client-secret>" --project src/WorkspaceHub.Api
dotnet user-secrets set "Cron:Secret" "<cron-secret>" --project src/WorkspaceHub.Api

# 3. Apply migrations (tạo DB)
dotnet ef database update --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api

# 4. Run
dotnet run --project src/WorkspaceHub.Api
# Swagger: https://localhost:5001/swagger
```

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

Scope dùng (sync 1 chiều): `gmail.readonly calendar.readonly drive.readonly`.

## Cron cho scheduled email (SCRUM-31)

Dùng cron ngoài (vd cron-job.org) gọi mỗi 5 phút:
```
POST https://<domain>/api/internal/process-scheduled
Header: X-Cron-Secret: <cron-secret>
```

## Frontend

<!-- CHỐT: điền theo React/Next/MVC -->
```bash
# vd nếu React/Vite:
cd frontend
npm install
npm run dev
```
Cấu hình base URL API trong `.env` (vd `VITE_API_URL=https://localhost:5001`).

## Deploy

<!-- CHỐT: Render / Vercel / Azure -->
- Backend: <!-- ... -->
- Frontend: <!-- ... -->
- DB: <!-- ... -->
