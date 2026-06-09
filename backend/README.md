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

## Trạng thái

✅ SCRUM-5 (solution + clean architecture + health endpoint)
✅ SCRUM-6 (13 entity + migration `InitialCreate` + seed Google)
⏭️ Kế: SCRUM-9 (register/login/JWT)
