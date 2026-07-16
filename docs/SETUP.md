# Setup Guide — Workspace Hub

## Yêu cầu

- .NET SDK 8.0+ (SDK 10 build target `net8.0` cũng chạy được)
- **SQL Server 2022** (dev chạy Docker, xem bên dưới)
- Node.js ≥ 20 (frontend Vite + React)
- Google Cloud project (OAuth credentials) — xem phần OAuth bên dưới

## Hạ tầng dev (Docker Compose)

Cách gọn nhất — chạy cả SQL Server + Redis một lệnh (xem `docker-compose.yml` ở repo root):
```bash
cp .env.example .env            # SA password cho SQL Server (gitignored; đổi nếu cần)
docker compose up -d            # SQL Server (wh-sqlserver) + Redis (wh-redis)
docker compose up -d wh-redis   # chỉ Redis (SCRUM-63)
```

Hoặc chạy SQL Server thủ công:
```bash
docker run -d --name wh-sqlserver \
  -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Workspace#2026Dev' \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```
Connection string dev tương ứng (đã có trong `appsettings.Development.json` mẫu):
`Server=localhost,1433;Database=WorkspaceHub;User Id=sa;Password=Workspace#2026Dev;TrustServerCertificate=True`
Redis dev: `ConnectionStrings:Redis=localhost:6379`.

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

# 3. Apply migrations (tạo DB). Gồm: InitialCreate, UsersMultiAuth, ModelBConnections,
#    RemoveClientCredentialsFromIntegration, ... , AddFriendSystem (mới nhất trên develop);
#    SCRUM-64 (đang làm) thêm migration rename PhoneVerified→EmailVerified + drop Phone.
dotnet ef database update --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api

# 4. Run (profile https — FE proxy trỏ tới https://localhost:7010)
dotnet run --project src/WorkspaceHub.Api --launch-profile https
# Swagger: https://localhost:7010/swagger (http vẫn mở ở 5118)
```

> **HTTPS dev:** Vite proxy (`frontend/vite.config.ts`) trỏ `/api` → `https://localhost:7010` (`secure:false` để chấp nhận dev cert tự ký). Chạy BE bằng `--launch-profile https`. Lần đầu cần tin tưởng dev cert: `dotnet dev-certs https --trust`. Nếu muốn chạy http-only thì đổi target proxy về `http://localhost:5118`.

> **Gotcha `dotnet ef`:** design-time factory đọc connection string theo thứ tự env `WORKSPACEHUB_CONNECTION` → appsettings của thư mục hiện tại. Nếu chạy `dotnet ef` từ thư mục Infrastructure (không có `--startup-project`), nó không thấy appsettings của Api và rơi về fallback `Trusted_Connection` (lỗi Kerberos trên macOS). Cách chắc nhất: luôn dùng `--startup-project src/WorkspaceHub.Api`, hoặc set env `WORKSPACEHUB_CONNECTION`.

> **OAuth credentials:** đọc từ section `OAuth:{provider}:ClientId` / `ClientSecret` trong config (appsettings / user-secrets / env var). Prod set qua env var `OAuth__google__ClientId` / `OAuth__google__ClientSecret` (ASP.NET dùng `__` thay `:` trong env). Không còn section `Dev:` riêng — đây là đường chính thức cho cả dev lẫn prod.

> **Distributed cache (SCRUM-63):** có `ConnectionStrings:Redis` → dùng **Redis** (`AddStackExchangeRedisCache`) cho refresh token (`refresh:{jti}`), OTP (SCRUM-64) và OAuth state CSRF. Thiếu Redis → fallback **in-memory** (dev) + log cảnh báo: refresh token + OAuth state sẽ mất khi restart, không chia sẻ giữa nhiều instance. Prod/multi-instance **bắt buộc** Redis.
>
> Chạy Redis dev: `docker compose up -d wh-redis` (xem `docker-compose.yml`), rồi set `ConnectionStrings:Redis=localhost:6379`.

## Biến môi trường / config cần thiết

| Key | Mục đích |
|---|---|
| `ConnectionStrings:Default` | DB connection |
| `Jwt:Secret` | ký JWT (≥32 ký tự) |
| `Jwt:ExpiresIn` | mặc định 3600s |
| `OAuth:google:ClientId` / `OAuth:google:ClientSecret` | OAuth Google (prod: env var `OAuth__google__ClientId`) |
| `OAuth:atlassian:ClientId` / `OAuth:atlassian:ClientSecret` | OAuth Jira (tương tự) |
| `Google:RedirectUri` | callback URL connect-để-sync (`/oauth/callback`) |
| `Google:SignInRedirectUri` | callback URL Google Sign-In (`/auth/google/callback`) — tách khỏi connect flow |
| `Cron:Secret` | bảo vệ `/api/internal/process-scheduled` và `/api/internal/process-sync` (header `X-Cron-Secret`) |
| `Cron:SyncAutoRun` | `true` = BE tự chạy `ConnectionSyncProcessorService`. **Prod:** bật trong `docker-compose.prod.yml` (cùng pattern cron email). Dev: `appsettings.Development.json` |
| `Cron:SyncIntervalSeconds` | Chu kỳ auto-sync khi `SyncAutoRun=true` (prod compose: 60s; default appsettings: 300s) |
| `ConnectionStrings:Redis` | Redis cho refresh token + OTP + OAuth state (SCRUM-63, vd `localhost:6379`) |
| `Email:Resend:ApiKey` / `Email:Resend:FromAddress` | Resend (SCRUM-64) — gửi OTP đăng ký qua email. Thiếu → dev fallback `LogEmailSender` (ghi OTP ra log). `FromAddress` phải thuộc domain đã verify trên Resend (test mode: `onboarding@resend.dev`). |
| `Cors:AllowedOrigins` | (prod) origin FE cho cookie auth cross-site, vd `https://app.example.com` |
| `R2:AccountId` / `R2:BucketName` / `R2:PublicUrl` | Cloudflare R2 (SCRUM-75) — avatar upload. `PublicUrl` = domain public bucket (`r2.dev` hoặc custom domain) |
| `R2:AccessKeyId` / `R2:SecretAccessKey` | API token R2 (scope **Object Read & Write**, giới hạn đúng bucket) — secret, KHÔNG commit |

