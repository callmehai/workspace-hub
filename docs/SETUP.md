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
dotnet user-secrets set "OAuth:google:ClientId" "<client-id>" --project src/WorkspaceHub.Api
dotnet user-secrets set "OAuth:google:ClientSecret" "<client-secret>" --project src/WorkspaceHub.Api
dotnet user-secrets set "Cron:Secret" "<cron-secret>" --project src/WorkspaceHub.Api

# 3. Apply migrations (tạo DB) — hiện có 4 migration: InitialCreate, UsersMultiAuth, ModelBConnections, RemoveClientCredentialsFromIntegration
dotnet ef database update --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api

# 4. Run
dotnet run --project src/WorkspaceHub.Api
# Swagger: http://localhost:5118/swagger (https: 7010)
```

> **Gotcha `dotnet ef`:** design-time factory đọc connection string theo thứ tự env `WORKSPACEHUB_CONNECTION` → appsettings của thư mục hiện tại. Nếu chạy `dotnet ef` từ thư mục Infrastructure (không có `--startup-project`), nó không thấy appsettings của Api và rơi về fallback `Trusted_Connection` (lỗi Kerberos trên macOS). Cách chắc nhất: luôn dùng `--startup-project src/WorkspaceHub.Api`, hoặc set env `WORKSPACEHUB_CONNECTION`.

> **OAuth credentials:** đọc từ section `OAuth:{provider}:ClientId` / `ClientSecret` trong config (appsettings / user-secrets / env var). Prod set qua env var `OAuth__google__ClientId` / `OAuth__google__ClientSecret` (ASP.NET dùng `__` thay `:` trong env). Không còn section `Dev:` riêng — đây là đường chính thức cho cả dev lẫn prod.

> **OAuth state cache:** state CSRF lưu bằng `AddDistributedMemoryCache` (in-memory) — **restart app giữa chừng flow OAuth sẽ mất state** → user nhận "State không hợp lệ", phải bấm connect lại. Chấp nhận được cho MVP single-instance; deploy nhiều instance thì phải đổi sang Redis.

## Biến môi trường / config cần thiết

| Key | Mục đích |
|---|---|
| `ConnectionStrings:Default` | DB connection |
| `Jwt:Secret` | ký JWT (≥32 ký tự) |
| `Jwt:ExpiresIn` | mặc định 3600s |
| `OAuth:google:ClientId` / `OAuth:google:ClientSecret` | OAuth Google (prod: env var `OAuth__google__ClientId`) |
| `OAuth:jira:ClientId` / `OAuth:jira:ClientSecret` | OAuth Jira (tương tự) |
| `Google:RedirectUri` | callback URL |
| `Cron:Secret` | bảo vệ /api/internal/process-scheduled (X-Cron-Secret) |
| `ConnectionStrings:Redis` | Redis cho refresh token + OTP + OAuth state (SCRUM-63, vd `localhost:6379`) |
| `Sms:Twilio:AccountSid` / `Sms:Twilio:AuthToken` / `Sms:Twilio:FromNumber` | Twilio SMS gửi OTP (SCRUM-64; thiếu → dev fallback `LogSmsSender` ghi OTP ra log) |
| `Cors:AllowedOrigins` | (prod) origin FE cho cookie auth cross-site, vd `https://app.example.com` |

> **Auth overhaul (SCRUM-62→64) — chưa merge, đang làm theo nhánh:** access token sẽ chuyển sang **HttpOnly cookie** (bỏ localStorage), refresh token lưu **Redis** với rotation, đăng ký thêm **OTP SMS qua Twilio**. Chi tiết quyết định: CHANGELOG.md mục [2026-06-30]. Khi các nhánh merge: cần chạy `docker compose up -d wh-redis`, set `ConnectionStrings:Redis` + `Sms:Twilio:*`, và chạy migration thêm cột `Users.Phone/PhoneVerified`.

## Tạo migration mới

```bash
dotnet ef migrations add <TenMigration> \
  --project src/WorkspaceHub.Infrastructure \
  --startup-project src/WorkspaceHub.Api
```

## Setup Google OAuth (cho SCRUM-12/13)

1. Google Cloud Console → tạo project.
2. APIs & Services → bật Gmail API, Calendar API, Drive API.
3. OAuth consent screen → cấu hình (External, scope read-write: gmail.modify + gmail.send, calendar, drive — xem danh sách bên dưới).
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
