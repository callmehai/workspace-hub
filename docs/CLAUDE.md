# Workspace Hub — Claude Code Context

> File này là context gốc cho Claude Code. Đọc file này trước, rồi tham chiếu các file trong `docs/` khi cần chi tiết. Thay đổi thiết kế gần nhất: xem `docs/CHANGELOG.md`.

## Project là gì

Web app gom email / sự kiện / file / note từ **Google (Gmail, Calendar, Drive)** về **một nơi duy nhất**, quản lý theo **Folder context** (dự án / khách hàng / chủ đề) với giao diện **Kanban 3 cột** (Cần xem / Đang xử lý / Done). Có chia sẻ folder (Viewer-only), hẹn giờ gửi email, và dashboard admin.

Đây là đồ án môn học (PRN232 — Fullstack ASP.NET, 60% backend / 40% frontend).

## Scope & Phase — ĐỌC KỸ

App đồng bộ **2 chiều** với Google (đọc + ghi ngược). Triển khai **theo Sprint**, đừng làm hết một lúc. Status chi tiết từng ticket: `docs/SPRINTS.md` (đồng bộ Jira).

### Đã xong (Sprint 1–2)
- **Nền tảng + Auth:** solution clean-architecture, EF schema + migrations, Data Protection, register/login (BCrypt+JWT), JWT middleware + RBAC, Google Sign-In. (SCRUM-5→14, 32/33)
- **Sync đọc Google:** Gmail client → Item, Calendar + Drive. **Sync theo nhu cầu (on-demand/lazy)** — xem mục dưới. (SCRUM-15/16/17)
- **Mô hình connection B:** mỗi service (Gmail/GCal/Drive) = 1 row `Connections` riêng, token riêng, authorize riêng từng service. (SCRUM-34/35/36)
- **Workspace:** Folder CRUD, Items list/filter/search/pagination, Kanban + Note + ItemFolders. (SCRUM-18/19/20)
- **Admin:** users list + stats, bật/tắt integration; bỏ DB credentials (đọc `OAuth:` từ config/env). (SCRUM-23, 39, 40)
- **Hardening:** exception middleware + error format chuẩn, logging + tối ưu query. (SCRUM-24/25)

### Sprint hiện hành (Sprint 3) — write-back hoàn thiện + scheduled email + bắt đầu FE
- **Write-back Google:** thao tác trên app ghi ngược lên Google ngay (synchronous). 🔍 SCRUM-37 **In Review** (Vũ).
  - Ghi được: Email (label/read/star/trash + gửi mới — **KHÔNG sửa nội dung**, Gmail immutable), Event (CRUD đầy đủ), File (rename/trash).
  - Conflict qua `Items.ETag` → 409. ⏳ SCRUM-38 (Lộc) — chốt `IWriteBackGuard` với Vũ trước.
- **Scheduled email:** tạo/list/cancel + cron gửi qua Gmail. ⏳ SCRUM-30/31.
- **Chất lượng:** refactor service (26), API testing + Postman (27), README backend (28), unit test service (29).
- **Bắt đầu FE:** API layer axios+JWT+TanStack Query (41), wire Login/Register (42), Connections page (43).

### Sprint 4 — Frontend đầy đủ + deploy + nghiệm thu
Inbox/Kanban/write-back UI/scheduled UI/admin dashboard (44→50), deploy prod (51), finalize Swagger+E2E (52), defense (53). Đừng nhảy vào trừ khi Sprint 3 xong.

