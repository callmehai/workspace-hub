# Postman API tests — Workspace Hub (SCRUM-27)

Bộ Postman collection kiểm thử toàn bộ endpoint chính của backend, lưu trong repo làm **evidence** cho deliverable "API testing".

## File

| File | Mô tả |
|------|-------|
| `Workspace-Hub.postman_collection.json` | Collection ~55 request, 11 nhóm (00–09 + 99), có test script tự assert status code + tự lưu token/id. |
| `Workspace-Hub.postman_environment.json` | Environment `Workspace Hub — Local (https)` (baseUrl **https** + token + biến id). |

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
2. Góc trên phải chọn environment **Workspace Hub — Local (https)**. Sửa `baseUrl` nếu backend chạy port khác (mặc định `https://localhost:7010`).
3. **Settings → General → SSL certificate verification = OFF** (cert dev self-signed sẽ làm request fail nếu bật).
4. Điền `userEmail` + `userPassword` của **tài khoản đã verify SĐT** (xem mục *Tài khoản test* bên dưới).
5. Mở **Collection Runner** (Run collection) → Run **Workspace Hub API (SCRUM-27)**.

## Chạy bằng CLI (Newman) — tuỳ chọn cho CI

```bash
npm install -g newman
newman run backend/postman/Workspace-Hub.postman_collection.json \
  -e backend/postman/Workspace-Hub.postman_environment.json \
  -k   # -k / --insecure: bỏ verify cert dev self-signed (HTTPS)
```

## Auth mới (SCRUM-62/63/64) — thay đổi so với bản cũ

Bản trước collection tự đăng ký user rồi đọc `accessToken` từ body response. Backend đã đổi:

- **Token nằm trong HttpOnly cookie `wh_access`**, KHÔNG còn trong body login/verify-otp. Request `Login` trong collection **đọc cookie** này (`pm.cookies.get('wh_access')`) → set biến `accessToken`, các request sau gửi qua header `Authorization: Bearer`. (Postman/Newman dùng Bearer nên **không dính CSRF** — middleware CSRF chỉ áp cho request dùng cookie.)
- **Register cần thêm `phone`** (E.164, vd `+84901234567`) và **gửi OTP**; login bị chặn **`403 PHONE_NOT_VERIFIED`** cho tới khi verify OTP.

### OTP đang bị bỏ qua khi test (chưa mua số Twilio)

OTP gửi qua SMS (Twilio). Tài khoản Twilio trial **chưa mua được số điện thoại** → BE tự fallback sang `LogSmsSender` (chỉ **ghi mã OTP ra console**, không gửi SMS thật). Vì mã không trả về qua API nên **không thể verify OTP theo kiểu blackbox**.

Do đó collection **chỉ test đường lỗi** của OTP và **bỏ qua happy-path verify**:

| Request | Kiểm tra |
|---------|----------|
| `Register (201)` | Trả `RegisterResult` (email + requiresPhoneVerification + cooldown), KHÔNG có token. |
| `Login unverified → PHONE_NOT_VERIFIED (403)` | Chứng minh cổng OTP chặn login khi chưa verify. |
| `Send OTP (200)` | Luôn 200 (chống enumeration). |
| `Verify OTP — mã sai (422)` | Mã sai/hết hạn → 422. Happy-path verify **skip**. |

> Nếu sau này **mua được số Twilio** và cấu hình `Sms:Twilio:AccountSid/AuthToken/FromNumber`, có thể thêm request verify-otp happy-path (đọc mã từ SMS thật) — hiện tại đã chủ động bỏ qua.

## Tài khoản test (BẮT BUỘC cho phần core)

Vì OTP không verify được blackbox, các nhóm 02–09 cần **1 tài khoản đã verify SĐT** để login lấy token. Tạo **một lần** bằng một trong hai cách:

1. **Qua console OTP (dev):** đăng ký (register) → đọc mã trong log BE dòng `[DEV SMS] To=... | Mã xác minh ... là: 123456` → gọi `POST /api/auth/verify-otp` với mã đó.
2. **Sửa DB trực tiếp (nhanh nhất):** đăng ký xong, set `UPDATE Users SET PhoneVerified = 1 WHERE Email = '<email>';` trong SQL Server.

Sau đó điền `userEmail` + `userPassword` của tài khoản này vào environment. Nếu để trống, các request core (nhóm 02–09) sẽ **skip** (Runner vẫn xanh) — chỉ chạy nhóm 00/01 (health + auth error-cases). Không có tài khoản verify thì **không có `accessToken`** → không test được phần cần đăng nhập.

> ⚠️ **Nếu Runner báo nhiều FAIL/404/405:** gần như chắc chắn do **chưa set `userEmail`/`userPassword`** (hoặc set nhưng tài khoản chưa verify SĐT). Khi đó Login không lấy được token → `folderId`/`noteItemId` rỗng → URL thành `/api/folders/`, `/api/items//status` (route không khớp → 404/405). Điền tài khoản đã verify là hết. (Collection đã guard để các case này **skip** thay vì fail, nhưng vẫn cần token để chạy phần core.)

## Biến môi trường

