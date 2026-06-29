# Postman API tests — Workspace Hub (SCRUM-27)

Bộ Postman collection kiểm thử toàn bộ endpoint chính của backend, lưu trong repo làm **evidence** cho deliverable "API testing".

## File

| File | Mô tả |
|------|-------|
| `Workspace-Hub.postman_collection.json` | Collection 48 request, 10 nhóm, có test script tự assert status code + tự lưu token/id. |
| `Workspace-Hub.postman_environment.json` | Environment `Workspace Hub — Local` (baseUrl + token + biến id). |

## Chạy bằng Postman (GUI)

1. **Import** cả 2 file (Import → chọn 2 file).
2. Góc trên phải chọn environment **Workspace Hub — Local**. Sửa `baseUrl` nếu backend chạy port khác (mặc định `http://localhost:5118`).
3. Chạy backend:
   ```bash
   cd backend
   dotnet run --project src/WorkspaceHub.Api
   ```
4. Mở **Collection Runner** (Run collection) → Run **Workspace Hub API (SCRUM-27)**.

Các request đã sắp đúng thứ tự: tự đăng ký user mới (email random theo `Date.now()` nên chạy lại nhiều lần không bị 409), tự đăng nhập lưu `accessToken`, tự tạo folder/note/contact rồi dọn ở cuối. **Không cần điền tay gì** cho phần lõi.

## Chạy bằng CLI (Newman) — tuỳ chọn cho CI

```bash
npm install -g newman
newman run backend/postman/Workspace-Hub.postman_collection.json \
  -e backend/postman/Workspace-Hub.postman_environment.json
```

## Biến môi trường

| Biến | Bắt buộc | Ý nghĩa |
|------|----------|---------|
| `baseUrl` | ✅ | URL backend. Mặc định `http://localhost:5118`. |
| `accessToken` | tự set | JWT user thường — request `Login` ghi vào. |
| `userEmail` / `userPassword` | tự set | User test (pre-request seed nếu trống). |
| `adminToken` | optional | JWT của tài khoản **Admin** để chạy 2 case admin-success. Trống → 2 request đó **skip**, phần còn lại vẫn xanh. |
| `gmailConnectionId` / `gcalConnectionId` | optional | Chỉ cần khi chạy happy-path provider (folder *Provider-dependent*). Trống → skip. |
| `folderId` / `noteItemId` / `contactId` | tự set | Capture trong lúc chạy. |
| `missingId` | preset | GUID không tồn tại để test 404. |

> **Lấy `adminToken`:** đăng nhập bằng tài khoản có `Role=Admin` (seed trong DB hoặc sửa cột `Users.Role`), copy `accessToken` trả về vào biến `adminToken`. Xem `docs/SETUP.md` để seed admin.

## Phủ status code (Acceptance Criteria)

| Code | Ví dụ trong collection |
|------|------------------------|
| **200** | Login, Get me, List folders/items/connections/scheduled, Patch status, Admin stats |
| **201** | Register, Create folder/note/contact, Add item to folder |
| **204** | Logout, Delete item/folder/contact, Remove item from folder |
| **400** | Register short password, bad hex color, status enum sai, event end<start, scheduled thiếu recipient |
| **401** | Get me không token, Login sai mật khẩu, Google callback invalid |
| **403** | Admin users/stats với token user thường (sai role) |
| **404** | Get item/folder/connection/scheduled với id không tồn tại |
| **409** | Register trùng email, Important contact trùng |
| **422** | OAuth start serviceType không hợp lệ, create event/scheduled connection sai loại |

## Phủ nhóm endpoint

Auth (register/login/me/logout/google) · Connections (list/oauth-start/refresh/disconnect) · Folders (CRUD + item-folder) · Items (list/filter/get/note/event/status/delete) · Scheduled-emails (list/create/cancel) · Important-contacts (CRUD) · Admin (users/stats + RBAC) · Health.

## Ghi chú

- **Wire format camelCase**, enum dạng string (`status: "Doing"`, `type: "Note"`). Auth: `Authorization: Bearer {{accessToken}}`.
- Mọi response 4xx/5xx được assert có error envelope chuẩn `{ error, message, traceId }` (SCRUM-24).
- Happy-path cần Google/Jira thật (gửi mail, tạo event/ticket, oauth callback, sync) tách riêng vào folder **08 — Provider-dependent (manual)** và **skip** khi chưa có connection, để Runner không đỏ giả.
