# DEPLOY — Hạ tầng, CI/CD, vận hành

> App production: **https://app.workspace-hub.space** (AWS Lightsail, Docker Compose, HTTPS Let's Encrypt).
> Tài liệu này = mọi thứ về deploy/vận hành. Rule code xem `CLAUDE.md` + `docs/CONVENTIONS.md`.

## 1. Kiến trúc production

Một máy **AWS Lightsail** (2GB, Singapore) chạy tất cả trong Docker Compose (`docker-compose.prod.yml`):

```
Internet ──443/80──▶ Caddy (web)  ── /api/* ──▶ api (.NET 8 :8080)
                       │  serve SPA          │
                       │  auto-HTTPS         ├──▶ mssql (SQL Server 2022, cap RAM 1GB)
                       └── try_files         └──▶ redis (refresh token / OTP / OAuth state)

volumes: mssql-data · redis-data · dp-keys (khoá mã hoá token OAuth) · caddy-data (cert)
```

- **Caddy** đứng trước: phục vụ SPA tĩnh + reverse-proxy `/api` → backend (same-origin ⇒ cookie auth + CSRF chạy, không CORS) + tự xin/gia hạn cert Let's Encrypt.
- **api** chỉ nghe HTTP nội bộ `:8080` (TLS do Caddy). Auto-migrate DB lúc khởi động khi `Db:AutoMigrate=true`.
- **mssql** giới hạn RAM (`MSSQL_MEMORY_LIMIT_MB=1024`) cho vừa máy 2GB; KHÔNG publish 1433 ra internet.

### File deploy (trong repo)
| File | Vai trò |
|---|---|
| `docker-compose.prod.yml` | Định nghĩa 4 service + volume + healthcheck. |
| `backend/Dockerfile` | Build .NET 8 API (multi-stage). |
| `frontend/Dockerfile` | Build SPA (node:22-slim, `npm install`*) → Caddy serve. |
| `deploy/Caddyfile` | Cấu hình Caddy (auto-HTTPS + proxy `/api`). |
| `deploy/.env.prod.example` | Mẫu secret; copy `.env` trên server. |

> *`npm install` (không `npm ci`) vì lockfile đa nền tảng của Vite 8/rolldown: lock sinh trên macOS thiếu optional deps chỉ-Linux (`@emnapi/*`) → `npm ci` strict fail trên container Linux.

## 2. CI/CD

```
Code → PR vào develop → CI (build/test) → merge → CD tự deploy lên Lightsail
```

- **CI** (`.github/workflows/ci.yml`): chạy mọi PR/push vào `develop`/`main`.
  - Backend: `dotnet build` + `dotnet test` (cổng cứng).
  - Frontend: `npm install` + `npm run lint` + `npm run build` (`tsc -b` bắt lỗi type; build là cổng cứng).
- **CD** (`.github/workflows/deploy.yml`): push/merge vào `develop` → GitHub Actions SSH vào Lightsail chạy `git reset --hard origin/develop` + `docker compose up -d --build`.
  - Cần 4 repo secrets (Settings → Secrets → Actions): `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_APP_DIR`, `DEPLOY_SSH_KEY` (Lightsail default private key).
  - Server track nhánh `develop`.

## 3. Cấu hình & secret

Secret prod nằm ở **`.env` trên server** (gitignored, KHÔNG commit) — xem `deploy/.env.prod.example` để biết các key:
`SITE_ADDRESS`, `MSSQL_SA_PASSWORD`, `JWT_SECRET`, `CRON_SECRET`, `GOOGLE_CLIENT_ID/SECRET`, `FIREBASE_PROJECT_ID` (tuỳ chọn), `R2_*` (SCRUM-75 — avatar, xem dưới).

Env quan trọng (set trong compose, đọc từ `.env`):
- `Db__AutoMigrate=true` — api tự áp migration lúc khởi động (single-instance).
- `Cron__AutoRun=true` — BackgroundService tự quét & gửi scheduled email mỗi `Cron__IntervalSeconds` (60s).
- `Cron__SyncAutoRun=true` + `Cron__SyncIntervalSeconds=60` — BackgroundService sync connections (Gmail/GCal/Drive/Jira) mỗi 60s. **Không** cần cron-job.org cho sync; **không** bật đồng thời với job HTTP `POST /api/internal/process-sync`.
- `Auth__CrossSiteCookies=false` — same-origin ⇒ cookie SameSite=Lax.
- Để xác thực SĐT đăng ký, cần thiết lập `Firebase:ProjectId`. Nếu không có, tính năng đăng ký sẽ báo lỗi khi FE yêu cầu Firebase ID Token.

### Cập nhật `.env` cho R2 (SCRUM-75) trên server đang chạy

Server track `develop` (mục 2) đã tự pull code mới (kèm code đọc `R2:*`), nhưng biến môi trường trong `.env` phải **tự set tay** (không có trong git). Trên Lightsail:

