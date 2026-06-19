# Coding Conventions — Workspace Hub

> Cập nhật ghi chú mô hình B + write-back. Phần naming/layer/git giữ nguyên.

## Kiến trúc layered
```
src/
  WorkspaceHub.Api/            # Controllers, middleware, Program.cs, DI
  WorkspaceHub.Application/     # Services, DTOs, interfaces, validation, OAuth, Sync, WriteBack
  WorkspaceHub.Domain/          # Entities, enums, metadata models
  WorkspaceHub.Infrastructure/  # DbContext, repos, EF config, migrations, provider clients
```
Luồng: Api → Application → Domain; Infrastructure → Application/Domain.

## Naming
PascalCase (class/method/property), camelCase (local/param), `I` prefix (interface), `Async` suffix, DTO/Request/Response suffix. Entity số ít, DbSet số nhiều.

## Controller / Service / Repository
- Controller mỏng, trả ActionResult<T> + status đúng. UserId từ JWT claim.
- **Base controller duy nhất: `ApiControllerBase`** — cung cấp route `api/[controller]`, `CurrentUserId`, `CurrentUserRole`. KHÔNG tạo base controller thứ hai, KHÔNG khai báo lại `[Route]` trên controller con (trừ khi route khác convention).
- **Validation:** gọi `ValidateAndThrowAsync` (FluentValidation) — `ExceptionMiddleware` format lỗi 400 chuẩn. KHÔNG tự format validation error trong controller.
- Service chứa business logic, nhận/trả DTO, throw custom exception (middleware map status code).
- Repository chỉ data access, async, AsNoTracking cho read, tránh N+1.

## EF Core
- Enum string, Guid PK, UTC (`datetime2`), JSON lưu `nvarchar(max)` (SQL Server) cho MetadataJson/ToJson/SupportedServices.
- Migration mới mỗi thay đổi schema, KHÔNG sửa migration đã commit.
- Composite PK junction.

## Mô hình B — Connections (quan trọng)
- Mỗi service = 1 row Connections, token riêng. KHÔNG còn OAuthConnection→ServiceConnection.
- Scope KHÔNG lưu DB — suy từ ServiceType qua `GoogleScopes.BuildRequestScopes()`. Một nguồn scope duy nhất trong code.
- KHÔNG thêm cột Permission/AccessLevel — bật service là full quyền.
- Disconnect = xoá đúng row Connection, Items.ConnectionId SET NULL.

## Auth — 2 luồng tách biệt
- **Google Sign-In:** verify id_token (`GoogleJsonWebSignature.ValidateAsync`, KHÔNG tự decode), không tạo Connection, chỉ User + JWT. Auto-link theo email.
- **Connect service:** tạo Connection, full scope. Khác hẳn login.

## Write-back (2 chiều)
- PATCH /api/items/{id} phân nhánh theo Type. Email KHÔNG sửa nội dung (reject field ngoài label/read/star/trash).
- Mọi write-back đi qua `IWriteBackGuard` (so ETag) trước khi ghi provider. Lệch → ConflictException → 409.
- Thiếu scope → 403 + gợi ý reconnect. Provider lỗi → 502.

## Bảo mật
- Token encrypt qua Data Protection (không tự viết AES). Token response luôn mask.
- BCrypt cost 12. Không hardcode secret (config/env/user-secrets) — kể cả fallback "dev only": thiếu `Jwt:Secret` thì app phải fail lúc startup.

## Frontend
- **1 axios instance duy nhất: `src/lib/api.ts`** (JWT interceptor + xử lý 401 tập trung). Token đọc/ghi qua `tokenStore` (key `wh_token`) — KHÔNG gọi `localStorage` trực tiếp, KHÔNG tạo instance thứ hai.
- Server state qua TanStack Query (`useQuery`/`useMutation`) — KHÔNG `useEffect + fetch/axios` thủ công, KHÔNG `useState loading` tự quản.
- `useAuth` import từ `src/hooks/useAuth`; context khai báo ở `src/context/auth-context.ts`, provider ở `src/context/AuthContext.tsx`.
- Type API response khai báo trong `src/types/` và phải khớp DTO backend (vd `AuthResponse.accessToken`).

## Git
- Branch: main / develop / feature/SCRUM-x-mo-ta.
- Commit gắn mã ticket: `SCRUM-37: add email write-back`.
- PR vào develop, ≥1 review. Không commit secret/bin/obj.

## Cập nhật tài liệu (BẮT BUỘC)

> **Sau khi hoàn thành bất kỳ task code nào, cập nhật các file .md liên quan để phản ánh trạng thái hoàn thành hiện tại của ticket/feature đó** — tối thiểu: status ticket trong `docs/SPRINTS.md`; nếu đổi schema → `docs/DATABASE.md`; đổi endpoint/response → `docs/API.md`; quyết định thiết kế lớn → `docs/CHANGELOG.md`.

## Khi Claude Code làm việc
- Bám phase hiện tại (CLAUDE.md). Phase sau (webhook/Jira) → hỏi.
- Đối chiếu DATABASE.md + API.md trước khi tạo entity/endpoint.
- Mô hình B: đừng tạo lại ServiceConnections cũ.
- Xong task → cập nhật .md theo rule trên.
- Không chắc → hỏi.
