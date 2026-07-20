# Postman API tests — Workspace Hub (SCRUM-27)

Bộ Postman collection kiểm thử **toàn bộ endpoint** của backend, lưu trong repo làm **evidence** cho deliverable "API testing".

> **Bản hiện tại được sinh lại từ source controller mới nhất** — phủ **123/123 endpoint** trên 22 controller (169 request, tính cả case lỗi 400/401/403/404/409/422/429/502).

## File

| File | Mô tả |
|------|-------|
| `Workspace-Hub.postman_collection.json` | Collection **169 request / 23 nhóm** (00–21 + 99), có test script tự assert status code + tự capture token/id. |
| `Workspace-Hub.postman_environment.json` | Environment `Workspace Hub - Local (https)` (baseUrl **https** + token + 46 biến id). |
| `generate_collection.py` | Script sinh lại 2 file trên từ spec. Khi thêm/đổi endpoint: sửa spec trong script rồi `python3 generate_collection.py`. |
| `test-run-report.txt` | Output Newman của lần chạy đầy đủ **bản collection cũ** (125/125 assertions pass) — giữ làm evidence lịch sử. Chạy lại với bản mới bằng lệnh Newman bên dưới. |

### Sinh lại collection sau khi đổi API

```bash
cd backend/postman
python3 generate_collection.py     # ghi đè collection + environment
```

Script in ra bảng số request theo nhóm để đối chiếu.

## Nhóm request

| # | Nhóm | # req | Ghi chú |
|---|------|-------|---------|
| 00 | Health | 1 | Không cần auth — smoke test |
| 01 | Auth | 15 | register/otp/login/me/logout/refresh/google |
| 02 | Users | 5 | profile, đổi mật khẩu, avatar (multipart) |
| 03 | Integrations | 1 | |
| 04 | Connections | 9 | OAuth start/callback, sync, gmail profile/sample |
| 05 | Folders | 11 | CRUD + item-folder (kể cả bulk) |
| 06 | Folder Sharing | 9 | invite/accept/decline/revoke/leave |
| 07 | Friends | 8 | request/accept/tier/invite-token |
| 08 | Items | 11 | list/filter/note/status/important/delete |
| 09 | Items · Calendar Event | 7 | create/patch/rsvp/detail/mail-guests |
| 10 | Calendar Invitations | 4 | |
| 11 | Emails | 12 | send/reply/forward/thread/draft/attachment |
| 12 | Email Contact Suggestions | 2 | OData |
| 13 | Scheduled Emails | 6 | create/list(OData)/cancel |
| 14 | Tags | 9 | CRUD + assign/unassign |
| 15 | Important Contacts | 6 | |
| 16 | Drive | 11 | folder/upload/content/thumbnail/permission/link-sharing |
| 17 | Items · Jira Ticket | 13 | create/patch/comment/attachment |
| 18 | Jira Metadata | 7 | projects/issue-types/priorities/users/transitions/site |
| 19 | Notifications | 5 | OData + mark read |
| 20 | Admin (RBAC) | 10 | users/stats/toggle + case 403 của user thường |
| 21 | Internal · Cron | 5 | `X-Cron-Secret` — process-scheduled / process-sync |
| 99 | Session | 2 | clear biến / whoami |

## Chạy backend (HTTPS + Docker)

Collection mặc định gọi qua **HTTPS** (`baseUrl = https://localhost:7010`). Chạy BE ở profile `https`:

```bash
cd backend

# 1) DB SQL Server (Docker) — nếu container chưa chạy (xem docs/SETUP.md)
docker start wh-sqlserver        # hoặc docker run ... lần đầu

# 2) Redis (Docker) — appsettings.Development trỏ ConnectionStrings:Redis=localhost:6379
#    nên profile Development sẽ kết nối Redis lúc khởi động (refresh token + OTP + OAuth state).
#    Nếu không muốn chạy Redis, để trống ConnectionStrings:Redis → fallback in-memory cache.
docker start wh-redis            # hoặc docker run -d -p 6379:6379 --name wh-redis redis

# 3) Backend ở HTTPS (mặc định https://localhost:7010)
dotnet run --project src/WorkspaceHub.Api --launch-profile https
```

