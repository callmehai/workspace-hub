# Workspace Hub — Claude Code Context

> File này là context gốc cho Claude Code. Đọc file này trước, rồi tham chiếu các file trong `docs/` khi cần chi tiết. Thay đổi thiết kế gần nhất: xem `docs/CHANGELOG.md`.

## Project là gì

Web app gom email / sự kiện / file / note (và ticket ở phase Jira) từ **Google (Gmail, Calendar, Drive)** về **một nơi duy nhất**, quản lý theo **Folder context** (dự án / khách hàng / chủ đề) với giao diện **Kanban 3 cột** (Cần xem / Đang xử lý / Done). Có chia sẻ folder (Viewer-only), hẹn giờ gửi email, và dashboard admin.

Đây là đồ án môn học (PRN232 — Fullstack ASP.NET, 60% backend / 40% frontend).

## Scope & Phase — ĐỌC KỸ

App gom + **đồng bộ 2 chiều** (đọc + ghi ngược lên provider). Hầu hết đã xong; vài ticket còn "In Review" nhưng code đã merge. Status từng ticket: `docs/SPRINTS.md` (⚠️ SPRINTS.md dòng "Phase Jira chưa code" đã stale — Jira đã code, xem dưới).

### Sprint hiện hành: **Sprint 4** (FE đầy đủ + hoàn thiện + nghiệm thu). Status chi tiết từng ticket: `docs/SPRINTS.md` (nguồn = Jira export mới nhất).

**✅ ĐÃ XONG (nền tảng 2 chiều + Jira + auth overhaul):**
- **Mô hình B + OAuth per-service + Google Sign-In:** SCRUM-34/35/36 + 32/33. Bật service = full scope read-write.
- **Write-back Google (2 chiều, synchronous):** `PATCH/POST/DELETE /api/items` — Email (label/read/star/trash + gửi mới, **KHÔNG sửa nội dung** Gmail immutable), Event (CRUD), File (rename/trash). Conflict qua `Items.ETag` → 409 (`IWriteBackGuard`). ✅ SCRUM-37/38.
- **Scheduled email + cron gửi:** `/api/internal/process-scheduled` (X-Cron-Secret). ✅ SCRUM-30/31.
- **Phase Jira/Atlassian (CRUD đầy đủ) — ĐÃ CODE XONG:** Atlassian = 1 Integration, mỗi Jira account = 1 Connection (ServiceType=Jira). OAuth 3LO+cloudId (54), sync issue→Item(Ticket) (55), tạo (56), write-back update qua cùng `IWriteBackGuard` — `fields.updated` làm version-token thay ETag (57), xoá (58), metadata helpers (59), ImportantContacts JiraAccount (60). Description = ADF 2 chiều. Migration `EnableJiraIntegration` đã bật `atlassian` IsEnabled=true trên develop.
- **Auth overhaul:** access token → **HttpOnly cookie + CSRF** (62 ✅), refresh token + Redis rotation (63 ✅ trên nhánh). OAuth key Jira = `atlassian` (đọc `OAuth:atlassian:ClientId/Secret`).
- **Tag BE:** CRUD + assign/unassign (70 ✅).

**🔄 Đang làm / ⏳ còn lại (Sprint 4):**
- 🔄 OTP đăng ký qua **Email (Resend)** (64, Lộc — đổi hướng từ SMS/Firebase, xem CHANGELOG [2026-07-16]) · FE Admin dashboard (49, Huy) · FE Admin toggle integration (61, Khánh) · Notifications in-app (68, Khánh).
- ⏳ FE: Tag UI (71) · Highlight email chưa đọc (67) · People API gợi ý contact (69) · responsive/dark mode (50).
- ⏳ **Cron sync connection định kỳ + FE auto-refresh (72, Dũng)** — **THÊM** sync định kỳ ngoài on-demand (xem lưu ý sync bên dưới).
- ⏳ Unit test service (29, Hải) · deploy prod config (51) · finalize Swagger+E2E (52) · defense (53).

> **Lưu ý mô hình sync:** hiện đọc = **on-demand** (không background pull, không webhook). SCRUM-72 sẽ **bổ sung** cron sync định kỳ (`/api/internal/process-sync`) + FE polling — vẫn KHÔNG phải webhook. Webhook/push realtime vẫn **ngoài scope, chưa có ticket**.

### NGOÀI scope (đừng code, chưa có ticket)
- Webhook/push realtime (Gmail watch + Pub/Sub, Calendar/Drive/Jira watch).
- AI workflow → future.

