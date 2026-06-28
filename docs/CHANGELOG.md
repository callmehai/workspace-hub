# CHANGELOG — Thay đổi thiết kế

> Ghi lại các quyết định thiết kế lớn để cả nhóm và Claude Code nắm bối cảnh "tại sao".

## [Target — chưa code, chưa có ticket] OData query cho GET collection

- **Quyết định:** bật **OData query options** (`Microsoft.AspNetCore.OData` v8, `[EnableQuery]`) cho các endpoint **GET đọc collection trên `IQueryable` EF**: `GET /api/items`, `/api/admin/users`, `/api/scheduled-emails`, `/api/folders`, `/api/tags`, `/api/integrations`. Cho phép `$filter/$orderby/$select/$top/$skip/$count`; **không** `$expand`.
- **Lý do:** giảm số query param thủ công + bộ filter rời rạc; client tự chọn field/sort/paging, đẩy xuống SQL. Hợp tiêu chí PRN232 (REST + truy vấn linh hoạt).
- **Phạm vi (cố ý hẹp):** **KHÔNG** bật cho endpoint trả **live provider data** (item detail, Jira metadata helpers), **mask/decrypt token** (connections), single-resource, aggregate (admin/stats), và mọi write. Lý do: OData chỉ an toàn + có nghĩa trên `IQueryable` thuần dịch được sang SQL.
- **Bảo mật (chốt):** luôn scope theo `CurrentUserId`/role **server-side TRƯỚC** rồi mới `[EnableQuery]`. Giới hạn `MaxTop=100`, `PageSize=20`. Action trả `IQueryable<TDto>` (`AsNoTracking` + projection DTO, KHÔNG Entity).
- **Ảnh hưởng shape:** endpoint nào bật OData thì `$count` thay `total`, `$top/$skip` thay `page/limit` của envelope cũ — FE cập nhật khi wire. Chi tiết: `docs/API.md` (mục "OData query") + `docs/CONVENTIONS.md`.
- **Status:** mới là **target tài liệu, chưa code, chưa có ticket Jira** — cần tạo ticket trước khi làm.

## [Phase Jira — kế hoạch, chưa code] Tích hợp Jira / Atlassian (SCRUM-54→60)

- **Bối cảnh:** board Jira đã tạo **7 ticket SCRUM-54→60** mở lại **phase Jira/Atlassian integration** (CRUD đầy đủ issue). Đây là **kế hoạch** — tất cả To Do, ở backlog, **chưa viết code**. Current phase vẫn dừng ở SCRUM-38 (write-back Google + conflict). Bắt đầu phase Jira sau khi Sprint 3 ổn định.
- **Cụm ticket:** 54 = Atlassian Integration + OAuth 3LO (cloudId), mô hình B (Khánh) · 55 = client + đọc/sync issue → Item(Type=Ticket) (Vũ) · 56 = tạo issue `POST /api/items/ticket` (Vũ) · 57 = write-back update `PATCH /api/items/{id}` Type=Ticket qua `IWriteBackGuard` (Vũ + Lộc guard) · 58 = xoá issue `DELETE /api/items/{id}` Type=Ticket (Vũ) · 59 = metadata helpers projects/issue-types/transitions/assignable-users/priorities (Huy) · 60 = ImportantContacts JiraAccount + Notification type, optional (Huy).
- **Tái dùng mô hình B nguyên vẹn:** Atlassian = 1 Integration (key=`atlassian`), mỗi Jira account = 1 Connection (ServiceType=Jira, `ProviderAccountId` = **cloudId** từ `/oauth/token/accessible-resources`). Credentials đọc config `OAuth:atlassian:...` (như SCRUM-39, không lưu DB). CursorType thêm `JqlUpdated` (poll issue có `fields.updated` sau mốc cursor).
- **Lưu ý kỹ thuật (a) — conflict không có ETag:** Jira REST **không trả HTTP ETag**. Dùng **`fields.updated` làm version-token** lưu trong `Items.ETag`, so sánh trước khi ghi; lệch → 409. Đi qua **cùng `IWriteBackGuard` của SCRUM-38** (không thêm cơ chế conflict riêng cho Jira) — guard chỉ cần coi ETag là "version-token mờ", không giả định đó là ETag HTTP.
- **Lưu ý kỹ thuật (b) — ADF:** description của Jira là **ADF (Atlassian Document Format)** — JSON cấu trúc, **không phải markdown thuần**. Cần **convert 2 chiều** (đọc: ADF → markdown để hiển thị/lưu Note-style; ghi: markdown → ADF trước khi PATCH). Đặt ở service layer.
- **Khác Gmail (quan trọng):** nội dung Jira (**summary + description**) **sửa được** qua write-back — KHÁC Email immutable (Gmail chỉ cho label/read/star/trash). Đổi status đi qua **transition** (không set field status trực tiếp). Thêm comment là thao tác riêng, không phải sửa field.
- **Vẫn ngoài scope:** webhook Jira (push realtime) — chưa có ticket; đọc Jira vẫn on-demand như Google.