> **Cert dev self-signed:** lần đầu chạy hãy tin cert dev — `dotnet dev-certs https --trust`. Nếu vẫn cảnh báo SSL trong Postman thì tắt verify (mục dưới).

## Chạy bằng Postman (GUI)

1. **Import** cả 2 file (Import → chọn 2 file).
2. Góc trên phải chọn environment **Workspace Hub - Local (https)**. Sửa `baseUrl` nếu backend chạy port khác.
3. **Settings → General → SSL certificate verification = OFF** (cert dev self-signed sẽ làm request fail nếu bật).
4. Điền `userEmail` + `userPassword` của **tài khoản đã verify email** (xem mục *Tài khoản test*).
5. Chạy **01 — Auth > Login** để nạp `accessToken`, rồi **04 — Connections > List connections** để tự nạp các `*ConnectionId`.
6. Hoặc mở **Collection Runner** chạy cả collection.

## Chạy bằng CLI (Newman) — tuỳ chọn cho CI

```bash
npm install -g newman
newman run backend/postman/Workspace-Hub.postman_collection.json \
  -e backend/postman/Workspace-Hub.postman_environment.json \
  -k   # -k / --insecure: bỏ verify cert dev self-signed (HTTPS)
```

## Auth (SCRUM-62/63/64)

- **Token nằm trong HttpOnly cookie `wh_access`**, KHÔNG còn trong body login/verify-otp. Request `Login` **đọc cookie** này (`pm.cookies.get('wh_access')`) → set biến `accessToken`; các request sau gửi qua header `Authorization: Bearer`. Khi có header `Authorization`, backend ưu tiên header và **bỏ qua CSRF check**.
- Login cũng lưu cookie `wh_csrf` vào biến `csrfToken` cho request nào vẫn đi bằng cookie (xem *Cookie jar* dưới).
- **Register KHÔNG còn `phone`**; OTP **gửi qua email**; login bị chặn **`403 EMAIL_NOT_VERIFIED`** cho tới khi verify OTP.
- **Rate limit theo IP** cho `/auth/register` + `/auth/send-otp` (5 req/phút) — chạy Runner nhiều lần liên tục có thể gặp `429`.

### OTP happy-path bị skip có chủ đích

OTP gửi qua email (Resend). Chưa cấu hình `Email:Resend:ApiKey`/`FromAddress` → BE fallback `LogEmailSender` (chỉ **ghi mã OTP ra console**). Mã không trả về qua API nên **không verify OTP kiểu blackbox được**.

Collection chỉ test đường lỗi + anti-enumeration; muốn chạy happy-path thì đọc mã trong log BE rồi set biến `otpCode` và chạy request `Verify OTP (200)`.

## Tài khoản test (BẮT BUỘC cho phần core)

Các nhóm 02–21 cần **1 tài khoản đã verify email**. Tạo **một lần**:

1. **Qua console OTP (dev):** register → đọc mã trong log BE dòng `[DEV EMAIL] ... Mã xác minh ... là: 123456` → `POST /api/auth/verify-otp`.
2. **Sửa DB trực tiếp (nhanh nhất):** `UPDATE Users SET EmailVerified = 1 WHERE Email = '<email>';`

Điền `userEmail` + `userPassword` vào environment. Để trống → request core sẽ **skip** (Runner vẫn xanh), chỉ chạy được nhóm 00/01.

> ⚠️ **Nếu Runner báo nhiều FAIL/404/405:** gần như chắc chắn do **chưa set `userEmail`/`userPassword`** (hoặc tài khoản chưa verify email) → Login không lấy được token → biến id rỗng → URL thành `/api/folders/` (route không khớp → 404/405).

## Biến môi trường