```bash
ssh <user>@<lightsail-ip>
cd ~/workspace-hub   # hoặc APP_DIR đã cấu hình trong secret DEPLOY_APP_DIR

# Thêm 5 dòng vào cuối .env (thay giá trị thật — bucket/token lấy từ Cloudflare Dashboard → R2)
cat >> .env << 'EOF'
R2_ACCOUNT_ID=4993758e52af95eea355e1b6484a554f
R2_BUCKET_NAME=workspace-hub-assets
R2_PUBLIC_URL=https://pub-xxxxxxxx.r2.dev
R2_ACCESS_KEY_ID=xxxxxxxx
R2_SECRET_ACCESS_KEY=xxxxxxxx
EOF

# Áp dụng: recreate container api để đọc .env mới (không cần rebuild image)
docker compose -f docker-compose.prod.yml up -d api

# Kiểm tra container thấy đúng biến
docker compose -f docker-compose.prod.yml exec api printenv | grep R2__
```

Không cần build lại image vì code đã có sẵn (merge `develop` là xong) — chỉ cần container mới đọc `.env` mới. Nếu deploy tiếp theo qua CI/CD (`git reset --hard` + `up -d --build`) chạy bình thường, `.env` trên server **không bị ghi đè** (file này không nằm trong git).

## 4. Google OAuth (prod)

Trong Google Cloud Console → OAuth client, thêm (giữ URL localhost cho dev):
- JavaScript origins: `https://app.workspace-hub.space`
- Redirect URIs: `https://app.workspace-hub.space/oauth/callback` + `https://app.workspace-hub.space/auth/google/callback`

## 5. Vận hành

```bash
# SSH vào server (Lightsail → Connect using SSH, hoặc key)
cd ~/workspace-hub

# xem trạng thái / log
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f api      # (web / mssql / redis)

# redeploy tay (bình thường CD tự làm khi merge develop)
git pull && docker compose -f docker-compose.prod.yml up -d --build

# restart / dừng
docker compose -f docker-compose.prod.yml restart web
docker compose -f docker-compose.prod.yml down
```

## 6. Truy cập DB production (DBeaver / GUI)

DB KHÔNG mở ra internet. 2 cách:

- **Query nhanh trong SSH:** `docker compose -f docker-compose.prod.yml exec mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '<MSSQL_SA_PASSWORD>' -C -d WorkspaceHub -Q "SELECT name FROM sys.tables"`
- **GUI (DBeaver):** cần mở `127.0.0.1:1433:1433` cho mssql trong compose (loopback, an toàn), rồi DBeaver connect với **SSH Tunnel** (Host `18.140.38.148`, user `ec2-user`, private key) → Main: Host `127.0.0.1`, Port `1433`, DB `WorkspaceHub`, user `sa`. Nếu lỗi cert: Driver properties `trustServerCertificate=true`.

## 7. Chi phí — giữ trong $100 credit

- Lightsail 2GB = **$10/tháng** phẳng (đã gồm SSD + static IP + băng thông) + IPv4 → ~$10-14/tháng → **$0 nhờ credit** (~8-10 tháng).
- **Đặt Budget cảnh báo:** Billing and Cost Management → **Budgets** → Create → Cost budget $80/tháng → alert 50%/80%/100% qua email.
- **Xem credit còn lại:** Billing → **Credits**.
- **Xem chi tiêu:** Billing → **Cost Explorer** (theo ngày/dịch vụ).
- **Khi hết môn:** Lightsail → Delete instance + **Release static IP** (kẻo IP mồ côi bị tính ~$3.6/tháng).

## 8. Troubleshooting (bẫy đã gặp)

| Triệu chứng | Nguyên nhân / fix |
|---|---|
| `compose build requires buildx 0.17.0 or later` | Cài buildx plugin: `curl -4 -SL .../buildx-vX.Y.Z.linux-amd64 -o /usr/libexec/docker/cli-plugins/docker-buildx && chmod +x`. |
| `curl (28) Failed to connect to github.com` | Máy dual-stack đi IPv6 treo → thêm cờ `-4` vào curl. |
| Web (Caddy) restart-loop, lỗi `email` in Caddyfile | `ACME_EMAIL` rỗng → KHÔNG để global `email` trong Caddyfile. |
| Không có HTTPS / cert fail | DNS chưa trỏ đúng static IP, hoặc port 80/443 chưa mở firewall Lightsail. |
| `api` restart liên tục | mssql chưa sẵn sàng (chờ ~40s), sai connection string, hoặc mật khẩu SA yếu. |
| `npm ci` fail Linux (`Missing @emnapi/*`) | Dùng `npm install` (đã cấu hình sẵn) — lockfile đa nền tảng. |
| Đăng nhập bị đá ra sau redeploy | Volume `dp-keys` / `redis-data` chưa mount. |

## 9. Đổi máy sang gói to hơn (nếu 2GB chật)

Đổi Lightsail sang 4GB ($20/tháng): nới RAM SQL Server trong `.env` (`MSSQL_MEMORY_LIMIT_MB=2048`, `MSSQL_MEM_LIMIT=2800m`), bỏ swap không cần nữa.