## [2026-06-21] Đồng bộ lại tài liệu theo Jira (đánh số ticket thay đổi)

- **Bối cảnh:** Jira được tổ chức lại; đánh số sau SCRUM-38 đổi hẳn so với các bản tài liệu trước. Cập nhật SPRINTS.md + CLAUDE.md + các file này cho khớp.
- **Đổi số chính:**
  - "Ticket tạm" cũ → số Jira thật: bỏ DB credentials `47*` → **SCRUM-39**; admin toggle integration `48*` → **SCRUM-40**; admin users+stats `49*` → **SCRUM-23**.
  - **Backlog Webhook & Jira (cũ SCRUM-39→46) bị bỏ khỏi Jira.** Số 39–46 nay là việc khác (cleanup + FE). Webhook/Jira/Atlassian không còn ticket — chỉ là định hướng tương lai ngoài scope.
  - Khối FE + deploy + nghiệm thu là **SCRUM-41→53** (mới).
- **Status đáng chú ý:** SCRUM-15/16/17 (sync Gmail/Calendar/Drive), 22 (auth pages), 23/39/40 = **Done**; SCRUM-37 (write-back) = **In Review**; 26–31, 38, 41–53 = **To Do**. Sprint hiện hành = **Sprint 3**.
- **Mô hình sync (chốt rõ theo SCRUM-16):** đọc dữ liệu Google chạy **theo nhu cầu (on-demand/lazy), KHÔNG pull định kỳ, KHÔNG webhook** trong MVP — đã bỏ hẳn timer/cron đọc. Cron chỉ còn cho **gửi scheduled email** (SCRUM-31). Các chỗ tài liệu cũ ghi "polling/cron đọc" đã sửa.

## [2026-06-20] Logging: built-in ILogger + request logging middleware (SCRUM-25)

- **Quyết định:** dùng **built-in `ILogger`** (KHÔNG thêm Serilog) cho structured logging. Thêm `RequestLoggingMiddleware` log mỗi request 1 dòng completion (method/path/status/elapsed ms/userId).
- **Lý do:** đồ án quy mô nhỏ, built-in đủ dùng và không thêm dependency; có thể nâng cấp Serilog sau nếu cần sink file/JSON. Một dòng completion (kiểu `UseSerilogRequestLogging`) đủ cho cả AC "log request/response" lẫn AC "đo response time", ít noise hơn log riêng request-in + response-out.
- **Thứ tự middleware (quan trọng):** `RequestLoggingMiddleware` đặt **ngoài cùng**, trước `ExceptionMiddleware`. Vì ExceptionMiddleware nuốt exception và tự set status code, đặt logging bên ngoài mới đọc đúng status 5xx và đo trọn thời gian. 5xx chỉ log Error 1 lần ở ExceptionMiddleware; dòng completion log Warning, tránh trùng.
- **EF query logging:** `EnableSensitiveDataLogging()` + `EnableDetailedErrors()` + `Database.Command=Information` **chỉ bật ở Development** (qua cờ `isDevelopment` truyền vào `AddInfrastructure`) — production không bật để khỏi lộ tham số SQL trong log.
- **Tối ưu query:** folder list `GetUserFoldersAsync` gỡ `Include(FolderShares)` (include chết: folder do user sở hữu luôn map "Owner"; cũng là 1 trong 2 collection gây cartesian explosion) + `AsSplitQuery()` cho `ItemFolders` (cả `GetSharedFoldersAsync`). Items list `GetPagedAsync` đã tối ưu sẵn — `ToQueryString()` cho ra đúng 1 SELECT có `OFFSET/FETCH`, không N+1.
- **Descope:** AC "đo response time với data mẫu (seed)" đã bỏ — `OFFSET/FETCH` + composite index `IX_Items_User_Status_OccurredAt` đủ; `elapsed ms` trong log đủ để đo khi cần.

