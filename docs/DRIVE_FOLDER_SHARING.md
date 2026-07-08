# Google Drive — Tạo folder & Chia sẻ (SCRUM-79)

> **Ticket:** SCRUM-79  
> **Trạng thái:** Spec — chưa code  
> **Đọc file này khi:** bắt đầu implement tính năng Drive folder + sharing

---

## TL;DR — Tóm tắt 30 giây

App hiện **đọc file từ Drive** và **đổi tên / xoá** file. Ticket này thêm:

1. **Tạo folder** trên Google Drive (không phải folder Kanban trong app).
2. **Chia sẻ** file/folder Drive: thêm email, đổi quyền, gỡ quyền, bật link công khai.

Mọi thao tác **ghi thẳng lên Google** ngay (giống đổi tên file hiện tại). **Không** tạo bảng DB mới cho quyền — hỏi Google mỗi lần mở dialog Share.

---

## Mục lục

1. [Tính năng là gì? (giải thích dễ hiểu)](#1-tính-năng-là-gì-giải-thích-dễ-hiểu)
2. [Hai loại "folder" — đừng nhầm](#2-hai-loại-folder--đừng-nhầm)
3. [Quyết định đã chốt (owner)](#3-quyết-định-đã-chốt-owner)
4. [App đang có gì / thiếu gì](#4-app-đang-có-gì--thiếu-gì)
5. [User sẽ thấy gì trên UI](#5-user-sẽ-thấy-gì-trên-ui)
6. [Luồng hoạt động (khi user bấm nút)](#6-luồng-hoạt-động-khi-user-bấm-nút)
7. [API cần làm (spec)](#7-api-cần-làm-spec)
8. [Hướng dẫn implement — từng bước](#8-hướng-dẫn-implement--từng-bước)
9. [Kiểm tra khi xong (checklist QA)](#9-kiểm-tra-khi-xong-checklist-qa)
10. [Lỗi thường gặp & cách xử lý](#10-lỗi-thường-gặp--cách-xử-lý)
11. [Không làm trong v1](#11-không-làm-trong-v1)
12. [File tham chiếu trong repo](#12-file-tham-chiếu-trong-repo)

---

## 1. Tính năng là gì? (giải thích dễ hiểu)

### 1.1 Tạo folder trên Drive

User bấm **"Tạo folder"** → app gọi Google → folder xuất hiện trên **Google Drive thật** → app cũng tạo một **Item** (loại File) để hiện trong Inbox/Kanban.

- Tạo ở **gốc My Drive** hoặc **bên trong folder cha** (nếu chọn parent).
- Folder mới **không cần chờ cron sync** — tạo Item local ngay sau khi Google trả về.

### 1.2 Chia sẻ file/folder Drive

Giống nút **Share** trên Google Drive web:


| Việc user làm                 | Tương đương Google Drive              |
| ----------------------------- | ------------------------------------- |
| Thêm email + chọn quyền       | Mời người xem / bình luận / chỉnh sửa |
| Xem danh sách người có quyền  | Panel "People with access"            |
| Đổi quyền / Gỡ ai đó          | Sửa role hoặc Remove                  |
| Bật "Ai có link đều xem được" | Anyone with the link                  |


**Áp dụng cho mọi Item loại File** — cả file tài liệu lẫn folder Drive.

### 1.3 Write-back là gì ở đây?

**Write-back** = user thao tác trên app → app **ghi ngược lên Google ngay**, không chỉ lưu local.

Ví dụ đã có: đổi tên file → `PATCH /api/items/{id}` → Google Drive API `files.update`.

Ticket này thêm write-back kiểu tương tự nhưng qua endpoint riêng `/api/drive/*` (tạo folder + permissions).

---

## 2. Hai loại "folder" — đừng nhầm

Đây là chỗ dễ confuse nhất trong project:

```
┌──────────────────────────────────────────────┐
│  THƯ MỤC trong sidebar Workspace Hub         │
│  (bảng Folders + FolderShares)               │
│  → Gom item theo dự án / khách hàng          │
│  → Chia sẻ giữa user WH (chỉ xem)            │
│  → KHÔNG liên quan Google Drive              │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│  FOLDER trên Google Drive                    │
│  (Drive API — mimeType = ...folder)          │
│  → Sync về Item type=File, isFolder=true     │
│  → Chia sẻ qua email / link (ticket này)     │
└──────────────────────────────────────────────┘
```

**Ví dụ:** User có folder Drive "Hợp đồng 2025" sync về Inbox. User có thể **kéo Item đó vào WH Folder "Khách hàng ABC"** — hai việc độc lập.

---

## 3. Quyết định đã chốt (owner)


| #   | Câu hỏi                         | Đã chốt                                                                                           |
| --- | ------------------------------- | ------------------------------------------------------------------------------------------------- |
| 1   | Nút "Tạo folder" đặt ở đâu?     | **Cả Integrations + toolbar Inbox/Kanban** (và có thể shortcut "Tạo folder con" trong ItemDetail) |
| 2   | Link sharing ("ai có link")?    | **Có trong v1**                                                                                   |
| 3   | Share trên file hay chỉ folder? | **Mọi file** (và folder)                                                                          |
| 4   | Ticket Jira                     | **SCRUM-79**                                                                                      |


---

## 4. App đang có gì / thiếu gì

### Đã có sẵn — tái sử dụng, không viết lại


| Thành phần               | File                                           | Ghi chú                                 |
| ------------------------ | ---------------------------------------------- | --------------------------------------- |
| OAuth Drive đủ quyền ghi | `GoogleScopes.cs`                              | Scope `drive` — **không cần reconnect** |
| Sync file từ Drive       | `GoogleDriveGateway.cs`, `DriveSyncService.cs` | Folder Drive đã sync như file thường    |
| Map → Item               | `DriveItemMapper.cs`                           | `ItemType.File` + metadata              |
| Đổi tên / trash file     | `DriveGateway.cs`, `ItemWriteBackService.cs`   | Pattern write-back                      |
| UI chi tiết file         | `ItemDetail.tsx`                               | Thêm nút Share / Tạo folder con         |
| Pattern API provider     | `JiraController.cs`                            | Làm mẫu cho `DriveController`           |


### Cần làm mới


| Thành phần | Mô tả ngắn                                            |
| ---------- | ----------------------------------------------------- |
| Gateway    | Gọi Google: `files.create` (folder) + `permissions.*` |
| Service    | Kiểm tra connection Drive, map Item → `ExternalId`    |
| Controller | `/api/drive/*`                                        |
| FE         | Dialog Share, modal Tạo folder, API client            |
| Sync nhỏ   | Thêm `parents` + `isFolder` vào metadata              |


**Không cần migration DB** — quyền share không lưu SQL, chỉ hỏi Google.

---

## 5. User sẽ thấy gì trên UI

### 5.1 Tạo folder


| Vị trí                     | Hành vi                                       |
| -------------------------- | --------------------------------------------- |
| **Integrations**           | Nút "Tạo folder" trên card Google Drive       |
| **Inbox / Kanban toolbar** | Nút "Tạo folder Drive" (khi đã connect Drive) |
| **ItemDetail** (tuỳ chọn)  | Khi đang xem folder Drive → "Tạo folder con"  |


Modal gồm: chọn connection (nếu nhiều) → nhập tên → chọn parent (My Drive root hoặc folder có sẵn).

### 5.2 Chia sẻ

Trong **ItemDetail** của mọi Item `type=File`:

- Nút **"Chia sẻ"** cạnh Đổi tên / Mở trên Drive.
- Dialog:
  - Ô nhập email + dropdown quyền (Người xem / Bình luận / Chỉnh sửa).
  - Danh sách người đang có quyền (owner không sửa được).
  - Toggle **"Bất kỳ ai có đường link"** + chọn quyền link.

### 5.3 Phân biệt folder vs file trên UI

Folder Drive: icon/badge khác file thường — dựa `metadata.isFolder` hoặc `mimeType === application/vnd.google-apps.folder`.

---

## 6. Luồng hoạt động (khi user bấm nút)

### 6.1 Tạo folder

```
User bấm "Tạo folder"
    → FE gọi POST /api/drive/folders { connectionId, name, parentItemId? }
    → BE kiểm tra: connection thuộc user, là Drive, đang Active
    → BE gọi Google: files.create (mimeType = folder)
    → BE tạo Item mới trong DB (type=File, isFolder=true)
    → FE nhận Item → refresh list Inbox, toast thành công
```

### 6.2 Chia sẻ email

```
User mở dialog Share → nhập email + role
    → FE gọi POST /api/drive/items/{itemId}/permissions
    → BE: Item thuộc user → lấy ExternalId (id file trên Drive)
    → BE gọi Google: permissions.create
    → FE refresh danh sách quyền
```

### 6.3 Link công khai

```
User bật toggle "Ai có link"
    → FE gọi PUT /api/drive/items/{itemId}/link-sharing { enabled: true, role: "reader" }
    → BE tạo/cập nhật permission type=anyone trên Google
```

> **Lưu ý:** Share **không** đi qua `PATCH /api/items/{id}` và **không** dùng ETag conflict — quyền không nằm trong bảng Items.

---

## 7. API cần làm (spec)

Tất cả dưới prefix `/api/drive`, cần đăng nhập (`[Authorize]`).

### 7.1 `POST /api/drive/folders` — Tạo folder

**Gửi lên:**

```json
{
  "connectionId": "uuid-của-connection-drive",
  "name": "Tên folder",
  "parentItemId": null
}
```

- `parentItemId: null` → tạo ở gốc My Drive.
- `parentItemId: "uuid"` → tạo bên trong folder Drive đó (phải là folder, cùng connection).

**Trả về:** `201` + object Item (giống GET item).

### 7.2 `GET /api/drive/items/{itemId}/permissions` — Xem ai có quyền

**Trả về:**

```json
{
  "items": [
    {
      "id": "permId",
      "type": "user",
      "role": "writer",
      "emailAddress": "a@example.com",
      "displayName": "Nguyễn A",
      "isOwner": false,
      "isLink": false
    },
    {
      "id": "permId2",
      "type": "anyone",
      "role": "reader",
      "isOwner": false,
      "isLink": true
    }
  ]
}
```

### 7.3 `POST /api/drive/items/{itemId}/permissions` — Thêm người

```json
{ "email": "user@example.com", "role": "reader", "notify": true }
```

`role`: `reader` | `commenter` | `writer`  
`notify: true` → Google gửi email mời (giống Drive web).

### 7.4 `PATCH /api/drive/items/{itemId}/permissions/{permissionId}` — Đổi quyền

```json
{ "role": "commenter" }
```

### 7.5 `DELETE /api/drive/items/{itemId}/permissions/{permissionId}` — Gỡ quyền

Trả `204`. Không cho gỡ owner.

### 7.6 `PUT /api/drive/items/{itemId}/link-sharing` — Bật/tắt link

```json
{ "enabled": true, "role": "reader" }
```

- `enabled: false` → tắt link, xoá permission `anyone` trên Google.

### Bảng mã lỗi


| Mã  | Khi nào                                                              |
| --- | -------------------------------------------------------------------- |
| 400 | Dữ liệu gửi lên sai format                                           |
| 403 | Không đủ quyền trên file Drive / token lỗi                           |
| 404 | Item hoặc connection không tồn tại / không thuộc user                |
| 409 | Email đã được share rồi                                              |
| 422 | Connection không phải Drive, file đã trash, parent không phải folder |
| 502 | Google API lỗi                                                       |


---

## 8. Hướng dẫn implement — từng bước

**Thứ tự khuyến nghị:** làm Backend trước (test Swagger/Postman), rồi Frontend.

Ước lượng: **4–5 ngày** fullstack.

---

### Bước 0 — Chuẩn bị (15 phút)

1. Đọc xong mục 2 (đừng nhầm WH Folder vs Drive folder).
2. Mở Swagger local: `dotnet run --project backend/src/WorkspaceHub.Api` → thử `PATCH /api/items/{id}` đổi tên file Drive (hiểu pattern write-back).
3. Đảm bảo máy dev đã connect Google Drive (`ServiceType=Drive`, status Active).

---

### PHẦN A — BACKEND

#### Bước A1 — Mở rộng model nội bộ (Gateway)

**Mục tiêu:** Định nghĩa kiểu dữ liệu trao đổi giữa Service ↔ Google.

**File:** `backend/src/WorkspaceHub.Application/Abstractions/DriveModels.cs`

**Thêm class:**

- `DrivePermissionDto` — id, type, role, emailAddress, displayName, isOwner, isLink
- (Tuỳ chọn) enum `DrivePermissionRole` — Reader, Commenter, Writer

**Kiểm tra:** `dotnet build` không lỗi.

---

#### Bước A2 — Mở rộng IDriveGateway + DriveGateway

**Mục tiêu:** Gói tất cả lời gọi Google Drive API vào một chỗ (giống `DriveGateway` hiện tại).

**File sửa:**

- `Application/Abstractions/IDriveGateway.cs` — thêm method
- `Infrastructure/Services/DriveGateway.cs` — implement

**Method cần thêm:**


| Method                      | Google API                         | Ghi chú                                         |
| --------------------------- | ---------------------------------- | ----------------------------------------------- |
| `CreateFolderAsync`         | `files.create`                     | `mimeType = application/vnd.google-apps.folder` |
| `ListPermissionsAsync`      | `permissions.list`                 | Lọc bỏ `deleted=true`                           |
| `CreateUserPermissionAsync` | `permissions.create`               | `type=user`, `sendNotificationEmail`            |
| `UpdatePermissionAsync`     | `permissions.update`               | Chỉ đổi `role`                                  |
| `DeletePermissionAsync`     | `permissions.delete`               |                                                 |
| `SetLinkSharingAsync`       | create/update/delete `type=anyone` | Bật/tắt link                                    |


**Mẫu tạo folder (C#):**

```csharp
var metadata = new Google.Apis.Drive.v3.Data.File
{
    Name = name,
    MimeType = "application/vnd.google-apps.folder",
    Parents = parentId != null ? new List<string> { parentId } : null
};
var request = drive.Files.Create(metadata);
request.Fields = "id, name, mimeType, webViewLink, modifiedTime, version, parents";
```

**Bắt lỗi:** bọc `GoogleApiException` bằng `GoogleApiExceptionHandler.Handle(...)` — copy pattern từ `GetFileAsync` hiện có.

**Kiểm tra:** `dotnet build`. (Unit test gateway optional — cần token thật.)

---

#### Bước A3 — Tạo DriveSharingService

**Mục tiêu:** Business logic — ai được làm gì, Item nào map sang file Drive nào.

**File mới:**

- `Application/Interfaces/Services/IDriveSharingService.cs`
- `Application/Services/DriveSharingService.cs`

**Logic chung mọi method — `ResolveDriveItemAsync`:**

1. Lấy Item theo `itemId` + `userId` → không có → `NotFoundException`
2. Item phải `Type == File`, `ConnectionId` không null
3. Lấy Connection → phải `ServiceType.Drive`, `Status == Active`, thuộc user
4. Trả `(Item, Connection)` — dùng `item.ExternalId` gọi Google

**Method service:**


| Method                  | Việc làm                                                                                           |
| ----------------------- | -------------------------------------------------------------------------------------------------- |
| `CreateFolderAsync`     | Validate parent (nếu có) là folder → gateway create → `DriveItemMapper` → insert Item → return DTO |
| `ListPermissionsAsync`  | Resolve item → gateway list                                                                        |
| `AddPermissionAsync`    | Resolve → gateway create user permission                                                           |
| `UpdatePermissionAsync` | Resolve → không sửa owner → gateway update                                                         |
| `RemovePermissionAsync` | Resolve → không xoá owner → gateway delete                                                         |
| `SetLinkSharingAsync`   | Resolve → gateway set anyone                                                                       |


**Kiểm tra:** viết unit test mock `IDriveGateway` + `IItemRepository`:

- Connection Gmail → 422
- Item user khác → 404
- Parent không phải folder → 422

---

#### Bước A4 — DTO + Validator

**File mới** (trong `Application/DTOs/Drive/` hoặc tương tự):

- `CreateDriveFolderRequest` — Name required, max 255 ký tự
- `AddDrivePermissionRequest` — Email valid, Role enum
- `UpdateDrivePermissionRequest`
- `LinkSharingRequest` — Enabled bool, Role

**Validator:** FluentValidation, gọi `ValidateAndThrowAsync` trong service hoặc controller.

---

#### Bước A5 — DriveController

**File mới:** `Api/Controllers/DriveController.cs`

```csharp
[Authorize]
[Route("api/drive")]
public class DriveController : ApiControllerBase
{
    // POST folders
    // GET items/{itemId}/permissions
    // POST items/{itemId}/permissions
    // PATCH items/{itemId}/permissions/{permissionId}
    // DELETE items/{itemId}/permissions/{permissionId}
    // PUT items/{itemId}/link-sharing
}
```

**Nguyên tắc:** Controller mỏng — chỉ gọi service, không if/else business.

**Đăng ký DI:** `Application/DependencyInjection.cs` → `AddScoped<IDriveSharingService, DriveSharingService>()`

**Kiểm tra:**

```bash
cd backend
dotnet build && dotnet test
```

Test thủ công Swagger/Postman từng endpoint (cần cookie auth hoặc Bearer).

---

#### Bước A6 — Cập nhật sync metadata

**Mục tiêu:** FE biết đâu là folder, biết parent để tạo folder con.

**File sửa:**

1. `GoogleDriveGateway.cs` — thêm `parents` vào chuỗi `Fields` khi list/changes
2. `DriveFileDto` — property `Parents` (list string)
3. `DriveItemMapper.cs` — metadata thêm:
  ```json
   {
     "mimeType": "...",
     "isFolder": true,
     "parents": ["driveParentId"],
     "webViewLink": "...",
     ...
   }
  ```
   `isFolder = (mimeType == "application/vnd.google-apps.folder")`

**Kiểm tra:** Sync Drive → mở DB/item API → folder có `isFolder: true`.

---

#### Bước A7 — Cập nhật docs BE

Khi merge xong:

- `docs/API.md` — thêm mục Drive (SCRUM-79)
- `docs/SPRINTS.md` — SCRUM-79 status
- `docs/CHANGELOG.md` — quyết định thiết kế

---

### PHẦN B — FRONTEND

> **Làm sau khi BE chạy được trên Swagger.**

#### Bước B1 — API client

**File mới:** `frontend/src/lib/driveApi.ts`

```typescript
// createFolder(payload)
// listPermissions(itemId)
// addPermission(itemId, { email, role, notify })
// updatePermission(itemId, permissionId, { role })
// removePermission(itemId, permissionId)
// setLinkSharing(itemId, { enabled, role })
```

Dùng axios instance sẵn có (cookie + CSRF). Types đặt cạnh file hoặc `types/drive.ts`.

**Kiểm tra:** gọi thử `listPermissions` từ console hoặc page tạm.

---

#### Bước B2 — Dialog Chia sẻ

**File mới:** `frontend/src/components/DriveShareDialog.tsx`

**Props:** `itemId`, `open`, `onClose`

**Bên trong:**

1. `useQuery(['drive-permissions', itemId], () => driveApi.listPermissions(itemId))`
2. Form thêm email + select role + nút "Mời"
3. List từng permission — owner: chỉ hiển thị, không nút sửa
4. Mỗi dòng user: dropdown đổi role + nút Gỡ (confirm)
5. Section link: Switch bật/tắt + select role → `setLinkSharing`
6. `useMutation` + `toast` + `invalidateQueries` sau mỗi thao tác

**Kiểm tra:** Mở file Drive → Share → thêm email → thấy trên Google Drive web.

---

#### Bước B3 — Modal Tạo folder

**File mới:** `frontend/src/components/CreateDriveFolderModal.tsx`

**Form:**

- Connection (dropdown nếu >1 Drive connection)
- Tên folder (input)
- Parent (select: "My Drive (gốc)" hoặc list folder từ items — filter `metadata.isFolder`)

**Sau submit thành công:** `invalidateQueries(['items'])`, đóng modal, toast.

---

#### Bước B4 — Gắn nút vào UI


| File                                       | Thay đổi                                                                                                                                       |
| ------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `ItemDetail.tsx`                           | Nút "Chia sẻ" khi `type==='File'` → mở `DriveShareDialog`. Icon folder nếu `metadata.isFolder`. Shortcut "Tạo folder con" khi đang xem folder. |
| `Integrations.tsx`                         | Nút "Tạo folder" trên card Drive                                                                                                               |
| `WorkspaceToolbar.tsx` (hoặc Inbox/Kanban) | Nút "Tạo folder Drive" khi có connection Active                                                                                                |


**Kiểm tra:** Cả 2 entry point (Integrations + toolbar) đều mở được modal.

---

#### Bước B5 — i18n

**File:** `frontend/src/i18n/translations.ts`

Thêm key VI/EN cho: Chia sẻ, Tạo folder, Người xem, Bình luận, Chỉnh sửa, Ai có link, Mời, Gỡ quyền, v.v.

---

#### Bước B6 — Build & lint

```bash
cd frontend
npm run build && npm run lint
```

---

### PHẦN C — Kết thúc ticket

1. Chạy checklist mục 9 bên dưới.
2. Cập nhật `SPRINTS.md`: SCRUM-79 → Done.
3. Tạo PR vào `develop`, gắn SCRUM-79.

---

## 9. Kiểm tra khi xong (checklist QA)

Đánh dấu từng mục khi test xong:

### Tạo folder

- [ ] Tạo folder ở gốc My Drive → thấy trên Google Drive web
- [ ] Tạo folder con (chọn parent) → nằm đúng trong folder cha
- [ ] Item mới xuất hiện Inbox **không cần** chờ cron
- [ ] Nút tạo folder hoạt động từ **Integrations**
- [ ] Nút tạo folder hoạt động từ **toolbar Inbox/Kanban**

### Chia sẻ email

- [ ] Share file thường (không chỉ folder)
- [ ] Share folder Drive
- [ ] Thêm email → người nhận có quyền trên Drive web
- [ ] `notify: true` → có email mời (nếu Google gửi)
- [ ] Đổi role reader → writer
- [ ] Gỡ quyền → mất trên Drive web
- [ ] Không sửa/xoá được dòng Owner

### Link sharing

- [ ] Bật "Ai có link" → link hoạt động incognito
- [ ] Tắt link → không truy cập được nữa
- [ ] Đổi role link (reader/commenter/writer)

### Lỗi & biên

- [ ] Share file đã trash → message 422 rõ ràng
- [ ] User không phải owner/writer → 403
- [ ] Connection Drive lỗi → gợi ý reconnect

---

## 10. Lỗi thường gặp & cách xử lý


| Triệu chứng           | Nguyên nhân                                  | Cách xử lý                                         |
| --------------------- | -------------------------------------------- | -------------------------------------------------- |
| 403 khi share         | User không phải owner/writer trên file Drive | Chỉ owner mới share được — hiện message tiếng Việt |
| 502 Bad Gateway       | Google API timeout / lỗi                     | Log `GoogleApiException`, trả message từ handler   |
| Email đã có quyền     | Google trả duplicate                         | Map 409, FE toast "Email này đã được mời"          |
| Không thấy folder mới | Chỉ chờ sync, chưa tạo Item local            | Sau `files.create` phải insert Item ngay (Bước A3) |
| Parent dropdown trống | Sync chưa có `isFolder`                      | Làm Bước A6 trước hoặc song song                   |
| Nhầm WH Folder        | Dev hiểu sai scope                           | Đọc lại mục 2 — không đụng `FolderShares`          |


---

## 11. Không làm trong v1


| Tính năng                              | Lý do                                          |
| -------------------------------------- | ---------------------------------------------- |
| Upload file / tạo Google Doc           | Ticket khác                                    |
| Duyệt cây folder đầy đủ (file browser) | Out of scope — chỉ chọn parent từ list đã sync |
| Quản lý Shared Drives (Team Drives)    | API & policy phức tạp                          |
| Webhook realtime                       | Ngoài scope MVP                                |
| Lưu permissions vào SQL                | v1 đọc live từ Google                          |
| Chia sẻ WH Folder (`FolderShares`)     | Khái niệm khác — đã có sẵn                     |


---

## 12. File tham chiếu trong repo

### Backend


| File                                                 | Vai trò                          |
| ---------------------------------------------------- | -------------------------------- |
| `Application/OAuth/Providers/Google/GoogleScopes.cs` | Scope OAuth Drive                |
| `Infrastructure/Services/GoogleDriveGateway.cs`      | Sync đọc                         |
| `Infrastructure/Services/DriveGateway.cs`            | Write-back file (mở rộng thêm)   |
| `Application/Services/DriveSyncService.cs`           | Orchestrate sync                 |
| `Application/Mapping/DriveItemMapper.cs`             | Map file → Item                  |
| `Application/Services/ItemWriteBackService.cs`       | Pattern write-back rename/trash  |
| `Api/Controllers/JiraController.cs`                  | Mẫu controller provider-specific |


### Frontend


| File                              | Vai trò                                |
| --------------------------------- | -------------------------------------- |
| `components/ItemDetail.tsx`       | Panel chi tiết — gắn Share + folder UI |
| `pages/Integrations.tsx`          | Card Drive — nút tạo folder            |
| `components/WorkspaceToolbar.tsx` | Toolbar — nút tạo folder               |
| `lib/itemsApi.ts`                 | Mẫu axios API layer                    |


### Docs liên quan


| File                | Khi nào cập nhật                    |
| ------------------- | ----------------------------------- |
| `docs/API.md`       | Sau khi có endpoint thật            |
| `docs/SPRINTS.md`   | SCRUM-79 status                     |
| `docs/CHANGELOG.md` | Khi merge — ghi quyết định thiết kế |


---

## Phụ lục — Google Drive API (tham khảo nhanh)

### Roles dùng trong v1


| Role        | Tiếng Việt UI   | Quyền                                  |
| ----------- | --------------- | -------------------------------------- |
| `reader`    | Người xem       | Xem, tải                               |
| `commenter` | Người bình luận | Xem + comment                          |
| `writer`    | Người chỉnh sửa | Sửa nội dung                           |
| `owner`     | Chủ sở hữu      | Full — chỉ hiển thị, không sửa qua app |


### Metadata Item sau khi sync (mẫu)

```json
{
  "mimeType": "application/vnd.google-apps.folder",
  "isFolder": true,
  "parents": ["1a2b3cDriveParentId"],
  "webViewLink": "https://drive.google.com/...",
  "iconLink": "https://..."
}
```