| Biến | Bắt buộc | Ý nghĩa |
|------|----------|---------|
| `baseUrl` | ✅ | URL backend. Mặc định `https://localhost:7010`. |
| `userEmail` / `userPassword` | ✅ cho core | Tài khoản **đã verify email**. Trống → nhóm core skip. |
| `accessToken` / `csrfToken` | tự set | Login đọc từ cookie `wh_access` / `wh_csrf`. |
| `adminToken` | optional | JWT tài khoản **Admin** cho nhóm 20. Trống → các request admin-success skip (case RBAC 403 vẫn chạy). |
| `cronSecret` | optional | Bằng config `Cron:Secret` — cho nhóm 21. Trống → 2 request success skip (case 401 vẫn chạy). |
| `gmailConnectionId` / `gcalConnectionId` / `driveConnectionId` / `jiraConnectionId` | tự set | Nạp từ request *List connections*. Trống → nhóm provider skip. |
| `friendEmail` | optional | Email người dùng khác để test kết bạn / chia sẻ Drive. |
| `emailItemId` / `driveFileItemId` / `jiraProjectKey` … | tự set / tay | Id thật để chạy happy-path provider. |
| `missingId` | preset | GUID không tồn tại để test 404. |

> **Lấy `adminToken`:** đăng nhập bằng tài khoản `Role=Admin` (đã verify email), copy giá trị cookie `wh_access` vào biến `adminToken`. Xem `docs/SETUP.md` để seed admin.

## Phủ status code (Acceptance Criteria)

| Code | Ví dụ trong collection |
|------|------------------------|
| **200** | Login, Get me, List folders/items/connections/tags/notifications, Patch status, Admin stats, Cron process-* |
| **201** | Register, Create folder/note/tag/contact/event/ticket, Add item to folder, Add drive permission |
| **204** | Logout, Delete item/folder/tag/contact, Remove share, Mark notification read, Thumbnail rỗng |
| **400** | Register payload sai, tag/folder tên rỗng, `limit=500`, `$top=500`, status enum sai, thiếu `connectionId`, bulk mảng rỗng |
| **401** | Get me token rác, Login sai mật khẩu, Cron sai/thiếu `X-Cron-Secret` |
| **403** | Admin users/stats/toggle-integration với token user thường; Login chưa verify (EMAIL_NOT_VERIFIED) |
| **404** | Item/folder/connection/scheduled-email/contact id không tồn tại, admin toggle-integration key sai |
| **409** | Register trùng email, Tag trùng tên, Important contact trùng, conflict ETag khi patch event/ticket, Drive link-sharing restrict |
| **422** | Verify OTP mã sai, đổi mật khẩu sai, cancel scheduled email đã gửi, kết bạn chính mình, invite share người chưa kết bạn |
| **429** | Register / send-otp vượt rate limit 5 req/phút |
| **502** | Mọi lời gọi Gmail/Calendar/Drive/Jira khi provider lỗi (`ProviderError`) |

## Ghi chú

- **Wire format camelCase**, enum dạng string (`status: "Doing"`, `type: "Note"`).
- Mọi response 4xx/5xx được assert có error envelope chuẩn `{ error, message, traceId }` (SCRUM-24) — trừ 405 (routing framework) và body rỗng.
- **Request phụ thuộc provider thật** (Gmail/Calendar/Drive/Jira) chấp nhận **502** vì lỗi provider được map thành `ProviderError` — Runner không đỏ giả.
- **Request thiếu biến phụ thuộc tự `pm.test.skip`** thay vì fail.
- **Cookie jar (Postman/Newman):** Login trả `Set-Cookie: wh_access` + `wh_csrf` → jar tự gửi kèm mọi request sau (kể cả request đặt `noauth`). 2 case xử lý riêng:
  - **`Me — token sai (401)`**: gửi Bearer token rác thay vì "không token" — chắc chắn 401 dù jar còn `wh_access`.
  - **`Logout (204)`**: logout KHÔNG nằm trong CSRF-exempt list; jar gửi `wh_access` nên phải kèm header `X-CSRF-Token` = `{{csrfToken}}` (double-submit, y như frontend).
- **Send OTP (200)** dùng email random *chưa từng đăng ký* (nhánh anti-enumeration → luôn 200); request `Send OTP lại` mới là case cooldown/rate-limit.
- **Endpoint multipart** (`drive/files`, `drive/folders/upload`, `items/{id}/attachments`, `users/me/avatar`) và **endpoint trả binary** để ở chế độ manual — cần chọn file / id thật.
