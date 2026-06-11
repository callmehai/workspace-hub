# Workspace Hub — Claude Code Context

> Bản đồng bộ của `CLAUDE.md` ở root (root là bản chính thức được load tự động). Sửa ở đâu thì sync sang bên kia.
> Thay đổi gần nhất: xem `docs/CHANGELOG.md`.

## Project là gì

Web app gom email / sự kiện / file / note / ticket từ **Google (Gmail, Calendar, Drive)** và **Jira** về một nơi, quản lý theo **Folder context** với **Kanban 3 cột** (Cần xem / Đang xử lý / Done). Có chia sẻ folder, hẹn giờ gửi email, dashboard admin.

Đồ án PRN232 (Fullstack ASP.NET, 60% BE / 40% FE).

## Scope & Phase — ĐỌC KỸ

App hướng tới **đồng bộ 2 chiều** (đọc + ghi ngược lên provider) với cả Google và Jira. Nhưng triển khai **theo phase**, đừng làm hết một lúc:

### Phase hiện tại (đang làm) — Mô hình B + Write-back Google
- **Mô hình connection B:** mỗi service = 1 connection riêng, token riêng (xem DATABASE.md).
- **2 chiều cho Google bằng polling + write-back:** cron đọc như cũ; thao tác trên app ghi ngược lên Google ngay (synchronous).
- Ghi được: Email (label/read/star/trash + gửi mới — KHÔNG sửa nội dung), Event (CRUD đầy đủ), File (rename/trash).
- Conflict qua ETag → 409.
- Ticket: SCRUM-34 ✅ (đã migrate DB) → 35→38 ⏳ (+ 30/31 scheduled email làm sau 38). Status: `docs/SPRINTS.md`.

### Phase sau (CHƯA làm — đừng code) — Webhook & Jira
- Webhook/push (Gmail watch + Pub/Sub, Calendar/Drive watch) thay polling chiều đọc.
- Bảng WebhookChannels + cron renew.
- Jira: OAuth Atlassian (cloudId), sync issue → Item(Ticket), write-back (transition/assign/comment), Jira webhook.
- Ticket: SCRUM-39→46 (backlog, chưa assign).

**Nếu một task thuộc phase sau (webhook, Jira) → dừng và hỏi.** Phase hiện tại dừng ở SCRUM-38.

## Tech Stack

- **Backend:** ASP.NET Core Web API (.NET 8), EF Core — solution 4 project (Domain/Application/Infrastructure/Api)
- **Database:** SQL Server (dev chạy Docker `wh-sqlserver`; JSON `nvarchar(max)`, datetime `datetime2` UTC)
- **Frontend:** Vite + React + TypeScript + Tailwind + React Router + TanStack Query + axios
- **Auth:** JWT Bearer + Google Sign-In (đăng nhập bằng Google, tách khỏi connect-để-sync)
- **Token encryption:** ASP.NET Data Protection (`IDataProtectionProvider`)
- **Deploy:** <!-- CHỐT -->

## Kiến trúc
Layered: **Controller → Service → Repository**. Controller mỏng. DTO tách Entity. Async toàn bộ. DI cho mọi service/repo.

## Quy ước nền tảng (BẮT BUỘC)
- **ID:** Guid cho mọi entity.
- **Role:** cột `Users.Role` string (Admin/User), KHÔNG bảng Roles/UserRoles.
- **Timestamp:** UTC (`datetime2`).
- **Soft delete:** không dùng, dùng IsArchived; xoá thật khi Delete.
- **Enum:** string (`.HasConversion<string>()`).
- **JSON:** `nvarchar(max)` (SQL Server).
- **Cascade:** child của User/Folder CASCADE; `Items.ConnectionId`/`ScheduledEmails.ConnectionId` NoAction ở DB (SQL Server cấm multiple cascade path) — service layer set NULL khi disconnect.
- **Unique:** junction composite PK; Key/slug UNIQUE; FK có index.

## Hai khái niệm dễ nhầm — phân biệt rõ
1. **Google Sign-In** = đăng nhập vào app (scope openid/email/profile). KHÔNG tạo Connection, chỉ tạo/tìm User + phát JWT. Disconnect service KHÔNG làm logout.
2. **Connect service** = cấp quyền đọc/ghi Gmail/Drive/Calendar/Jira. Tạo 1 Connection (mô hình B). Bật service = cấp FULL scope của service đó (không read-only, scope do dev quyết).

## Không thêm cột thừa
- KHÔNG thêm cột `Permission`/`AccessLevel` vào Connections — bật là full, không có "một phần quyền".
- KHÔNG thêm cột `Scopes` vào Connections — scope suy từ ServiceType (cố định mỗi service).

## Tài liệu
- `docs/DATABASE.md` — schema (mô hình B)
- `docs/API.md` — endpoint
- `docs/SPRINTS.md` — ticket + assignee + dependency
- `docs/CONVENTIONS.md` — coding style, git
- `docs/SETUP.md` — chạy local, OAuth setup
- `docs/CHANGELOG.md` — lịch sử thay đổi thiết kế

## Nguyên tắc khi code
1. Bám phase hiện tại. Phase sau (webhook/Jira) → hỏi trước.
2. Tuân thủ quy ước nền tảng.
3. Layered + DTO.
4. Status code đúng (API.md). Write-back: 403 thiếu scope, 409 conflict, 502 provider lỗi.
5. Không hardcode secret.
6. **Xong task code → cập nhật các .md liên quan** (tối thiểu status trong SPRINTS.md; schema → DATABASE.md; endpoint → API.md).
7. Không chắc thuộc scope → hỏi.
