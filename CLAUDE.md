# Workspace Hub — Claude Code Context

> File này là context gốc cho Claude Code. Đọc file này trước, rồi tham chiếu các file trong `docs/` khi cần chi tiết.

## Project là gì

Web app gom email / sự kiện / file / note từ Google (Gmail, Calendar, Drive) về **một nơi duy nhất**, quản lý theo **Folder context** (dự án / khách hàng / chủ đề) với giao diện **Kanban 3 cột** (Cần xem / Đang xử lý / Done). Có chia sẻ folder (Viewer-only), hẹn giờ gửi email, và dashboard admin.

Đây là đồ án môn học (PRN232 — Fullstack ASP.NET, 60% backend / 40% frontend).

## Scope HIỆN TẠI (Sprint 1–3) — đọc kỹ

Chỉ làm những thứ sau. **KHÔNG** tự ý thêm các tính năng ngoài scope kể cả khi thấy "hợp lý":

- Auth: register / login / JWT / role (User, Admin)
- OAuth Google: connect, lưu token encrypted, list/disconnect/refresh
- Sync **1 CHIỀU** (pull/cron): Gmail → Item. Calendar/Drive là stretch.
- Folder CRUD + Kanban + Note + Tag + filter/pagination/search
- Scheduled email (hẹn giờ gửi qua Gmail)
- Admin dashboard (list user, stats, lock/unlock)
- Frontend: auth pages, layout, CRUD pages

### NGOÀI scope hiện tại (đừng code, chỉ để tham khảo roadmap)
- Realtime 2 chiều / webhook / ghi ngược lên Google → **KHÔNG làm bây giờ**. Sync chỉ 1 chiều bằng cron.
- Jira / Atlassian integration → future
- Social / friend system → future
- AI workflow (mail → tạo lịch) → future

Nếu một task có vẻ cần webhook hoặc ghi ngược, **dừng lại và hỏi** — nhiều khả năng đang vượt scope.

## Tech Stack

<!-- CHỐT GIÚP: điền nốt Deploy rồi xoá comment này -->
- **Backend:** ASP.NET Core Web API (.NET 8), Entity Framework Core
- **Database:** SQL Server (EF Core `UseSqlServer`). JSON lưu `nvarchar(max)`, datetime lưu `datetime2` (UTC). Dev + prod cùng SQL Server.
- **Frontend:** Vite + React + TypeScript + Tailwind + React Router + TanStack Query + axios + react-hot-toast (SPA gọi REST).
- **Auth:** JWT Bearer
- **Token encryption:** ASP.NET Data Protection (`IDataProtectionProvider`) — KHÔNG tự viết AES, KHÔNG lưu key trong DB
- **Deploy:** <!-- CHỐT: Render / Vercel / Azure? -->

## Kiến trúc

Layered / Clean: **Controller (API) → Service (business logic) → Repository (data access)**.
- Controller mỏng, không chứa business logic.
- DTO tách khỏi Entity — không expose entity trực tiếp ra API.
- Async/await toàn bộ data access.
- DI cho mọi service/repository (không `new` trực tiếp trong controller).

## Quy ước nền tảng (BẮT BUỘC tuân thủ)

- **ID:** `Guid` cho mọi entity.
- **Role:** 1 user = 1 role. Lưu cột `Users.Role` (string `Admin`/`User`), KHÔNG bảng `Roles`/`UserRoles`. Đẩy vào JWT claim `role`.
- **Timestamp:** UTC. SQL Server `datetime2`, EF `DateTimeKind.Utc`. Convert timezone ở frontend.
- **Soft delete:** KHÔNG dùng. Dùng `IsArchived`. Xoá thật khi Delete.
- **Enum:** lưu dạng string (`.HasConversion<string>()`).
- **JSON:** SQL Server `nvarchar(max)`. Parse ở frontend.
- **Cascade:** ON DELETE CASCADE cho child của User/Folder/OAuthConnection. SET NULL cho `Items.ServiceConnectionId`.
- **Unique:** junction → composite PK. Cột Key/slug → UNIQUE. Mọi FK có index.

## Cấu trúc tài liệu

- `docs/DATABASE.md` — schema đầy đủ, quan hệ, constraint
- `docs/API.md` — endpoint, request/response, status code
- `docs/SPRINTS.md` — ticket SCRUM-5→31, ai làm gì, dependency
- `docs/CONVENTIONS.md` — coding style, naming, git
- `docs/SETUP.md` — cách chạy local, env, migration

## Nguyên tắc khi code

1. Bám đúng scope Sprint 1–3 ở trên.
2. Tuân thủ quy ước nền tảng (ID/timestamp/enum/cascade).
3. Theo layered architecture, dùng DTO.
4. Validate input; trả status code đúng (xem `docs/API.md`).
5. Không hardcode secret — đọc từ config / env.
6. Khi không chắc thuộc scope hay không → hỏi trước khi code.
