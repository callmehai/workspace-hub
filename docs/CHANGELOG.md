# CHANGELOG — Thay đổi thiết kế

> Ghi lại các quyết định thiết kế lớn để cả nhóm và Claude Code nắm bối cảnh "tại sao".

## [2026-06-20] Logging: built-in ILogger + request logging middleware (SCRUM-25, phần logging)

- **Quyết định:** dùng **built-in `ILogger`** (KHÔNG thêm Serilog) cho structured logging. Thêm `RequestLoggingMiddleware` log mỗi request 1 dòng completion (method/path/status/elapsed ms/userId).
- **Lý do:** đồ án quy mô nhỏ, built-in đủ dùng và không thêm dependency; có thể nâng cấp Serilog sau nếu cần sink file/JSON. Một dòng completion (kiểu `UseSerilogRequestLogging`) đủ cho cả AC "log request/response" lẫn AC "đo response time", ít noise hơn log riêng request-in + response-out.
- **Thứ tự middleware (quan trọng):** `RequestLoggingMiddleware` đặt **ngoài cùng**, trước `ExceptionMiddleware`. Vì ExceptionMiddleware nuốt exception và tự set status code, đặt logging bên ngoài mới đọc đúng status 5xx và đo trọn thời gian. 5xx chỉ log Error 1 lần ở ExceptionMiddleware; dòng completion log Warning, tránh trùng.
- **EF query logging:** `EnableSensitiveDataLogging()` + `EnableDetailedErrors()` + `Database.Command=Information` **chỉ bật ở Development** (qua cờ `isDevelopment` truyền vào `AddInfrastructure`) — production không bật để khỏi lộ tham số SQL trong log.
- **Tối ưu query (cùng SCRUM-25, làm 2026-06-20):** folder list `GetUserFoldersAsync` gỡ `Include(FolderShares)` — include chết vì folder do user sở hữu luôn map permission "Owner", không đọc FolderShares; đây cũng là 1 trong 2 collection gây cartesian explosion (folders × itemFolders × folderShares). Thêm `AsSplitQuery()` cho collection `ItemFolders` còn lại (cả `GetSharedFoldersAsync`). Items list `GetPagedAsync` xác nhận đã tối ưu sẵn — `ToQueryString()` cho ra đúng 1 SELECT có `OFFSET/FETCH` (filter/sort/paging ở DB, `AsNoTracking`, không navigation → không N+1).
- **Descope:** AC "đo response time với data mẫu (seed)" đã bỏ khỏi SCRUM-25 — `OFFSET/FETCH` + composite index `IX_Items_User_Status_OccurredAt` đã đảm bảo độ phức tạp tốt; `elapsed ms` trong log (`RequestLoggingMiddleware`) đủ để đo bất cứ lúc nào khi chạy thật, không cần seed-script riêng. SCRUM-25 còn lại 2 AC (logging + query no-N+1) → **Done**.

## [2026-06-12] Integrations: bỏ credentials trong DB, dùng config/env (chốt scope admin)

- **Quyết định:** OAuth client credentials (ClientId/ClientSecret của app với Google) đọc từ **config/env duy nhất** cho cả dev lẫn prod. Drop 2 cột `Integrations.ClientIdEncrypted/ClientSecretEncrypted` + xoá endpoint `PUT /api/connections/{key}/credentials` (một phần SCRUM-13). Ticket: SCRUM-47 (xem SPRINTS.md).
- **Lý do:** scope "admin quản lý tích hợp" được chốt lại = **bật/tắt integration** (`IsEnabled` — user không dùng được tính năng của integration bị tắt), KHÔNG bao gồm nhập/đổi credentials lúc runtime. Đường DB-credentials vì vậy mất lý do tồn tại, và đang dở dang (mới có nửa ghi, nửa đọc chưa viết). Env/config là cách chuẩn (12-factor) cho app có số provider cố định.
- **Không đổi:** Data Protection vẫn mã hoá access/refresh token của user trong `Connections` (chỗ này mới quan trọng). JWT secret, connection string vẫn ở env/config như cũ.

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

### Đổi section config OAuth credentials từ `Dev:` → `OAuth:`
- **Trước:** credentials đọc từ `Dev:{provider}:ClientId/ClientSecret` (tên ngụ ý "chỉ dùng dev", prod có comment placeholder decrypt DB).
- **Sau:** credentials đọc từ `OAuth:{provider}:ClientId/ClientSecret` cho cả dev lẫn prod. Prod set qua env var `OAuth__google__ClientId` / `OAuth__google__ClientSecret` (ASP.NET `__` thay `:`).
- **Lý do:** section `Dev:` gây nhầm lẫn — đây là đường cấu hình chính thức, không phải "đường tắt". Bỏ hẳn comment "PRODUCTION: decrypt từ DB" và bỏ cột `ClientIdEncrypted`/`ClientSecretEncrypted` khỏi bảng `Integrations` (SCRUM-13 encrypt DB không còn trong plan — credentials quản lý qua config/env, không lưu DB).
- **Tác động:** `appsettings.Development.json` đổi key; `appsettings.json` thêm template `OAuth` rỗng; `GoogleTokenVerifier`, `AuthService`, `ConnectionsService` đổi `_config["Dev:..."]` → `_config["OAuth:..."]`. Fallback `Google:ClientId` cho Sign-In vẫn còn.

### Ticket liên quan
SCRUM-32 (migration multi-auth), 33 (Google Sign-In), 34 (migration mô hình B), 35 (OAuth flow B), 36 (scope read-write), 37 (write-back Email+Event+File), 38 (conflict resolution). SCRUM-30/31 (scheduled email) viết lại theo Connections, làm sau 38.

## [Phase 3 — backlog, chưa làm] Webhook & Jira
- Webhook Gmail/Calendar/Drive (watch + Pub/Sub) thay polling chiều đọc; bảng WebhookChannels + cron renew.
- Jira: OAuth Atlassian (cloudId), sync issue→Item(Ticket), write-back, Jira webhook.
- ImportantContacts khôi phục type JiraAccount.
- Ticket: SCRUM-39→46.

## [Phase 1 — đã xong] Nền tảng (tham khảo)
Auth email/password (JWT, RBAC), OAuth Google mô hình A readonly, sync 1 chiều Gmail, Folder/Item/Kanban/Tag/filter, Admin dashboard, FE skeleton. SCRUM-5→29.