| Biến | Bắt buộc | Ý nghĩa |
|------|----------|---------|
| `baseUrl` | ✅ | URL backend. Mặc định `https://localhost:7010`. |
| `userEmail` / `userPassword` | ✅ cho core | Tài khoản **đã verify SĐT** (tạo sẵn 1 lần). Trống → nhóm 02–09 skip. |
| `userPhone` | tự set | SĐT E.164 dùng cho các case register throwaway (`+14155550100`, không cần thật). |
| `accessToken` | tự set | JWT — request `Login` đọc từ cookie `wh_access` rồi ghi vào. |
| `adminToken` | optional | JWT của tài khoản **Admin** để chạy 2 case admin-success. Trống → 2 request đó **skip**. |
| `gmailConnectionId` / `gcalConnectionId` | optional | Chỉ cần khi chạy happy-path provider (folder *Provider-dependent*). Trống → skip. |
| `throwawayEmail` | tự set | Email register random mỗi lần chạy (không đụng tài khoản test). |
| `userId` / `folderId` / `noteItemId` / `contactId` | tự set | Capture trong lúc chạy. |
| `missingId` | preset | GUID không tồn tại để test 404. |

> **Lấy `adminToken`:** đăng nhập bằng tài khoản có `Role=Admin` (đã verify SĐT), copy giá trị cookie `wh_access` trả về (hoặc dùng token từ Swagger) vào biến `adminToken`. Xem `docs/SETUP.md` để seed admin.

## Phủ status code (Acceptance Criteria)

| Code | Ví dụ trong collection |
|------|------------------------|
| **200** | Login, Send OTP, Get me, List folders/items/connections/scheduled, Patch status, Admin stats |
| **201** | Register (RegisterResult), Create folder/note/contact, Add item to folder |
| **204** | Logout, Delete item/folder/contact, Remove item from folder |
| **400** | Register short password + no phone, bad hex color, status enum sai, event end<start, scheduled thiếu recipient |
| **401** | Get me không token, Login sai mật khẩu, Google callback invalid |
| **403** | Login chưa verify SĐT (PHONE_NOT_VERIFIED), Admin users/stats/toggle-integration với token user thường |
| **404** | Get item/folder/connection/scheduled với id không tồn tại, Jira metadata connectionId không tồn tại, admin toggle-integration key sai |
| **409** | Register trùng email, Important contact trùng |
| **422** | Verify OTP mã sai, OAuth start serviceType không hợp lệ, create event/scheduled/ticket connection sai loại |

## Phủ nhóm endpoint

Auth (register/login/me/logout/google **+ send-otp/verify-otp**) · Connections (list/oauth-start/refresh/disconnect) · Folders (CRUD + item-folder) · Items (list/filter/get/note/event/status/delete) · Scheduled-emails (list/create/cancel) · Important-contacts (CRUD) · Admin (users/stats/toggle-integration + RBAC) · Jira (projects/issue-types/priorities/assignable-users/transitions/ticket CRUD error-cases) · Health.

## Kết quả chạy (evidence)

`test-run-report.txt` — output Newman (plain text, đã redact token/email) của lần chạy đầy đủ với 1 tài khoản đã verify: **125/125 assertions pass, 0 fail**. Các skip còn lại đều là optional có chủ đích (`adminToken`, `gmailConnectionId`, `gcalConnectionId`). Chạy lại: xem mục *Newman* ở trên.

## Ghi chú

- **Wire format camelCase**, enum dạng string (`status: "Doing"`, `type: "Note"`). Auth: `Authorization: Bearer {{accessToken}}`.
- Mọi response 4xx/5xx được assert có error envelope chuẩn `{ error, message, traceId }` (SCRUM-24).
- **Cookie jar (Postman/Newman):** Login trả `Set-Cookie: wh_access` + `wh_csrf` → Postman/Newman tự lưu vào cookie jar và gửi kèm mọi request sau (kể cả request đặt `noauth`). Auth thực tế của collection đi qua **Bearer header** (`accessToken`), nhưng 2 case bị cookie jar ảnh hưởng nên xử lý riêng:
  - **`Get me — invalid token (401)`**: gửi Bearer token rác thay vì "không token" — chắc chắn 401 dù jar còn `wh_access` (Bearer được ưu tiên hơn cookie, token rác không verify được). Ổn định trên cả GUI lẫn Newman.
  - **`Logout (204)`**: logout KHÔNG nằm trong CSRF-exempt list; jar gửi `wh_access` nên phải kèm header `X-CSRF-Token` khớp cookie `wh_csrf` (double-submit, y như frontend). Cách làm: **test-script của Login lưu `wh_csrf` vào biến `csrfToken`** (`pm.cookies.get('wh_csrf')` đọc được trong test-script, khác prerequest), rồi Logout gửi header `X-CSRF-Token: {{csrfToken}}` — interpolate đồng bộ, đáng tin trên cả Postman GUI lẫn Newman (không dùng `jar.get()` async vì GUI gửi request trước khi callback kịp gắn header).
- **Send OTP** dùng email random *chưa từng đăng ký* (nhánh anti-enumeration → luôn 200), KHÔNG dùng email vừa register vì còn cooldown 60s → 422.
- Happy-path cần Google/Jira thật (gửi mail, tạo event/ticket, oauth callback, sync) tách riêng vào folder **08 — Provider-dependent (manual)** và **skip** khi chưa có connection, để Runner không đỏ giả.
