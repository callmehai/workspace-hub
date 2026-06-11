# Workspace Hub — Backend

ASP.NET Core 8 Web API, EF Core 8, SQL Server. Kiến trúc **clean / layered**.

> 📖 **Cách chạy local đầy đủ (Windows + macOS):** xem [`../docs/SETUP.md`](../docs/SETUP.md).

## Quick start

```bash
cd backend
cp src/WorkspaceHub.Api/appsettings.Development.json.example src/WorkspaceHub.Api/appsettings.Development.json
#   → sửa ConnectionStrings:Default cho khớp SQL Server của bạn (xem docs/SETUP.md mục 3)
dotnet restore
dotnet ef database update --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api
dotnet run --project src/WorkspaceHub.Api
# → http://localhost:5118/swagger ; thử GET /api/health
```

## Cấu trúc (4 project)

```
src/
  WorkspaceHub.Domain/          # Entities, Enums — không phụ thuộc gì
  WorkspaceHub.Application/      # Services, DTOs, interface repository/service
  WorkspaceHub.Infrastructure/  # DbContext, EF config, repositories, Migrations
  WorkspaceHub.Api/             # Controllers, Program.cs (DI), appsettings
```
Luồng phụ thuộc: **Api → Application → Domain**, **Infrastructure → Application/Domain**.
Request đi: **Controller → Service → Repository → DbContext**. DTO tách khỏi Entity.

## Quy ước

- Guid PK; enum lưu string; datetime `datetime2` UTC; JSON `nvarchar(max)`.
- 1 user = 1 role (cột `Users.Role`), không bảng Roles/UserRoles.
- DI cho mọi service/repository (không `new` trong controller).
- Chi tiết: [`../docs/CONVENTIONS.md`](../docs/CONVENTIONS.md), schema: [`../docs/DATABASE.md`](../docs/DATABASE.md), API: [`../docs/API.md`](../docs/API.md).

## Lệnh EF

```bash
dotnet ef migrations add <Tên> --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api
dotnet ef database update       --project src/WorkspaceHub.Infrastructure --startup-project src/WorkspaceHub.Api
```

> Luôn kèm `--startup-project` (hoặc set env `WORKSPACEHUB_CONNECTION`) — không thì design-time factory rơi về fallback `Trusted_Connection` → lỗi Kerberos trên macOS.

Migrations hiện có: `InitialCreate` → `UsersMultiAuth` → `ModelBConnections` (mô hình B: gộp OAuthConnections + ServiceConnections thành `Connections`, Items.ConnectionId + ETag).

## Trạng thái (2026-06-11 — chi tiết: ../docs/SPRINTS.md)

✅ SCRUM-5→13: solution, schema + migrations, Data Protection, auth (register/login/JWT/me/role), OAuth Google start + callback
✅ SCRUM-18/19: Folder CRUD, Items list + filter + pagination + search
✅ SCRUM-32/33/34: Users multi-auth, Google Sign-In, migration mô hình B
⏭️ Kế: SCRUM-35/36 (OAuth per-service) → 37 (write-back) + 38 (conflict ETag) → 30/31 (scheduled email). Nợ: SCRUM-14 viết lại theo Connections.