## [2026-06-12] Integrations: bỏ credentials trong DB, dùng config/env (SCRUM-39)

- **Quyết định:** OAuth client credentials (ClientId/ClientSecret của app với Google) đọc từ **config/env duy nhất** cho cả dev lẫn prod. Drop 2 cột `Integrations.ClientIdEncrypted/ClientSecretEncrypted` + xoá endpoint `PUT /api/connections/{key}/credentials`. Ticket: **SCRUM-39** (migration `RemoveClientCredentialsFromIntegration`).
- **Lý do:** scope "admin quản lý tích hợp" = **bật/tắt integration** (`IsEnabled`), KHÔNG bao gồm nhập/đổi credentials lúc runtime. Đường DB-credentials vì vậy mất lý do tồn tại. Env/config là cách chuẩn (12-factor) cho app có số provider cố định.
- **Không đổi:** Data Protection vẫn mã hoá access/refresh token của user trong `Connections`. JWT secret, connection string vẫn ở env/config.

## [2026-06-12] Code cleanup — thống nhất pattern sau review

Đợt dọn code do nhiều người viết song song tạo ra 2 phiên bản của cùng một thứ. Quy ước mới đã ghi vào `docs/CONVENTIONS.md`:

- **FE: hợp nhất 2 axios instance** — xoá `src/services/api.ts` (token key `'token'`), giữ `src/lib/api.ts` (`tokenStore`, key `wh_token`) làm instance duy nhất.
- **FE: auth chuyển sang TanStack Query** — `AuthContext` bỏ `useEffect + fetch` thủ công, Login dùng `useMutation`. `useAuth` tách ra `src/hooks/useAuth.ts`. Thêm route `/register`, sửa type `AuthResponse` khớp BE (`accessToken`).
- **BE: merge 2 base controller** → một `ApiControllerBase` duy nhất: route `api/[controller]`, `CurrentUserId`, throw `UnauthorizedException` (map 401 qua middleware).
- **BE: thống nhất validation** — Folders/Items dùng `ValidateAndThrowAsync` → `ExceptionMiddleware` format 400 chuẩn.
- **BE: bỏ fallback JWT secret hardcode** — thiếu `Jwt:Secret` thì fail lúc startup.
- **BE: `/api/health` đổi `[Authorize]` → `[AllowAnonymous]`.**
- Dedupe nhỏ: `AuthService.SignInAsync` helper, `ConnectionsService.GetDevCredential` helper, FE xoá dead code `AppLayout.tsx`, thêm `QueryClient` defaultOptions.

## [Sprint 2 — đã xong] Mô hình B + Write-back Google + Google Sign-In

### Connection: chuyển từ mô hình A sang mô hình B
- **Trước:** 1 OAuthConnection (1 grant Google) → n ServiceConnection (Gmail/GCal/Drive) dùng chung 1 token, scope gộp.
- **Sau:** mỗi service = 1 row `Connections` độc lập, token riêng, scope riêng cố định.
- **Lý do:** mỗi service độc lập → disconnect sạch (xoá đúng row), đổi account riêng từng service được, cấu trúc dễ hiểu.
- **Đánh đổi:** user phải authorize riêng mỗi service (nhiều redirect hơn).
- **DB:** gộp 2 bảng OAuthConnections + ServiceConnections → `Connections`. Items.ServiceConnectionId → ConnectionId.

