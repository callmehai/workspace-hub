# CHANGELOG — Thay đổi thiết kế

> Ghi lại các quyết định thiết kế lớn để cả nhóm và Claude Code nắm bối cảnh "tại sao".

## [2026-07-07] Cron sync connections + FE auto-refresh (SCRUM-72)

> **Mở rộng SCRUM-16:** bổ sung sync **định kỳ** ngoài on-demand; webhook/push realtime vẫn ngoài scope.

- **BE cron batch sync:** `ProcessConnectionsSyncService` quét mọi Connection Active + Integration enabled → sync qua `IConnectionSyncDispatcher` (đủ Gmail/GCal/Drive/Jira). Mỗi connection lỗi không chặn batch.
- **HTTP cron (prod):** `POST /api/internal/process-sync` + `X-Cron-Secret` (dùng chung `Cron:Secret` với SCRUM-31). Khuyến nghị 5 phút/lần.
- **BackgroundService (dev):** `Cron:SyncAutoRun` + `Cron:SyncIntervalSeconds` — tách key riêng với cron email (`AutoRun` / `IntervalSeconds`). Prod mặc định `SyncAutoRun=false`.
- **On-demand nhất quán:** `ConnectionHealthChecker` chuyển sang dispatcher (không chỉ Gmail) — align với cron.
- **FE polling (TanStack Query):** Inbox/Kanban `items` 45s; Integrations `connections` 60s; `refetchIntervalInBackground` (poll cả tab nền); invalidate cross-tab/lọc khi `total` đổi (Inbox). Manual sync trên Integrations invalidate cả `items`.
- **Không làm:** WebSocket/SSE, Gmail push notification — UI cập nhật qua poll sau khi cron ghi DB.

## [2026-07-07] UI polish + Theme Sáng/Tối + Song ngữ VI/EN + Trang Profile