> **Friend system (bạn bè nội bộ app) đã VÀO scope + code xong nền tảng** (2026-07-10, chưa có ticket Jira): kết bạn theo email (user có tài khoản → pending in-app; chưa có → FriendInvite + mail mời qua Gmail của người mời, link `/register?inviteToken=` → tự kết bạn), hạng Friend/CloseFriend per-side, trang `/friends`. KHÔNG dùng provider ngoài (đã bỏ hướng Google Contacts/People API của PR #100). Tương lai: share folder cho bạn bè có role, tạo event nhanh cùng bạn.

Nếu một task có vẻ cần **webhook**, dừng lại và hỏi. Jira/write-back/auth-cookie **đã code xong** — sửa/mở rộng bình thường theo ticket, không cần hỏi "có thuộc phase không" nữa.

## Tech Stack

- **Backend:** ASP.NET Core Web API (.NET 8), Entity Framework Core — solution 4 project: `Domain` / `Application` / `Infrastructure` / `Api` (xem `backend/README.md`)
- **Database:** SQL Server (EF Core `UseSqlServer`; dev chạy Docker `wh-sqlserver`). JSON lưu `nvarchar(max)`, datetime lưu `datetime2` (UTC). Dev + prod cùng SQL Server.
- **Frontend:** Vite + React + TypeScript + Tailwind + React Router + TanStack Query + axios + react-hot-toast (SPA gọi REST).
- **Auth:** JWT Bearer + Google Sign-In (đăng nhập bằng Google, tách khỏi connect-để-sync)
- **Token encryption:** ASP.NET Data Protection (`IDataProtectionProvider`) — KHÔNG tự viết AES, KHÔNG lưu key trong DB
- **Deploy:** AWS Lightsail (1 máy, Docker Compose) — app live tại `https://app.workspace-hub.space`. CI/CD: merge `develop` → auto-deploy. Chi tiết hạ tầng/vận hành/CI-CD/DB access/billing: **`docs/DEPLOY.md`**.

## Kiến trúc

Layered / Clean: **Controller (API) → Service (business logic) → Repository (data access)**.
- Controller mỏng, không chứa business logic.
- DTO tách khỏi Entity — không expose entity trực tiếp ra API.
- Async/await toàn bộ data access.
- DI cho mọi service/repository (không `new` trực tiếp trong controller).

## Hai khái niệm dễ nhầm — phân biệt rõ

1. **Google Sign-In** = đăng nhập vào app (scope openid/email/profile). KHÔNG tạo Connection, chỉ tạo/tìm User + phát JWT. Disconnect service KHÔNG làm logout.
2. **Connect service** = cấp quyền đọc/ghi Gmail/GCal/Drive. Tạo 1 Connection mỗi service (mô hình B). Bật service = cấp FULL scope của service đó (scope do dev quyết, hardcode trong code).

## Quy ước nền tảng (BẮT BUỘC tuân thủ)

- **ID:** `Guid` cho mọi entity.
- **Role:** 1 user = 1 role. Lưu cột `Users.Role` (string `Admin`/`User`), KHÔNG bảng `Roles`/`UserRoles`. Đẩy vào JWT claim `role`.
- **Timestamp:** UTC. SQL Server `datetime2`, EF `DateTimeKind.Utc`. Convert timezone ở frontend.
- **Soft delete:** KHÔNG dùng. Dùng `IsArchived`. Xoá thật khi Delete.
- **Enum:** lưu dạng string (`.HasConversion<string>()`).
- **JSON:** SQL Server `nvarchar(max)`. Parse ở frontend.
- **Cascade:** ON DELETE CASCADE cho child của User/Folder. `Items.ConnectionId`/`ScheduledEmails.ConnectionId` → NoAction ở DB (tránh multiple cascade path), service layer set NULL khi disconnect.
- **Unique:** junction → composite PK. Cột Key/slug → UNIQUE. Mọi FK có index.
- **Connections (mô hình B):** KHÔNG thêm cột `Scopes` (suy từ ServiceType trong code) và KHÔNG cột `Permission`/`AccessLevel` (bật là full quyền).

## Cấu trúc tài liệu

- `docs/DATABASE.md` — schema đầy đủ (mô hình B), quan hệ, constraint
- `docs/API.md` — endpoint, request/response, status code
- `docs/SPRINTS.md` — ticket + assignee + dependency + **status** (đồng bộ Jira)
- `docs/CONVENTIONS.md` — coding style, naming, git
- `docs/SETUP.md` — cách chạy local, env, migration
- `docs/DEPLOY.md` — hạ tầng production (AWS Lightsail), CI/CD, vận hành, truy cập DB, billing
- `docs/CHANGELOG.md` — lịch sử quyết định thiết kế

## Nguyên tắc khi code

1. Bám đúng phase hiện tại ở trên. Việc ngoài scope (webhook realtime) → hỏi trước. Mở rộng Jira thì OK (đã có nền tảng BE+FE) — đối chiếu code hiện có.
2. Tuân thủ quy ước nền tảng (ID/timestamp/enum/cascade).
3. Theo layered architecture, dùng DTO.
4. Validate input; trả status code đúng (xem `docs/API.md`). Write-back: 403 thiếu scope, 409 conflict ETag, 502 provider lỗi.
5. Không hardcode secret — đọc từ config / env.
6. **Sau khi hoàn thành bất kỳ task code nào, cập nhật các file .md liên quan để phản ánh trạng thái hoàn thành hiện tại của ticket/feature đó** (tối thiểu `docs/SPRINTS.md`; schema → DATABASE.md; endpoint → API.md).
7. Khi không chắc thuộc scope hay không → hỏi trước khi code.