> **Auth overhaul (SCRUM-62→64):** access token chuyển sang **HttpOnly cookie** (bỏ localStorage), refresh token lưu **Redis** với rotation (62/63 ✅), đăng ký thêm **OTP qua Email** (64 🔄 đang đổi hướng SMS→Email/Resend — xem CHANGELOG [2026-07-16]). Cần chạy `docker compose up -d wh-redis`, set `ConnectionStrings:Redis` + `Email:Resend:*`, và chạy migration đổi `Users.EmailVerified`.

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
5. Authorized redirect URIs → thêm cả 2: `http://localhost:5173/oauth/callback` (connect-để-sync) **và** `http://localhost:5173/auth/google/callback` (Google Sign-In) cho dev; thêm URL prod tương ứng.
6. Copy Client ID + Secret vào user-secrets.

Scope dùng (2 chiều, mô hình B — mỗi service xin riêng full scope):
- Gmail: `gmail.modify gmail.send`
- Calendar: `calendar`
- Drive: `drive.file`
- Google Sign-In (đăng nhập): `openid email profile` (riêng, không tạo Connection)

> Connection cũ connect bằng scope readonly (mô hình A) sau migration SCRUM-34 vẫn giữ token cũ → phải **reconnect** mới dùng được write-back.

## Setup Email OTP — Resend (SCRUM-64)

OTP đăng ký được gửi tới **chính email user đăng ký**, từ một **sender hệ thống** qua [Resend](https://resend.com) (HTTP API). KHÔNG dùng Gmail của user (`IGmailGateway` là luồng khác). Thiếu config → dev fallback `LogEmailSender` ghi OTP ra log để test.

1. Tạo tài khoản Resend, lấy **API Key** (Dashboard → API Keys).
2. **Verify domain** (bắt buộc cho prod): Dashboard → Domains → thêm `workspace-hub.space`, tạo DNS record **SPF + DKIM** ở registrar. DNS propagate mất thời gian → làm sớm. Chưa verify (test mode): `FromAddress` phải là `onboarding@resend.dev` và chỉ gửi được tới email chủ tài khoản Resend.
3. Set config:
```bash
dotnet user-secrets set "Email:Resend:ApiKey"      "re_xxx"                     --project src/WorkspaceHub.Api
dotnet user-secrets set "Email:Resend:FromAddress" "no-reply@workspace-hub.space" --project src/WorkspaceHub.Api
```
Dev không set → OTP in ra log console (`LogEmailSender`), lấy mã ở đó để verify.

## Cron cho scheduled email (SCRUM-31)

Dùng cron ngoài (vd cron-job.org) gọi mỗi 5 phút:
```
POST https://<domain>/api/internal/process-scheduled
Header: X-Cron-Secret: <cron-secret>
```

Tuỳ chọn dev: `Cron:AutoRun=true` → BE tự chạy `ScheduledEmailProcessorService` mỗi `Cron:IntervalSeconds` (xem `appsettings.Development.json.example`).

## Cron cho connection sync (SCRUM-72)

Đồng bộ định kỳ mọi Connection Active (Gmail / GCal / Drive / Jira) về DB — **bổ sung** on-demand sync (SCRUM-16), không thay webhook.

**Dev (auto-run, khuyến nghị khi test local):** trong `appsettings.Development.json` (gitignored):

```json
"Cron": {
  "Secret": "dev-cron-secret-change-me",
  "SyncAutoRun": true,
  "SyncIntervalSeconds": 60
}
```

Restart BE → log `ConnectionSyncProcessor started — quét mỗi 60s.`

**Prod (mặc định — BackgroundService trong container, giống cron gửi email hẹn giờ):** `docker-compose.prod.yml` đã bật:

```yaml
Cron__SyncAutoRun: "true"
Cron__SyncIntervalSeconds: "60"
```

Merge + CD deploy là đủ — **không cần** thêm job cron-job.org cho sync. **Không bật đồng thời** cron ngoài + `SyncAutoRun=true` (trùng lặp, tốn quota API Google/Jira).

**Tuỳ chọn (thay BackgroundService):** tắt `Cron__SyncAutoRun`, dùng cron-job.org gọi mỗi ~5 phút:

```
POST https://<domain>/api/internal/process-sync
Header: X-Cron-Secret: <Cron:Secret>
```

Trả `200` + `ProcessSyncResult` (`totalConnections`, `successCount`, `skippedCount`, `errorCount`, `details?`).

**FE auto-refresh:** Inbox/Kanban poll `items` mỗi 45s; Integrations poll `connections` mỗi 60s (`refetchIntervalInBackground` — poll cả tab nền); không cần F5 sau cron.

**Test thủ công (không đợi timer):**

```bash
curl -k -X POST https://localhost:7010/api/internal/process-sync \
  -H "X-Cron-Secret: dev-cron-secret-change-me"
```

> `Cron:Secret` dùng **chung** cho cả `process-scheduled` và `process-sync`. Không commit secret thật vào git.

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
