# CHANGELOG — Thay đổi thiết kế

> Ghi lại các quyết định thiết kế lớn để cả nhóm và Claude Code nắm bối cảnh "tại sao".

## [2026-06-12] Code cleanup — thống nhất pattern sau review

Đợt dọn code do nhiều người viết song song tạo ra 2 phiên bản của cùng một thứ. Quy ước mới đã ghi vào `docs/CONVENTIONS.md`:

- **FE: hợp nhất 2 axios instance** — xoá `src/services/api.ts` (token key `'token'`), giữ `src/lib/api.ts` (`tokenStore`, key `wh_token`) làm instance duy nhất. Trước đó Login và Register dùng 2 instance với 2 token key khác nhau → đăng nhập/đăng ký xong các request sau mất JWT.
- **FE: auth chuyển sang TanStack Query** — `AuthContext` bỏ `useEffect + fetch` thủ công, Login dùng `useMutation` (đồng bộ với Register). `useAuth` tách ra `src/hooks/useAuth.ts` (fix lint react-refresh). Thêm route `/register`, sửa type `AuthResponse` khớp BE (`accessToken`).
- **BE: merge 2 base controller** — `BaseApiController` + `ApiControllerBase` (2 dev viết song song) gộp thành một `ApiControllerBase` duy nhất: route `api/[controller]`, `CurrentUserId`, throw `UnauthorizedException` (map 401 qua middleware thay vì `UnauthorizedAccessException` → 500).
- **BE: thống nhất validation** — Folders/Items bỏ block tự format lỗi, dùng `ValidateAndThrowAsync` → `ExceptionMiddleware` format 400 chuẩn (cùng đường với Auth).
- **BE: bỏ fallback JWT secret hardcode** trong `Program.cs` — thiếu `Jwt:Secret` thì fail lúc startup. ⚠️ Dev local phải có section `Jwt` trong `appsettings.Development.json` (copy từ `.example`).
- **BE: `/api/health` đổi `[Authorize]` → `[AllowAnonymous]`** — load balancer/monitoring gọi được không cần JWT.
- Dedupe nhỏ: `AuthService.SignInAsync` helper (3 chỗ lặp check IsActive + phát JWT), `ConnectionsService.GetDevCredential` helper, bỏ null-forgiving trong `FolderService.CreateAsync`. FE xoá dead code `AppLayout.tsx`, thêm `QueryClient` defaultOptions.

## [Phase 2 — đang làm] Mô hình B + Write-back Google + Google Sign-In

### Connection: chuyển từ mô hình A sang mô hình B
- **Trước:** 1 OAuthConnection (1 grant Google) → n ServiceConnection (Gmail/GCal/Drive) dùng chung 1 token, scope gộp.
- **Sau:** mỗi service = 1 row `Connections` độc lập, token riêng, scope riêng cố định.
- **Lý do:** mỗi service độc lập → disconnect sạch (xoá đúng row, không ảnh hưởng service khác), đổi account riêng từng service được, cấu trúc dễ hiểu, mở Jira cùng pattern.
- **Đánh đổi:** user phải authorize riêng mỗi service (nhiều redirect hơn).
- **DB:** gộp 2 bảng OAuthConnections + ServiceConnections → `Connections`. Items.ServiceConnectionId → ConnectionId.

### Bỏ cột Permission & Scopes ở Connections
- **Quyết định:** không thêm cột Permission/AccessLevel, không lưu Scopes ở Connections.
- **Lý do:** nghiệp vụ "bật service = full quyền", user không chọn read/write (scope do dev quyết). Mỗi connection = 1 service cố định → scope suy ra từ ServiceType trong code. Không có trạng thái "một phần quyền" để lưu.

### Đồng bộ 2 chiều (write-back) cho Google
- **Trước:** chỉ đọc (readonly), một chiều.
- **Sau:** thao tác trên app đẩy ngược lên Google. Scope đổi readonly → read-write (gmail.modify+send, calendar, drive.file).
- **Giới hạn:** email KHÔNG sửa nội dung (Gmail immutable) — chỉ label/read/star/trash + gửi mới. Event/File CRUD đầy đủ hơn.
- **Conflict:** thêm Items.ETag, so trước khi ghi, lệch → 409.
- **Cách làm phase này:** polling đọc (như cũ) + write-back synchronous (ghi ngay khi user thao tác). Webhook để phase sau.

### Google Sign-In (đăng nhập bằng Google)
- **Thêm:** đăng nhập app bằng Google, song song email/password.
- **Tách biệt với connect-để-sync:** login chỉ xin openid/email/profile, KHÔNG tạo Connection, chỉ tạo/tìm User + JWT. Disconnect service không làm logout.
- **Auto-link:** email Google trùng user đã đăng ký password → gắn GoogleSub, AuthProvider=Both.
- **DB:** Users.PasswordHash nullable, thêm GoogleSub + AuthProvider.

### Ticket liên quan
SCRUM-32 (migration multi-auth), 33 (Google Sign-In), 34 (migration mô hình B), 35 (OAuth flow B), 36 (scope read-write), 37 (write-back Email+Event+File), 38 (conflict resolution). SCRUM-30/31 (scheduled email) viết lại theo Connections, làm sau 38.

## [Phase 3 — backlog, chưa làm] Webhook & Jira
- Webhook Gmail/Calendar/Drive (watch + Pub/Sub) thay polling chiều đọc; bảng WebhookChannels + cron renew.
- Jira: OAuth Atlassian (cloudId), sync issue→Item(Ticket), write-back, Jira webhook.
- ImportantContacts khôi phục type JiraAccount.
- Ticket: SCRUM-39→46.

## [Phase 1 — đã xong] Nền tảng (tham khảo)
Auth email/password (JWT, RBAC), OAuth Google mô hình A readonly, sync 1 chiều Gmail, Folder/Item/Kanban/Tag/filter, Admin dashboard, FE skeleton. SCRUM-5→29.