### Phase Jira — đã lên kế hoạch, CHƯA bắt đầu code (SCRUM-54→60)
Tích hợp **Jira / Atlassian** (CRUD đầy đủ) đã có 7 ticket trên board (SCRUM-54→60, To Do, backlog) nhưng **chưa code** — current phase vẫn dừng ở SCRUM-38. Mô hình B áp dụng nguyên: Atlassian = 1 Integration, mỗi Jira account = 1 Connection (ServiceType=Jira). Gồm OAuth 3LO + cloudId (54), client + sync issue → Item(Ticket) (55), tạo issue (56), write-back update qua `IWriteBackGuard` (57), xoá issue (58), metadata helpers (59), ImportantContacts JiraAccount + Notification (60). Chi tiết + dependency: `docs/SPRINTS.md` (bảng "Phase Jira"); quyết định kỹ thuật (ADF 2 chiều, `fields.updated` làm version-token thay ETag): `docs/CHANGELOG.md`. **Đừng bắt đầu khi Sprint 3 chưa xong — hỏi trước.**

### Mô hình sync (quan trọng — đừng nhầm)
- **Đọc = on-demand.** KHÔNG có background service pull định kỳ (đã bỏ hẳn timer/cron đọc), KHÔNG webhook trong MVP. Khi user CRUD/mở list của một connection mới check connection còn Active + Enabled + token còn hạn (refresh nếu cần) rồi pull. (SCRUM-16)
- **Cron chỉ dùng cho gửi scheduled email** (`/api/internal/process-scheduled`, SCRUM-31) — không liên quan đọc dữ liệu.
- **Ghi = write-back synchronous** ngay khi user thao tác (SCRUM-37).

### NGOÀI scope — chưa có ticket (đừng code, đừng gán số SCRUM)
- Webhook/push realtime (Gmail watch + Pub/Sub, Calendar/Drive/Jira watch).
- Social / friend system, AI workflow.

> ⚠️ Số SCRUM-39→46 **không còn** là webhook/Jira — đó là việc đã/đang làm khác (xem SPRINTS.md). **Jira/Atlassian giờ ĐÃ có ticket = SCRUM-54→60** (phase Jira đã lên kế hoạch, chưa code). **Webhook** vẫn chưa có ticket. Nếu một task có vẻ cần webhook, hoặc đụng phase Jira khi Sprint 3 chưa xong, **dừng lại và hỏi**.

## Tech Stack

- **Backend:** ASP.NET Core Web API (.NET 8), Entity Framework Core — solution 4 project: `Domain` / `Application` / `Infrastructure` / `Api` (xem `backend/README.md`)
- **Database:** SQL Server (EF Core `UseSqlServer`; dev chạy Docker `wh-sqlserver`). JSON lưu `nvarchar(max)`, datetime lưu `datetime2` (UTC). Dev + prod cùng SQL Server.
- **Frontend:** Vite + React + TypeScript + Tailwind + React Router + TanStack Query + axios + react-hot-toast (SPA gọi REST).
- **Auth:** JWT Bearer + Google Sign-In (đăng nhập bằng Google, tách khỏi connect-để-sync)
- **Token encryption:** ASP.NET Data Protection (`IDataProtectionProvider`) — KHÔNG tự viết AES, KHÔNG lưu key trong DB
- **Deploy:** <!-- CHỐT: Render / Vercel / Azure? (SCRUM-51) -->

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
- `docs/CHANGELOG.md` — lịch sử quyết định thiết kế

## Nguyên tắc khi code

1. Bám đúng Sprint hiện hành ở trên. Việc ngoài scope (webhook/Jira) → hỏi trước.
2. Tuân thủ quy ước nền tảng (ID/timestamp/enum/cascade).
3. Theo layered architecture, dùng DTO.
4. Validate input; trả status code đúng (xem `docs/API.md`). Write-back: 403 thiếu scope, 409 conflict ETag, 502 provider lỗi.
5. Không hardcode secret — đọc từ config / env.
6. **Sau khi hoàn thành bất kỳ task code nào, cập nhật các file .md liên quan để phản ánh trạng thái hoàn thành hiện tại của ticket/feature đó** (tối thiểu `docs/SPRINTS.md`; schema → DATABASE.md; endpoint → API.md).
7. Khi không chắc thuộc scope hay không → hỏi trước khi code.
