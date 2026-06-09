# Coding Conventions — Workspace Hub

## Kiến trúc layered

```
src/
  WorkspaceHub.Api/          # Controllers, middleware, Program.cs, DI
  WorkspaceHub.Application/   # Services, DTOs, interfaces, validation
  WorkspaceHub.Domain/        # Entities, enums
  WorkspaceHub.Infrastructure/# DbContext, repositories, EF config, migrations, external clients (Gmail...)
```
> Chốt: **clean architecture 4 project** dưới `src/`. SCRUM-5 dựng đúng 4 project này.

**Luồng phụ thuộc:** Api → Application → Domain; Infrastructure → Application/Domain. Domain không phụ thuộc gì.

## Naming

- **Class/Method/Property:** PascalCase.
- **Local var/param:** camelCase.
- **Interface:** tiền tố `I` (`IItemService`, `IUserRepository`).
- **Async method:** hậu tố `Async` (`GetByIdAsync`).
- **DTO:** hậu tố `Dto`/`Request`/`Response` (`CreateFolderRequest`, `ItemResponse`).
- **Entity = số ít** (`Item`, `Folder`); **DbSet = số nhiều** (`Items`, `Folders`).

## Controller

- Mỏng: nhận request → gọi service → trả kết quả. KHÔNG business logic.
- Trả `ActionResult<T>` với status code đúng (xem API.md).
- Lấy UserId từ JWT claim qua một base controller / helper, không tin tham số client gửi.
- `[Authorize]` cho route cần auth; `[Authorize(Roles="Admin")]` cho admin.

## Service

- Chứa toàn bộ business logic + validation rule.
- Nhận/trả DTO, không trả entity ra ngoài.
- Throw custom exception (vd `NotFoundException`, `ConflictException`, `BusinessRuleException`) → middleware map sang status code.

## Repository

- Chỉ data access. Async toàn bộ.
- `AsNoTracking()` cho query read-only.
- Tránh N+1: dùng `Include`/projection hợp lý.

## DTO & Validation

- Mọi input qua DTO + validate (FluentValidation hoặc DataAnnotations).
- KHÔNG bind thẳng entity từ request body.
- Validate: email format, password ≥ 8, required field, enum hợp lệ.

## EF Core

- Enum: `.HasConversion<string>()`.
- Guid PK cho mọi entity. Role lưu cột string trên `Users` (1 user 1 role), không bảng `Roles`/`UserRoles`.
- UTC: cấu hình `DateTimeKind.Utc` cho mọi datetime (`datetime2`).
- `nvarchar(max)` (SQL Server) cho MetadataJson, ToJson...
- Mỗi thay đổi schema = một migration mới, đặt tên có nghĩa (`AddScheduledEmails`).
- Composite PK cho junction qua `HasKey(x => new { x.A, x.B })`.

## Error handling

- Một exception middleware tập trung (SCRUM-24) bắt mọi lỗi → error format chuẩn + traceId.
- KHÔNG để stack trace lộ ra client ở production.
- KHÔNG nuốt exception im lặng — log lại.

## Bảo mật

- Token OAuth: encrypt qua Data Protection trước khi lưu. KHÔNG tự viết AES.
- KHÔNG hardcode secret/connection string — đọc từ config/env/user-secrets.
- Token trong response luôn mask.
- BCrypt cost 12 cho password.

## Git

- **Branch:** `main` (ổn định), `develop` (tích hợp), `feature/SCRUM-x-mo-ta`.
- **Commit:** gắn mã ticket. VD: `SCRUM-9: add register endpoint with BCrypt`.
- **PR:** vào develop, cần ≥1 review trước merge. Mô tả PR nêu ticket + tóm tắt thay đổi.
- KHÔNG commit secret, `appsettings.*.json` chứa key, `bin/`, `obj/`.

## Khi Claude Code làm việc

- Bám scope Sprint 1–3 (xem CLAUDE.md). Không thêm webhook/2 chiều/Jira/social/AI.
- Trước khi tạo entity/endpoint mới, đối chiếu DATABASE.md và API.md — đã có sẵn spec, theo đúng đó.
- Khi sửa schema, tạo migration, đừng sửa migration cũ đã commit.
- Không chắc thuộc scope → hỏi.
