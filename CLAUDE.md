# Workspace Hub — Claude Code Context

> File này là context gốc cho Claude Code. Đọc file này trước, rồi tham chiếu các file trong `docs/` khi cần chi tiết. Thay đổi thiết kế gần nhất: xem `docs/CHANGELOG.md`.

## Project là gì

Web app gom email / sự kiện / file / note (và ticket ở phase Jira) từ **Google (Gmail, Calendar, Drive)** về **một nơi duy nhất**, quản lý theo **Folder context** (dự án / khách hàng / chủ đề) với giao diện **Kanban 3 cột** (Cần xem / Đang xử lý / Done). Có chia sẻ folder (Viewer-only), hẹn giờ gửi email, và dashboard admin.

Đây là đồ án môn học (PRN232 — Fullstack ASP.NET, 60% backend / 40% frontend).

## Scope & Phase — ĐỌC KỸ

App gom + **đồng bộ 2 chiều** (đọc + ghi ngược lên provider). Hầu hết đã xong; vài ticket còn "In Review" nhưng code đã merge. Status từng ticket: `docs/SPRINTS.md` (⚠️ SPRINTS.md dòng "Phase Jira chưa code" đã stale — Jira đã code, xem dưới).

### Đã làm — Google + nền tảng
- **Mô hình connection B:** mỗi service = 1 row `Connections`, token riêng, authorize riêng. ✅ (SCRUM-34)
- **Google Sign-In** (đăng nhập, tách connect-để-sync) ✅ + **OAuth per-service** scope read-write ✅ (SCRUM-32/33/35/36).
- **Sync on-demand** (Gmail/Calendar/Drive → Item; KHÔNG cron pull định kỳ) ✅ (SCRUM-16).
- **Write-back Google:** Email (label/read/star/trash + gửi mới, KHÔNG sửa nội dung), Event (CRUD), File (rename/trash); conflict `Items.ETag`→409 ✅ (SCRUM-37 In Review / 38 Done).
- **Scheduled email** (tạo/list/cancel + cron `POST /api/internal/process-scheduled`) ✅, đã deploy prod.
- **FE đầy đủ:** Inbox, Kanban drag-drop + Folder, write-back UI, scheduled email, integrations, **admin dashboard**; **Tags** (SCRUM-70) ✅.

### Jira / Atlassian — **ĐÃ CODE** (không còn "chưa bắt đầu")
Tích hợp theo mô hình B (Atlassian = 1 Integration; mỗi Jira account = Connection `ServiceType=Jira`; issue → Item type `Ticket`):
- **BE done:** OAuth 3LO + cloudId (54) qua `JiraStrategy`; sync issue→Item (55) `JiraSyncService`+`JiraItemMapper`; tạo/sửa/xoá/transition issue qua `IJiraGateway` (`CreateIssue/UpdateIssue/DeleteIssue/GetTransitions/TransitionIssue`, 56-58); metadata `GET /api/jira/{projects,issue-types,priorities,assignable-users,transitions}` (59) `JiraMetadataService`. Đã đăng ký DI đầy đủ.
- **FE done:** Ticket write-back (assignee/priority/transition/comment) trong Kanban/Items (SCRUM-46, PR #69).
- **Seed `Integrations` Atlassian mặc định `IsEnabled=false`** (cố ý) — bật/tắt **runtime** qua admin toggle `PATCH /api/admin/integrations/{key}/enable` (SCRUM-40), **KHÔNG cần migration**. Cần config `OAuth:atlassian:ClientId/Secret` để hoạt động thật.
- SCRUM-60 (ImportantContacts JiraAccount + Notification): xác nhận status ở board Jira / `docs/SPRINTS.md`.

### NGOÀI scope (đừng code, chỉ roadmap — hỏi trước)
- Webhook/push realtime (Gmail watch + Pub/Sub, Calendar/Drive/Jira watch) → **chưa có ticket**.
- Social / friend system, AI workflow → future.

Task có vẻ cần **webhook** → dừng lại hỏi (nhiều khả năng vượt scope).

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
