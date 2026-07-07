---
description: "Invoke khi làm bất kỳ việc gì về deploy / hạ tầng / CI-CD / vận hành server / truy cập DB production / billing của Workspace Hub. Vd: 'deploy lên prod', 'sao app lỗi trên server', 'redeploy', 'sửa docker-compose.prod', 'thêm secret CI/CD', 'truy cập DB production', 'check billing AWS', 'app không lên HTTPS'. Toàn bộ chi tiết ở docs/DEPLOY.md — skill này là bản đồ nhanh + quy trình."
---

# Deploy & Ops — Workspace Hub

**App production:** https://app.workspace-hub.space — AWS Lightsail (2GB, Singapore, static IP), Docker Compose. **Nguồn chân lý đầy đủ: `docs/DEPLOY.md`** (đọc nó trước khi làm việc lớn).

## Stack (docker-compose.prod.yml)
`web` (Caddy: auto-HTTPS + serve SPA + proxy `/api` same-origin) → `api` (.NET 8 :8080, auto-migrate) · `mssql` (SQL Server, cap RAM 1GB, KHÔNG public 1433) · `redis`. Volumes: mssql-data, redis-data, `dp-keys` (khoá mã hoá token — mất là hỏng OAuth), caddy-data (cert).

## Quy tắc VÀNG khi đụng deploy
1. **Không push thẳng develop/main** (nhánh mặc định, cần PR + approval) — luôn tạo nhánh + PR.
2. **Không commit secret.** Secret prod ở `.env` trên server (gitignored). Trong repo chỉ có `deploy/.env.prod.example`.
3. **FE dùng `npm install` KHÔNG `npm ci`** (Dockerfile + ci.yml) — lockfile đa nền tảng Vite 8/rolldown, npm ci fail trên Linux (thiếu `@emnapi/*`). Đừng "sửa" thành npm ci.
4. **Caddyfile KHÔNG để global `email`** — `ACME_EMAIL` rỗng làm Caddy crash-loop.
5. **Sửa `docker-compose.prod.yml`/`Caddyfile` phải qua PR vào develop** — sửa tay trên server sẽ bị CD `git reset --hard` xoá.
6. Trên server dual-stack: `curl` GitHub thêm `-4` (IPv6 hay treo).

## CI/CD
- **CD:** merge/push `develop` → `deploy.yml` SSH vào Lightsail → `git reset --hard origin/develop` + `docker compose -f docker-compose.prod.yml up -d --build`. Secrets: `DEPLOY_HOST/USER/APP_DIR/SSH_KEY`.
- **CI:** `ci.yml` mọi PR/push develop/main → BE build+test, FE lint+build.

## Việc hay làm
- **Redeploy tay:** SSH → `cd ~/workspace-hub && git pull && docker compose -f docker-compose.prod.yml up -d --build`.
- **Xem log / debug:** `docker compose -f docker-compose.prod.yml ps` + `... logs -f <web|api|mssql|redis>`.
- **Lấy OTP đăng ký** (Twilio để trống): `... logs api | grep -i otp`.
- **Truy cập DB (DBeaver):** SSH tunnel (Host 18.140.38.148, user ec2-user, Lightsail key) → mssql `127.0.0.1:1433` (cần mở loopback trong compose), sa / DB WorkspaceHub, `trustServerCertificate=true`. Query nhanh: `... exec mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '<pw>' -C -d WorkspaceHub -Q "..."`.
- **Billing (giữ trong $100):** Billing → Budgets (đặt cảnh báo $80) · Credits (xem còn lại) · Cost Explorer. Lightsail ~$10-14/tháng.

## Troubleshooting nhanh (đầy đủ ở docs/DEPLOY.md §8)
buildx <0.17 → cài docker-buildx plugin · Caddy restart-loop `email` → bỏ global email · cert fail → DNS/firewall 80-443 · api restart → mssql chưa healthy / SA password yếu · npm ci fail → dùng npm install.