> Review UI phát hiện **lệch tông màu**: Login/Header dùng `brand`=blue-600 (#2563eb) trong khi Sidebar/Inbox/toolbar dùng indigo-600 (#4f46e5) — logo "W" + nút primary hai màu xanh khác nhau; Header nền `gray-50` lệch app nền `slate-50`; avatar Header (gradient) khác avatar Sidebar. Cùng lúc bổ sung theme + i18n + profile (chuẩn bị avatar/R2).

- **Thống nhất palette:** `brand` (tailwind.config) đổi blue → **indigo** (SSOT màu thương hiệu) + thêm shade 200/300/400/800/900. Mọi bề mặt `brand-*` (Login/Header/nút/focus ring) nay đồng tông với indigo của sidebar/inbox. Header đổi `gray-50`→`bg-white` (khớp chrome sidebar), avatar Header đồng bộ `brand-50/brand-600`.
- **Theme Sáng/Tối:** `darkMode:'class'`; `ThemeProvider` toggle class `.dark` trên `<html>` + persist `localStorage['wh-theme']`; inline script `index.html` set class **trước paint** (chống FOUC). **Đổi theme = thao tác DOM thuần → KHÔNG remount** cây React (giữ state/scroll/query cache). Style dark phủ **toàn app**: shell + auth (Login/Register/VerifyOtp/callbacks) + Profile + Inbox/Kanban/Integrations + AdminDashboard + ScheduledEmails/SendEmail + ItemDetail drawer + mọi modal (CreateNote/CreateEvent/Folder) + RichTextEditor (+css `.dark .wh-rte`)/BulkActionBar/DateTimePicker/EmailChipsInput/Select/PageSizeSelect/WorkspaceToolbar/Toaster. Badge tint (`bg-*-50/100`) → `dark:bg-*-500/15 dark:text-*-300` để hết loá trên nền tối.
- **Song ngữ VI/EN:** i18n **tự viết, không thêm lib** (`src/i18n/` — `translations.ts` từ điển phẳng VI/EN + `I18nProvider` + `useI18n().t()`, nội suy `{var}`), persist `localStorage['wh-lang']`. Đổi ngôn ngữ = đổi context value → **re-render, KHÔNG remount** (không mất state form, không refetch query). VI mặc định. Dịch đủ shell/auth/profile/toolbar + nhãn chính core; page phụ mở rộng dần bằng cách thêm key.
- **Trang Profile:** route `/profile`, vào từ avatar Header + block user Sidebar; hiển thị tài khoản + tuỳ chọn theme/ngôn ngữ; vùng avatar đặt sẵn nút "Đổi ảnh đại diện" (disabled) — **chừa chỗ cho task avatar upload + Cloudflare R2** (kế tiếp).
- **Tickets (chưa có trên Jira):** draft ở `docs/tickets-ui-i18n-theme-profile.md` (SCRUM-73 i18n, 74 Profile, 75 Avatar/R2, 76 Theme + CSV import). Dark mode vốn nằm trong SCRUM-50 (gộp responsive+dashboard) — tách 76 hoặc đánh dấu tiến độ ở 50.
- **Files:** `tailwind.config.js`, `index.html`, `src/index.css`, `src/App.tsx`, `src/context/{theme-context.ts,ThemeProvider.tsx}`, `src/i18n/*`, `src/hooks/{useTheme,useI18n}.ts`, `src/components/ThemeLangControls.tsx`, `src/pages/ProfilePage.tsx`, `src/router.tsx`, shell + Login/Register/Inbox/KanbanBoard/Integrations + Select/PageSizeSelect/WorkspaceToolbar/GoogleSignInButton.

## [2026-07-07] UX overhaul: Folder = context (không phải filter) + prototype v2

> Cơ chế folder/Inbox/Kanban cũ bị lai: sidebar coi folder như trang, Inbox coi folder như filter chip, nav Inbox/Kanban làm rớt `?folder=` khi click, tiêu đề trang luôn "Inbox" (đụng tên status `Inbox`). Chốt lại mô hình **Folder = context, view = cách hiển thị context**.

- **Mô hình:** một context (Tất cả mục / 1 thư mục) có 2 view — Danh sách (`/`) và Bảng (`/kanban`), context qua `?folder={id}`. **Bất biến:** đổi view giữ context, đổi context giữ view; xoá folder đang xem → về Tất cả mục (giữ view).
- **Sidebar:** bỏ nav "Inbox"/"Bảng Kanban" (view toggle nằm trong page); nav chính có **"Tất cả mục"**; section THƯ MỤC chỉ chứa folder thật + itemCount badge. Active duy nhất 1 mục tại mọi thời điểm.
- **Header trang = context:** chấm màu + tên thư mục (hoặc "Tất cả mục") + subtitle đếm; folder **không** còn trong dải chip "Đang lọc"; chip folder trên item ẩn folder đang đứng trong.
- **Từ ngữ:** không dùng "Inbox" trong UI; status label `Done`→"Hoàn thành"; chip "Tất cả" lặp → "Mọi trạng thái"/"Mọi loại". 3 empty state riêng (filter / folder trống / chưa có dữ liệu).
- **Dọn:** bỏ dead routes `/tasks` `/files` `/calendar` + trang `Projects` placeholder.
- **Prototype v2:** `docs/prototype/workspace-v2.html` — prototype tương tác self-contained (đổi context/view, drag-drop cột + gán folder, drawer, dark mode, URL contract sống) + 7 nhóm spec viết. FE code/fix theo file này.
- Files: `Sidebar.tsx`, `Inbox.tsx`, `KanbanBoard.tsx`, `router.tsx` (xoá `Projects.tsx`).

## [2026-07-04] Tag management BE (SCRUM-70) + tạo ticket FE (SCRUM-71)

> Entity `Tag`/`TagAssignment` đã tồn tại trong schema từ đầu (migration `InitialCreate`) nhưng **chưa có ticket, chưa có API** — chỉ nằm trong DB. Bổ sung lớp BE để dùng được, đồng thời tạo ticket FE (làm sau).

- **Bảng đã có sẵn:** `Tags`/`TagAssignments` từ `InitialCreate` (composite PK, cascade User→Tag→TagAssignment, Item→TagAssignment NoAction để tránh 2 đường cascade). Chỉ thêm Controller/Service/Repository/DTO/Validator theo layered convention.
- **Unique tên tag = trong phạm vi 1 user, enforce ở DB** qua unique index `IX_Tags_UserId_Name` (migration `AddTagUserNameUniqueIndex`). `Tag.Name` cố ý *không* unique toàn hệ thống (mỗi user có "namespace" tag riêng). Service vẫn check `NameExistsAsync` trước → 409 sớm với thông báo đẹp; unique index là backstop cho **TOCTOU race** (2 request POST cùng tên đồng thời cùng vượt check) → `DbUpdateException` map về 409 trong `SaveOrThrowConflictAsync`.
  - ⚠️ **Đảo quyết định ban đầu:** bản đầu chốt "enforce ở service layer, KHÔNG index DB, không migration". Sau **code-review PR #70** chỉ ra race window → owner đồng ý thêm unique index + migration `AddTagUserNameUniqueIndex` (đã apply DB dev). Index thay `IX_Tags_UserId` (UserId cột đầu vẫn cover FK).
- **Assign/unassign qua junction `TagAssignment`** giống pattern `ItemFolder` của Folder (SCRUM-65): `POST /api/tags/{id}/items` + `DELETE /api/tags/{id}/items/{itemId}`. Cả tag lẫn item phải thuộc `CurrentUserId` (404 nếu không), trùng gắn → 409.
- **Xoá tag = hard delete**, cascade dọn `TagAssignment`, **Item giữ nguyên** (đúng nguyên tắc "không soft delete"; tag chỉ là label, gỡ label không xoá nội dung).
- **FE tách riêng SCRUM-71** (quản lý tag + chip + gắn/gỡ + filter theo tag) — chưa làm, chờ đợt sau.

## [2026-06-30 — kế hoạch, ĐANG TRIỂN KHAI theo nhánh] Đại tu Auth: HttpOnly cookie + refresh token (Redis) + OTP đăng ký (Twilio)

> ⚠️ **VƯỢT SCOPE SCRUM-42 và thay đổi NỀN TẢNG AUTH chung** (Lộc/Khánh/Vũ phụ thuộc). Yêu cầu phát sinh từ owner (ngoài board lúc ghi). Đã tách thành **3 ticket mới SCRUM-62/63/64** (xem SPRINTS.md) + làm theo **3 nhánh riêng** để dễ review, không dồn vào PR SCRUM-42. Ghi lại đây để cả nhóm nắm "tại sao" vì nó **đảo nhiều quyết định cũ** ở CLAUDE.md.

### Bối cảnh — đảo các quyết định cũ
- CLAUDE.md cũ: *"Token encryption (Data Protection) chỉ cho OAuth connection token, KHÔNG cho JWT login"*, *"stateless JWT MVP, không blacklist, không refresh"*, dùng `AddDistributedMemoryCache`, đăng ký = email+password đơn giản. Đợt này thay đổi cả 4 điểm trên.

### Quyết định 1 — Access token → **HttpOnly cookie** (SCRUM-62)
- **Vấn đề với yêu cầu gốc "mã hoá token rồi lưu cookie":** mã hoá ở **client là bảo mật giả** — FE là JS, key nằm trong bundle, ai mở DevTools cũng giải mã được. Cách đúng để "ẩn token khỏi JS" là **HttpOnly cookie** do server set (JS không đọc được) → chống XSS đánh cắp token. **Không tự mã hoá ở FE.**
- **BE:** login / google-callback / register-verified set JWT vào cookie `wh_access` (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, `Max-Age=expiresIn`). JwtBearer đọc token từ cookie qua `OnMessageReceived` (fallback vẫn nhận `Authorization: Bearer` để Swagger/Postman dùng được). **KHÔNG còn trả `accessToken` trong body** (chỉ trả `user` + `expiresIn`).
- **CSRF:** cookie tự gửi kèm → phải chống CSRF. Dùng **double-submit cookie**: thêm cookie `wh_csrf` (KHÔNG HttpOnly) + middleware bắt buộc header `X-CSRF-Token` khớp trên mọi request mutating (POST/PUT/PATCH/DELETE). FE đọc cookie `wh_csrf` gắn vào header.
- **CORS:** dev dùng Vite proxy (same-origin → không cần CORS). Prod: `AddCors` với `WithOrigins(FE)` + `AllowCredentials()` (KHÔNG dùng `AllowAnyOrigin` cùng credentials — bị cấm). Cookie cross-site prod cần `SameSite=None; Secure`.
- **FE:** bỏ `tokenStore`/localStorage, axios `withCredentials: true`, bỏ interceptor gắn Bearer; thêm interceptor đọc `wh_csrf` → `X-CSRF-Token`. `AuthContext.login` không nhận token nữa, chỉ set user cache + gọi `/auth/me`.

### Quyết định 2 — **Refresh token + Redis** (SCRUM-63)
- App chuyển từ **stateless → có refresh token server-side**. Access token TTL ngắn (vd 15 phút); refresh token TTL dài (vd 7 ngày) lưu **Redis** (key `refresh:{jti}` → userId + metadata), set vào cookie `wh_refresh` (HttpOnly, `Path=/api/auth/refresh`).
- **Luồng = sơ đồ Client/Cookie/Redis (chốt 2026-06-30):** access + refresh token đều ở **HttpOnly cookie** phía client; refresh có **bản đối chiếu ở Redis**. Cách đối chiếu = **JWT refresh + `jti`** (Redis lưu `jti → metadata`, verify = check chữ ký JWT + tra jti còn sống) — KHÔNG dùng opaque-token-hash. Chọn jti để thống nhất hạ tầng JWT sẵn có; revoke vẫn bằng xoá key Redis như cách hash.
- **Rotation:** mỗi lần `/auth/refresh` cấp access mới + **xoay refresh token mới**, revoke token cũ (xoá key Redis). Phát hiện reuse token đã revoke → revoke cả family (chống token theft).
- **Hạ tầng:** thêm Redis qua **docker-compose** (`wh-redis`), đổi `AddDistributedMemoryCache` → `AddStackExchangeRedisCache` (dev fallback in-memory nếu thiếu Redis, log warning). OTP (QĐ 3) cũng dùng Redis store này.
- **Logout giờ STATEFUL:** revoke refresh token trong Redis + clear cả 3 cookie. (Khác MVP cũ "client tự xoá token".)
- **FE:** interceptor 401 → gọi `/auth/refresh` 1 lần → retry request gốc (single-flight queue tránh refresh dồn); refresh fail → logout + về /login.

### Quyết định 3 — **OTP đăng ký qua SMS (Twilio)** (SCRUM-64)
- **Flow (chốt): tạo account trước, verify sau.** Register tạo user ngay với `PhoneVerified=false` (+ cột `Phone`), gửi OTP qua SMS; user nhập OTP ở `/auth/verify-otp` để set `PhoneVerified=true`. **Login chặn user `PhoneVerified=false`** (trả 403 + tín hiệu cần verify) — trừ Google Sign-In (bỏ qua OTP, không có phone).
- **OTP store:** Redis key `otp:{userId}` → mã 6 số hash + count, TTL 5 phút; rate-limit gửi lại (cooldown 60s) + tối đa N lần verify sai.
- **Provider:** **Twilio** (trial — đủ cho đồ án). `ISmsSender` ở Application; `TwilioSmsSender` ở Infrastructure đọc `Sms:Twilio:AccountSid/AuthToken/FromNumber` từ config. Dev có thể dùng `LogSmsSender` (ghi OTP ra log) khi chưa cấu hình Twilio.
- **DB:** migration thêm `Users.Phone` (string null), `Users.PhoneVerified` (bool, default true cho user cũ để không phá đăng nhập hiện có). Endpoint mới: `POST /api/auth/send-otp`, `POST /api/auth/verify-otp`.
- **FE:** Register thêm field SĐT; sau register điều hướng màn nhập OTP (resend + đếm ngược).

### Fix sau code-review (PR SCRUM-63/64)
- **TOCTOU refresh rotate (63):** tiêu thụ jti bằng GETDEL atomic (Lua) qua `IConnectionMultiplexer` khi có Redis; dev in-memory fallback get+remove.
- **OTP TTL không reset khi nhập sai (64):** lưu absolute expiry trong value Redis (`hash:attempts:expiryTicks`), update đếm dùng TTL còn lại → cửa sổ tấn công cố định 5' (không gia hạn theo mỗi lần sai).
- **OTP hash:** SHA-256 → **HMAC-SHA256 keyed theo userId** (chống rainbow table dùng chung) + so sánh `FixedTimeEquals`.
- **Chống user enumeration (64):** `/auth/send-otp` luôn 200 (im lặng nếu email không đủ điều kiện); `/auth/verify-otp` trả 422 đồng nhất cho mọi case không hợp lệ.
- **Twilio config:** đọc 1 lần ở ctor `TwilioSmsSender` thay vì mỗi lần gửi.
- **docker-compose:** SA password đọc từ `.env` (gitignored) `${MSSQL_SA_PASSWORD:-...}` thay vì hardcode; thêm `.env.example`.
- **Đã làm (review vòng sau):** tách `IJwtTokenFactory` chung cho AuthService + RefreshTokenService (bỏ duplicate access-token gen); GETDEL atomic chống TOCTOU khi rotate refresh token (Redis thật).
- **Để backlog (đồng ý với review):** rate-limit `/auth/refresh`; race TOCTOU trong `OtpService.SendAsync` (risk thấp, single-instance dev).

### Dev ergonomics (2026-06-30, sau review)
- **FE proxy → HTTPS:** `vite.config.ts` đổi target `/api` từ `http://localhost:5118` sang `https://localhost:7010` (`secure:false` cho dev cert tự ký). Chạy BE bằng `--launch-profile https`. Lý do: dev/test sát prod (cookie `Secure`, HTTPS) hơn.
- **SMS fallback chặt hơn:** chỉ chọn `TwilioSmsSender` khi **đủ cả** `AccountSid` + `AuthToken` + `FromNumber`. Twilio trial chưa mua số (`FromNumber` trống) → tự fallback `LogSmsSender` ghi OTP ra console — team test OTP không cần gọi Twilio thật. Trước đây chỉ check `AccountSid` nên sẽ chọn Twilio rồi fail vì thiếu From.

### Ngoài scope đợt này (cố ý)
- KHÔNG đụng mã hoá **OAuth connection token** (Data Protection giữ nguyên). KHÔNG làm email-verification (chỉ phone OTP). KHÔNG đa thiết bị/quản lý session nâng cao (chỉ rotation cơ bản). Multi-region Redis, Twilio production (mua số) → để sau.

## [2026-06-28] Fix code-review phase Jira (PR #46)

- **`ProviderAccountId` Jira = cloudId (KHÔNG phải account_id):** `JiraStrategy` trước lưu `account_id` từ `/me`, nhưng base URL gọi Jira REST là `https://api.atlassian.com/ex/jira/{cloudId}/rest/api/3` → sai giá trị làm mọi call 404. Đổi sang gọi `GET /oauth/token/accessible-resources`, lấy `id` (cloudId) của site đầu tiên làm `ProviderAccountId`. **Đây là bug chặn — Jira integration không thể hoạt động nếu không có fix này.**
- **Race condition rotating refresh token:** Atlassian xoay vòng refresh token. 2 request đồng thời cùng refresh → request thứ 2 dùng token đã vô hiệu. `AtlassianTokenService` thêm `SemaphoreSlim` per-connection (static `ConcurrentDictionary<Guid,...>`) + double-check (đọc lại tracked connection sau khi acquire lock) → chỉ 1 refresh chạy.
- **Refresh fail → 422 thay vì 500:** đổi `InvalidOperationException` (map 500) sang `BusinessRuleException`/`ProviderException` để client nhận tín hiệu re-auth đúng (422/502).
- **`issueUrl` để null:** browse URL Jira là `https://{site}.atlassian.net/browse/{KEY}` cần TÊN SITE, không phải cloudId. Connection chỉ lưu cloudId → để `null` thay vì emit link sai (`api.atlassian.com/.../browse` → API error). Site URL persist ở phase sau nếu cần.
- **Clear description dùng ADF doc rỗng:** `AdfConverter.FromPlainTextOrEmptyDoc` trả `{type:doc,version:1,content:[]}` khi text rỗng → thật sự xoá description (trước gửi paragraph chứa " ").
- **Sync cập nhật issue đã tồn tại:** `JiraSyncService` trước skip hẳn issue đã sync → local Item stale mãi. Giờ fetch tracked items (`IItemRepository.GetTrackedByConnectionIdAsync`), re-sync cập nhật field provider (Title/Snippet/ETag/OccurredAt/IsImportant/Metadata) nhưng GIỮ field local (Status Kanban, folders, IsArchived).
- **Accept header ở DI:** `AddHttpClient("Jira", ...)` set `Accept: application/json` 1 lần (tránh `.Add` tích luỹ per-request); Authorization vẫn set per-request.
- **ADF reader bổ sung:** emoji (`text`→`shortName` fallback), `bulletList`/`orderedList` là block node (có separator).
- **Feedback KHÔNG áp dụng:** (a) "thiếu OAuth start/callback cho Atlassian" — thực ra ĐÃ có (dùng chung `ConnectionsController` + `JiraStrategy`); vấn đề thật là cloudId, đã fix ở trên. (b) "dispatcher không catch Jira exceptions" — `JiraGateway` đã throw typed exceptions (ProviderException→502, Forbidden→403...) middleware map đúng; lỗ hổng thật chỉ ở `AtlassianTokenService` ném `InvalidOperationException`, đã đổi sang typed.

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
- **Conflict:** thêm Items.ETag, so trước khi ghi, lệch → 409. Bỏ qua kiểm tra conflict ETag cho riêng Email do `HistoryId` của Gmail thay đổi liên tục từ các tác vụ bên ngoài, dễ gây ra false-positive 409 khi người dùng cập nhật trạng thái đọc/chưa đọc/sao trên app.
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