### Bỏ cột Permission & Scopes ở Connections
- **Quyết định:** không thêm cột Permission/AccessLevel, không lưu Scopes ở Connections.
- **Lý do:** nghiệp vụ "bật service = full quyền". Mỗi connection = 1 service cố định → scope suy ra từ ServiceType trong code. Không có trạng thái "một phần quyền" để lưu.

### Đồng bộ 2 chiều (write-back) cho Google
- **Trước:** chỉ đọc, một chiều.
- **Sau:** thao tác trên app đẩy ngược lên Google. Scope đổi readonly → read-write (gmail.modify+send, calendar, drive.file).
- **Giới hạn:** email KHÔNG sửa nội dung (Gmail immutable) — chỉ label/read/star/trash + gửi mới. Event/File CRUD đầy đủ hơn.
- **Conflict:** thêm Items.ETag, so trước khi ghi, lệch → 409.
- **Cách đọc:** **on-demand/lazy** (không polling định kỳ, không webhook); ghi = synchronous khi user thao tác. Webhook ngoài scope.

### Google Sign-In (đăng nhập bằng Google)
- **Thêm:** đăng nhập app bằng Google, song song email/password.
- **Tách biệt với connect-để-sync:** login chỉ xin openid/email/profile, KHÔNG tạo Connection, chỉ tạo/tìm User + JWT. Disconnect service không làm logout.
- **Auto-link:** email Google trùng user đã đăng ký password → gắn GoogleSub, AuthProvider=Both.
- **DB:** Users.PasswordHash nullable, thêm GoogleSub + AuthProvider.

### Đổi section config OAuth credentials từ `Dev:` → `OAuth:`
- **Sau:** credentials đọc từ `OAuth:{provider}:ClientId/ClientSecret` cho cả dev lẫn prod. Prod set qua env var `OAuth__google__ClientId` / `OAuth__google__ClientSecret`.
- **Lý do:** section `Dev:` gây nhầm lẫn — đây là đường cấu hình chính thức. Bỏ comment "PRODUCTION: decrypt từ DB" và bỏ cột `ClientIdEncrypted`/`ClientSecretEncrypted`.

### Ticket liên quan
SCRUM-32 (migration multi-auth), 33 (Google Sign-In), 34 (migration mô hình B), 35 (OAuth flow B), 36 (scope read-write), 37 (write-back Email+Event+File — In Review), 38 (conflict resolution — To Do). Scheduled email viết lại theo Connections = SCRUM-30/31 (Sprint 3).

## [Sau mô hình B] Hardening + FE + deploy (Sprint 3–4)

Thay cho backlog "Webhook & Jira" cũ (đã bỏ khỏi Jira):
- **Sprint 3:** hoàn thiện write-back (37) + conflict (38), scheduled email (30/31), refactor service (26), API testing (27), README backend (28), unit test (29), bắt đầu FE (41–43).
- **Sprint 4:** FE đầy đủ (44–50), deploy prod (51), finalize Swagger + E2E (52), defense (53).

## [Cập nhật scope] Webhook vs Jira
- **Jira/Atlassian:** ĐÃ chuyển từ "ngoài scope" sang **phase có kế hoạch = SCRUM-54→60** (xem entry "[Phase Jira]" đầu file) — chưa code. Đừng tái dùng số SCRUM-39→46 cho Jira (số đó là việc khác).
- **Webhook Gmail/Calendar/Drive/Jira (watch + Pub/Sub)** thay sync on-demand: **vẫn ngoài scope, chưa có ticket** — định hướng tương lai, đừng code.

## [Sprint 1 — đã xong] Nền tảng (tham khảo)
Auth email/password (JWT, RBAC), OAuth Google, sync Gmail/Calendar/Drive (on-demand), Folder/Item/Kanban/Tag/filter, Admin (users+stats), FE skeleton. SCRUM-5→23.
