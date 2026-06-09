# Setup Guide — Workspace Hub

Hướng dẫn chạy local từ con số 0, cho **Windows** (đa số nhóm) và **macOS/Linux**. Làm theo đúng thứ tự.

## Stack & port

| Thành phần | Công nghệ | URL dev |
|---|---|---|
| Backend | ASP.NET Core 8 Web API | http://localhost:5118 · https://localhost:7010 · Swagger `/swagger` |
| Frontend | Vite + React + TS | http://localhost:5173 |
| Database | SQL Server | localhost:1433 (Docker) hoặc instance Windows |

> FE gọi BE qua proxy `/api` → `http://localhost:5118` (cấu hình trong `frontend/vite.config.ts`), nên dev **không cần lo CORS**.

---

## 1. Cài công cụ (1 lần)

| Công cụ | Windows | macOS / Linux |
|---|---|---|
| **.NET SDK 8** | [Tải bộ cài](https://dotnet.microsoft.com/download/dotnet/8.0) | `brew install dotnet@8` hoặc bộ cài |
| **EF Core CLI** | `dotnet tool install --global dotnet-ef` | giống Windows |
| **Node.js 18+** | [nodejs.org](https://nodejs.org) | `brew install node` |
| **SQL Server** | SQL Server Express **hoặc** LocalDB (xem bước 3) | Docker (xem bước 3) |
| **Git** | [git-scm.com](https://git-scm.com) | có sẵn |

Kiểm tra:
```bash
dotnet --version   # 8.x
dotnet ef --version
node -v             # v18+
```

> Repo có `backend/global.json` ghim SDK 8 → kể cả máy bạn có .NET 9/10, lệnh `dotnet` trong `backend/` vẫn dùng SDK 8.

---

## 2. Clone

```bash
git clone <repo-url> workspace-hub-plan
cd workspace-hub-plan
```

---

## 3. Dựng SQL Server

Chọn **một** cách hợp với máy bạn.

### Cách A — Windows: LocalDB (nhẹ nhất, có sẵn khi cài Visual Studio)
Không cần làm gì thêm, dùng connection string:
```
Server=(localdb)\MSSQLLocalDB;Database=WorkspaceHub;Trusted_Connection=True;TrustServerCertificate=True
```

### Cách B — Windows: SQL Server Express
[Tải SQL Server Express](https://www.microsoft.com/sql-server/sql-server-downloads). Connection string:
```
Server=localhost\SQLEXPRESS;Database=WorkspaceHub;Trusted_Connection=True;TrustServerCertificate=True
```

### Cách C — macOS / Linux (hoặc Windows có Docker): container
macOS Apple Silicon (arm64) cần `--platform linux/amd64`:
```bash
docker run -d --name wh-sqlserver --platform linux/amd64 \
  -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Workspace#2026Dev" -e "MSSQL_PID=Developer" \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```
Connection string:
```
Server=localhost,1433;Database=WorkspaceHub;User Id=sa;Password=Workspace#2026Dev;TrustServerCertificate=True
```
Lần sau chỉ cần `docker start wh-sqlserver`.

> **DB `WorkspaceHub` không cần tạo tay** — lệnh `dotnet ef database update` ở bước 4 sẽ tự tạo.

---

## 4. Chạy Backend

```bash
cd backend

# 4.1 — Khai báo connection string của bạn (file này gitignore, mỗi máy 1 bản)
#       Copy file mẫu rồi sửa chuỗi cho khớp cách A/B/C ở trên:
cp src/WorkspaceHub.Api/appsettings.Development.json.example \
   src/WorkspaceHub.Api/appsettings.Development.json
#   (Windows PowerShell: copy ...\appsettings.Development.json.example ...\appsettings.Development.json)
#   → mở appsettings.Development.json, sửa ConnectionStrings:Default

# 4.2 — Restore + tạo DB (chạy migration)
dotnet restore
dotnet ef database update --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api

# 4.3 — Run
dotnet run --project src/WorkspaceHub.Api
```
Mở **http://localhost:5118/swagger** → thử `GET /api/health`, phải trả:
```json
{ "status": "Healthy", "database": "Connected", "userCount": 0 }
```
`database: "Connected"` = BE nối DB OK. Nếu `Unreachable` → xem [Troubleshooting](#troubleshooting).

> `dotnet ef` và `dotnet run` đọc **chung** connection string từ `appsettings.Development.json`. Muốn override nhanh không sửa file: set env `WORKSPACEHUB_CONNECTION="..."` trước khi chạy `dotnet ef`.

---

## 5. Chạy Frontend

Mở **terminal khác** (giữ BE đang chạy):
```bash
cd frontend
npm install
npm run dev
```
Mở **http://localhost:5173**. Trang Inbox (sau khi có token) gọi `/api/health` qua proxy để kiểm tra kết nối BE.

> Dev không cần file `.env`. Prod mới cần set `VITE_API_URL` (xem `frontend/.env.example`).

---

## 6. Config backend (key cần thiết)

Để ở `appsettings.Development.json` (local) hoặc user-secrets. **KHÔNG commit secret.**

| Key | Mục đích | Khi nào cần |
|---|---|---|
| `ConnectionStrings:Default` | DB connection | **Ngay (bước 4)** |
| `Jwt:Secret` | ký JWT (≥32 ký tự) | SCRUM-9 (auth) |
| `Jwt:Issuer` / `Jwt:Audience` / `Jwt:ExpiresIn` | tham số JWT | SCRUM-9 |
| `Google:ClientId` / `Google:ClientSecret` / `Google:RedirectUri` | OAuth Google | SCRUM-12/13 |
| `Cron:Secret` | bảo vệ `/api/internal/process-scheduled` | SCRUM-31 |

Dùng user-secrets (thay cho sửa file) nếu thích:
```bash
cd backend
dotnet user-secrets init --project src/WorkspaceHub.Api
dotnet user-secrets set "ConnectionStrings:Default" "<chuỗi của bạn>" --project src/WorkspaceHub.Api
dotnet user-secrets set "Jwt:Secret" "<random-32+-chars>" --project src/WorkspaceHub.Api
```
> Lưu ý: `dotnet ef` **không** đọc user-secrets. Nếu để connection string ở user-secrets, khi chạy `dotnet ef` hãy set thêm env `WORKSPACEHUB_CONNECTION`.

---

## 7. Lệnh EF hay dùng

```bash
cd backend
# tạo migration mới sau khi đổi entity
dotnet ef migrations add <TenMigration> --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api
# áp dụng vào DB
dotnet ef database update --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api
# gỡ migration cuối (chưa apply)
dotnet ef migrations remove --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api
```
> Đừng sửa migration đã commit. Đổi schema → tạo migration **mới**.

---

## 8. Google OAuth (SCRUM-12/13 — chưa cần lúc này)

1. Google Cloud Console → tạo project.
2. APIs & Services → bật Gmail API, Calendar API, Drive API.
3. OAuth consent screen → External, scope readonly.
4. Credentials → tạo OAuth Client ID (Web application).
5. Authorized redirect URIs → thêm `http://localhost:5173/oauth/callback` (dev) + URL prod.
6. Copy Client ID + Secret vào config (`Google:ClientId` / `Google:ClientSecret`).

Scope (sync 1 chiều): `gmail.readonly calendar.readonly drive.readonly`.

---

## 9. Cron scheduled email (SCRUM-31 — sau)

Cron ngoài (vd cron-job.org) gọi mỗi 5 phút:
```
POST http://localhost:5118/api/internal/process-scheduled
Header: X-Cron-Secret: <Cron:Secret>
```

---

## Troubleshooting

| Triệu chứng | Cách xử lý |
|---|---|
| `database: "Unreachable"` ở `/api/health` | SQL Server chưa chạy / sai connection string. Docker: `docker start wh-sqlserver`. Windows: kiểm tra tên instance (`localhost` vs `localhost\SQLEXPRESS` vs `(localdb)\MSSQLLocalDB`). |
| `dotnet ef database update` báo lỗi connect TCP error 40 | Như trên — SQL Server chưa lên hoặc sai server name. |
| `dotnet ef: command not found` | `dotnet tool install --global dotnet-ef`, rồi mở terminal mới. |
| FE gọi API 500/timeout | BE chưa chạy ở port 5118, hoặc đổi port → sửa `target` trong `frontend/vite.config.ts`. |
| HTTPS cert warning khi mở `https://localhost:7010` | Dev tin cert 1 lần: `dotnet dev-certs https --trust` (macOS/Windows). Hoặc dùng bản http 5118. |
| macOS: container SQL Server không chạy | Thiếu `--platform linux/amd64` (Apple Silicon) hoặc Docker Desktop chưa bật. |
| Port 5173 / 5118 bận | Tắt tiến trình cũ, hoặc đổi port (`npm run dev -- --port 5174`). |

---

## Deploy (Tuần 9 — chưa làm)

<!-- CHỐT: Render / Vercel / Azure -->
- Backend: <!-- ... --> (set `ConnectionStrings:Default`, `Jwt:Secret`... qua env của host)
- Frontend: <!-- ... --> (set `VITE_API_URL` trỏ BE đã deploy)
- DB: <!-- ... -->
