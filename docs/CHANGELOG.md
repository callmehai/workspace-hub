# CHANGELOG — Thay đổi thiết kế

> Ghi lại các quyết định thiết kế lớn để cả nhóm và Claude Code nắm bối cảnh "tại sao".

## [2026-07-18] Jira multi-site từng-grant-một + callback UPSERT (fix nút "Kết nối lại")

> QA multi-account phát hiện 2 vấn đề ở luồng connect: (1) cùng account Atlassian không thêm được site thứ 2 — `JiraStrategy` luôn lấy `resources[0]` → 409 trùng cloudId; (2) trùng đúng service+account → 409 "Hãy ngắt kết nối trước" — tức nút **"Kết nối lại"** (connection Error) xưa giờ luôn 409, phải disconnect mới reconnect được.

- **Jira chọn site chưa connect:** Atlassian KHÔNG cho biết user chọn site nào ở màn consent (`accessible-resources` trả **cộng dồn** mọi site đã cấp quyền) → heuristic: lấy site đầu tiên **chưa có connection Active** của user. Connect lần 2 cùng account = ăn site kế tiếp. Khác quyết định "bỏ Jira multi-site" trước đó: mỗi site giờ đến từ **1 grant riêng** (authorize riêng) → refresh token **độc lập**, KHÔNG dính vụ "nhiều site chung 1 refresh token xoay vòng" (vụ đó chỉ xảy ra khi tách N site từ CÙNG 1 grant). `ExchangeCodeRequest.ExistingActiveProviderAccountIds` mang danh sách account Active cùng provider vào strategy (Google bỏ qua).
- **Callback UPSERT thay 409:** trùng đúng `(UserId,Provider,ServiceType,ProviderAccountId)` → cập nhật AccessToken/RefreshToken (giữ cái cũ nếu provider không trả mới) + `Status=Active`, giữ `CursorValue` (cursor delta-sync vẫn hợp lệ cùng account). Nút "Kết nối lại" hoạt động thật; connect lại account đã Active = làm mới token, vô hại. Jira mọi site đều Active → rơi về site đầu → upsert.
- **Lưu ý QA:** cần verify bằng account Atlassian thật có ≥2 site: connect 2 lần → 2 connection 2 cloudId; sync/refresh site này không làm site kia rớt token (giả định grant mới không revoke grant cũ — hành vi chuẩn multi-device của OAuth).

## [2026-07-18] Gửi mail — bỏ auto-tạo nháp (giật màn) → nút "Lưu nháp" chủ động

> Bug UX ở trang Gửi mail: điền người nhận + chọn template → sau ~2s tự tạo nháp → **màn giật/load lại toàn trang** + lưu nháp ngoài ý muốn.

- **Root cause:** debounce 2s auto-**tạo** nháp (`createDraft`) khi chưa có nháp; `onSuccess` set `draftItemId` → bật lại 2 query `draft-item`/`draft-thread` (`enabled: !!draftItemId`) → **loading guard render spinner toàn màn** → form nháy + hydrate lại.
- **Fix:** (1) tách `hydrateDraftId` (chỉ nháp mở từ URL mới fetch+hydrate) khỏi `draftItemId` (dùng cho save) → nháp **tự tạo trong phiên KHÔNG fetch lại** ⟹ hết giật. (2) Auto-save **chỉ update nháp ĐÃ tồn tại** (`if (!draftItemId) return` ở cả debounce lẫn save-on-unmount) — **không** tự tạo nháp. (3) Thêm nút **"Lưu nháp"** để user chủ động tạo nháp lần đầu; sau đó auto-save (update) tiếp quản. i18n `sendEmail.saveDraft`/`draftEmpty`.
- **Giữ nguyên:** mở nháp từ Inbox (URL `?draftItemId=`) vẫn hydrate + auto-save như cũ; gửi thẳng không qua nháp vẫn hoạt động.
- **Fix kèm (đa tài khoản):** nháp gắn cứng 1 mailbox Gmail — BE chặn `updateDraft` khi `item.ConnectionId != request.ConnectionId` ("Draft connection mismatch"). Multi-account làm lộ: đổi dropdown "Kết nối" sang account khác khi đang có nháp → auto-save fail âm thầm + Gửi lỗi 422. Fix: **khoá selector tài khoản khi `draftItemId` tồn tại** (+ hint `sendEmail.connectionLocked`) và ghim `conn=resolvedConn` lúc tạo nháp (tránh `resolvedConn` trôi về `activeGmail[0]`). Chưa có nháp → vẫn đổi account tự do.

## [2026-07-18] Multi-connection per integration — nhiều tài khoản Google mỗi service (chưa có ticket Jira)

> Cho phép 1 user kết nối **nhiều tài khoản Google** (nhiều Gmail/Calendar/Drive khác email). Hoá ra **mô hình B đã thiết kế sẵn** cho đa tài khoản → chủ yếu là mở UI + 1 chỉnh nhỏ OAuth.

- **Vì sao gần như không đụng backend:** unique index `(UserId,Provider,ServiceType,ProviderAccountId)` đã gồm `ProviderAccountId` (2 Gmail khác email = 2 row hợp lệ); connect flow đã chặn trùng theo *account* (không theo service); sync/write-back/send/scheduled đều theo `connectionId` tường minh. Không đổi schema, không đổi dedup.
- **BE — điểm THEN CHỐT (`prompt=select_account`):** CHỈ `GoogleAuthUrlBuilder.BuildForService` đổi `prompt=consent` → `prompt=select_account consent`. Không có `select_account`, Google tự dùng account đang đăng nhập → **không thêm được account thứ 2**. Giữ `consent` (đi cùng `access_type=offline`) để luôn được cấp lại refresh token. `BuildForLogin` KHÔNG đổi (Google Sign-In giữ nguyên). **`JiraStrategy` giữ `consent`** — Jira không multi-connection (xem dưới).
- **FE — trang Kết nối (Integrations):** mỗi service render **N account** (thay `connections.find` → `.filter`), mỗi account có Sync/Ngắt/Kết nối lại riêng (mutations vốn đã theo `connectionId`) + nút **"Thêm tài khoản"**. i18n `integrations.addAccount`/`accountsCount`, `toolbar.allAccounts`.
- **FE — bộ lọc tài khoản (Inbox/Kanban):** dropdown "Tài khoản" ở `WorkspaceToolbar` (chỉ hiện khi nguồn đang xem có ≥2 account; Gmail/Calendar/Drive — bỏ Jira vì đã có lọc project/assignee + cloudId GUID khó đọc) → truyền `connectionId` vào `GET /api/items` (param sẵn có). Reset khi đổi tab (tránh lọc vô hình). Drive upload/kéo-thả ưu tiên account đang lọc làm đích.
- **Jira: chỉ Google mở UI multi-account (chốt scope):** `select_account` + nút "Thêm tài khoản" + bộ lọc "Tài khoản" (Inbox/Kanban) **chỉ áp cho Google**. Card Jira **không** có nút "Thêm tài khoản" và `JiraStrategy` giữ `prompt=consent`. Hành vi connect/reconnect Jira (chọn site chưa connect + callback UPSERT) xem entry **"Jira multi-site từng-grant-một"** phía trên — đó là track riêng, không phải Google-style multi-account.
- **Giữ nguyên (đủ dùng):** Friends contact-suggest + gửi invite dùng Gmail-đầu (`FirstOrDefault`) — rất hiếm khi nhiều Gmail; không phá vỡ gì.
- **Lưu ý QA:** multi-account Google phải test bằng OAuth **thật** — dev thiếu `id_token` rơi về `dev-placeholder@gmail.com` → 2 account "dev" đụng unique index.

### Sau review đa chiều (cùng ngày) — 2 fix multi-Drive
- **Upload/tạo folder vào đúng Drive account của folder đang mở:** `DriveStackEntry` (drill-down) nay mang `connectionId` của folder. Trước đây ở view "Tất cả tài khoản", drill vào folder của Drive account B rồi upload lại lấy `driveConns[0]` (account A) → BE reject "parent khác connection". Nay đích upload = connection **sở hữu folder** (truyền `currentDriveFolderConnectionId` xuyên `Inbox → WorkspaceToolbar → WorkspaceNewMenu`); ngoài folder mới ưu tiên account đang lọc rồi Drive-đầu.
- **Tự bỏ lọc account khi account rớt Active:** effect ở Inbox + Kanban clear `accountFilter` khi account đang lọc bị disconnect/Error (dropdown ẩn khi <2 account nhưng `connectionId` cũ vẫn áp → list/board lọc ngầm vô hình; Kanban không có nút clear filters).

## [2026-07-17] Calendar sync — bỏ birthday/holiday khỏi WorkspaceHub

- **Lý do:** Birthday là `eventType=birthday` đặc biệt, có recurrence hằng năm; Google holiday thường nằm ở calendar phụ/subscribed calendar. Đưa các mục này vào Items/Inbox/Kanban làm UI nhiễu và dễ bung nhiều occurrence tương lai.
- **Sau:** `CalendarGateway.SyncEventsAsync` chỉ sync event chính (`eventTypes=default`) từ `primary`, không kéo birthday/special event vào app. Holiday calendar phụ vẫn không sync vì MVP chỉ đọc `primary`.
- **Giới hạn:** full sync Calendar giữ `TimeMin=now-3 months`; không đặt `TimeMax` để tránh đóng băng cửa sổ sync tương lai. Incremental sync vẫn dùng syncToken và cùng filter `eventTypes=default`.
- **Dữ liệu cũ:** không tự cleanup birthday đã lỡ sync trong DB; owner sẽ dọn thủ công nếu cần.


## [2026-07-17] Drive UX — detail preview/download + phân biệt folder/file ở Kanban

> Nâng chất lượng luồng Drive: drawer chi tiết file "nghèo nàn" → có preview + tải xuống; Kanban trước đây folder và file nhìn y hệt nhau.

- **BE — proxy media (2 endpoint mới):** `GET /api/drive/items/{id}/content` (tải/xem nội dung, `?dl=true` = attachment; Google-native docs export sang PDF/PNG) + `GET /api/drive/items/{id}/thumbnail` (proxy `thumbnailLink`, 204 nếu không có). **Stream thẳng — KHÔNG buffer file 100MB vào RAM** (`HttpCompletionOption.ResponseHeadersRead` + `CopyToAsync(Response.Body)`). Named `HttpClient` "DriveMedia" timeout Infinite, hủy theo CancellationToken. Layer: `IDriveGateway.DownloadFileAsync/GetThumbnailAsync` + `DriveMediaResult` (IAsyncDisposable ôm `HttpResponseMessage`) → service `IDriveContentService` (resolve item→connection, đọc mimeType từ metadata) → controller stream.
- **Vì sao proxy on-demand thay vì lưu thumbnailLink:** `thumbnailLink`/nội dung Google là URL ngắn hạn cần bearer token → không nhúng trực tiếp `<img>` được, và lưu vào metadata thì phải re-sync. Proxy live bằng token connection: không đổi schema, không re-sync.
- **Vì sao download = `<a download>` + probe `/auth/me`, KHÔNG blob:** auth là cookie HttpOnly → thẻ `<a>` tự gửi cookie, trình duyệt stream ra đĩa (không nạp 100MB vào RAM trình duyệt). Probe `/auth/me` trước để interceptor refresh token nếu hết hạn. Preview ảnh thì lấy blob qua axios (ảnh nhỏ, cần hưởng refresh 401 cho `<img>`).
- **FE — detail overhaul:** khối `DriveFilePreview` (ảnh nhỏ = nội dung gốc, ảnh lớn/PDF/video/google-docs = thumbnail, còn lại = icon + nhãn loại + link mở Drive); nút **Tải xuống** (file) / **Mở thư mục** (folder, điều hướng `?openDrive=` để Inbox seed drill-down); header dùng brand icon Drive/folder; bỏ khung "Nội dung" rỗng cho File; mimeType thô → nhãn thân thiện (`friendlyMimeLabel`).
- **FE — Kanban:** truyền `isDriveFolder` vào `typeIcon` + nhãn **"Thư mục"** (trước đây folder/file đều icon Drive + "Tệp"); sao quan trọng trên thẻ **bấm được** (optimistic trên cache từng cột board) — đồng bộ view Danh sách.
- **i18n:** thêm `type.folder`, `item.download`/`openFolder`, `drive.preview.*`, `drive.mime.*` (vi/en).

### Sau QA local (cùng ngày) — UX + điều hướng
- **Footer drawer bớt ngợp:** trước rải 6–7 nút ngang → giờ **hành động chính bên trái** (Tải xuống/Mở thư mục + Chia sẻ), **phụ gộp vào menu "..."** (quan trọng, đã/chưa xem, đổi tên, tạo folder con, mở ngoài) + nút xoá. Áp cho mọi loại item.
- **Điều hướng folder Drive qua history:** stack folder chuyển từ local state → **`location.state.driveStack` + `?df=<internalId>`** ⟹ **nút Back của trình duyệt lùi về folder cha** đúng (trước đây Back nhảy sang tab khác). Breadcrumb + double-click + "Mở thư mục" đều `navigate` push. Breadcrumb gốc hiện tên nguồn (Drive/…) thay vì luôn "Tất cả mục".
- **Nút "Mới" context-aware:** đang trong 1 folder Drive → **chỉ hiện option Drive** (Thư mục mới / Tải tệp / Tải thư mục), ẩn Ghi chú/Sự kiện/Ticket (tạo Ticket vào folder Drive là vô nghĩa).
- **Fix preview vỡ khi mở lại:** object URL phải **tạo + revoke trong CÙNG một effect** — dùng `useMemo` (như bản đầu) khiến StrictMode dev revoke URL mà memo không tính lại → ảnh vỡ. Đổi lại `useState`+`useEffect`.
- **Thumbnail nét hơn:** proxy nâng size param `=s220`→`=s1024` khi lấy `thumbnailLink`.

### Phase 4 — Upload UX (kéo-thả + tiến độ song song)
- **Hàng đợi upload toàn cục** `driveUploadStore` (module singleton + pub/sub, KHÔNG phụ thuộc React) → `DriveUploadPanel` mount 1 lần ở `MainLayout`, **sống xuyên trang**. Upload **SONG SONG** (concurrency 3) thay vì tuần tự; mỗi file **1 progress bar** (`axios onUploadProgress`).
- **Skip file lỗi:** file rỗng / quá 100MB bị **bỏ qua kèm lý do**, không chặn cả lô như trước.
- **Kéo-thả** (`DriveDropZone`): thả file → upload vào folder đang mở; thả thư mục → upload cả cây (entries API đệ quy). Chỉ phản ứng khi kéo **chứa file** (`types` có `Files`) → không đụng thao tác kéo thẻ Kanban. Bật ở tab Drive / "Tất cả mục".
- `WorkspaceNewMenu` "Tải tệp"/"Tải thư mục" chuyển sang enqueue store (chạy nền, bỏ toast-loop tuần tự + bỏ block nút "Mới").

### Sau QA local (2) — upload vào folder context + phóng to ảnh
- **Bug: upload khi đứng trong 1 folder context app (dự án/khách hàng) thì item rơi ra "Tất cả mục", KHÔNG vào folder đó.** Nguyên nhân: upload chỉ tạo Item trên Drive (root), không gán vào folder context — 2 trục độc lập. Fix ở FE: thêm `folderId` (folder context) vào `EnqueueOpts`; sau khi upload thành công gọi `foldersApi.addItemsToFolderBulk(folderId, [itemId])`. Áp cho **cả 3 đường**: kéo-thả (`DriveDropZone` nhận prop `folderId`), nút "Tải tệp"/"Tải thư mục" (`WorkspaceNewMenu` truyền `folder?.id`), và "Thư mục mới" (`CreateDriveFolderModal` nhận `folderContextId`). Upload folder chỉ gán **thư mục gốc** (item folder trùng tên) vào context, không gán từng file/subfolder con. Gán folder hụt (network) KHÔNG đánh sập task — item đã lên Drive.
  - `parentItemId` (vị trí trên Drive) và `folderId` (folder context app) là **2 trục độc lập** — upload vẫn lên My Drive root, chỉ thêm liên kết ItemFolder.
- **Tách `lib/driveDrop.ts`** (logic đọc entries + đệ quy cây thư mục + `handleDriveDrop`/`dragHasFiles`) dùng chung, gỡ code lặp trong `DriveDropZone`.
- **Preview ảnh bấm phóng to (lightbox):** `DriveFilePreview` — ảnh/thumbnail bấm mở overlay toàn màn hình (Esc / bấm nền / nút X để đóng), hover hiện gợi ý "Bấm để phóng to". Thêm i18n `drive.preview.zoom`.

### Sau QA local (3) — polish list/Kanban + điều hướng + gợi ý share
- **Folder xếp trước file (list Drive):** `ItemRepository` sort thêm `OrderByDescending("isFolder":true)` TRƯỚC `OccurredAt`, **chỉ khi view Drive** (`driveParentId != null` hoặc types == [File]) → các view Email/All giữ nguyên sort thời gian. Folder luôn nổi lên đầu như mọi trình quản lý file.
- **Filter Tệp/Thư mục cho Drive:** thêm query param `driveKind` (`folder` | `file`) xuyên `GetItemsRequest → ItemService → IItemRepository`. FE: chip **Tất cả / Thư mục / Tệp** ở toolbar, **chỉ hiện ở view Drive** (tab Tệp hoặc trong 1 folder Drive); Inbox + Kanban đều gửi param, và **chỉ áp khi đang ở Drive scope** (đổi tab khác tự bỏ, tránh lọc vô hình). i18n `toolbar.driveKind*`.
  - `folder` = metadata có `"isFolder":true`; **`file` = phần bù (NOT folder)** — item cũ / metadata tối giản thiếu hẳn field `isFolder` vẫn được coi là tệp, không bị giấu khỏi tab "Tệp" (thay vì đòi khớp cứng `"isFolder":false`).

### Sau review PR #114 — fix + test
- **Kanban "quan trọng" reconcile server:** mutation `toggleImportant` thêm `onSettled: invalidateQueries(['items'])` — đồng bộ với view Danh sách, tránh cache board giữ giá trị optimistic lệch nếu response server khác kỳ vọng.
- **Test BE mới:** `DriveContentServiceTests` (10 case — resolve item→connection: đúng user/service/active + folder guard, ủy thác gateway) và `ItemRepositoryDriveFilterTests` (InMemory chạy `GetPagedAsync` thật — filter `driveKind` folder/file **gồm item cũ thiếu `isFolder`** + sort folders-first).
- **Cảnh báo xoá folder Drive:** xoá 1 folder → dialog cảnh báo "sẽ chuyển cả thư mục + TOÀN BỘ nội dung vào Thùng rác Drive" (i18n `item.confirmDeleteDriveFolder`), thay câu xoá file thường.
- **Kanban empty-state "chưa kết nối":** board rỗng + chưa kết nối service của nguồn đang xem → panel dẫn sang trang Kết nối (giống view Danh sách), thay 3 cột trống khó hiểu.
- **F5 không văng khỏi folder con:** stack drill-down vốn sống trong `location.state` (mất khi reload). Nay nếu còn `?df` mà mất state → fetch item dựng lại 1 cấp → **người dùng vẫn ở trong folder** thay vì bật về gốc Drive. (Breadcrumb khi đó gọn còn folder hiện tại; điều hướng thường vẫn giữ đủ cấp.)
- **Gợi ý contact khi share:** `DriveShareDialog` — ô mời email có **gợi ý bạn bè** (friend system, chỉ Accepted, lọc theo chuỗi đang gõ) + **validate email** (nút mời khoá tới khi hợp lệ) + **Enter để mời**.
- **Chưa làm (ghi chú):** (1) *thống nhất branding thẻ Kanban* — thực tế thẻ đã dùng cùng brand-icon + nhãn folder/file như list, khác biệt còn lại chỉ là kiểu nền (pill vs tile), để sau. (2) *Ticket transition đúng mọi Jira project* — map cứng `Inbox→To Do / Doing→In Progress / Done→Done` chỉ đúng khi project dùng đúng 3 status mặc định; fix tổng quát cần **endpoint Jira lấy transitions động** (mảng riêng, chưa làm).

## [2026-07-16] Google Drive — Case 1 tắt link giống Drive (mở rộng SCRUM-79)

> Khi tắt link file mà folder mẹ đang "ai có link", app hỏi user giống Google Drive — không silent apply.

- **Case 1:** file + folder mẹ đều `anyone` → `PUT link-sharing` tắt không confirm → **409** + `DriveLinkRestrictConflict`; confirm `confirmRestrictParent: true` → tắt link **cả file lẫn folder mẹ** (file vẫn trong folder).
- **Case 2:** folder mẹ hạn chế, bật link file → **không popup** (khớp Drive web).
- **BE:** `DetectLinkRestrictConflictAsync`, `GET .../restrict-conflict`, `LinkSharingRequest.ConfirmRestrictParent`; test Case 1 trên `DriveSharingServiceTests`. `ConflictException.Payload` + middleware ghi payload làm body 409 (một path service→API, tránh 409 chỉ có message).
- **FE:** `DriveLinkRestrictDialog` (layout gần Drive: tiêu đề, cây quyền, Huỷ / Xoá khỏi thư mục mẹ); wire trong `DriveShareDialog` bắt 409.
- **Docs:** `docs/API.md`, `docs/DRIVE_FOLDER_SHARING.md` §6.4 / §7.6–7.7 / QA.

## [2026-07-18] CalendarInvitation InviteeItemId — NoAction + null hoá service layer

- **Không dùng ON DELETE SET NULL:** SQL Server Msg 1785 (multiple cascade paths) — `OrganizerItemId` đã `CASCADE` → `Items`; thêm `InviteeItemId SET NULL` bị reject. Migration `CalendarInvitationInviteeItemSetNull` thực tế no-op (FK vẫn NoAction).
- **Fix:** giữ `DeleteBehavior.NoAction`; trước khi xoá Item invitee → `ClearInviteeItemLinksAsync` / `ExecuteUpdate` null `InviteeItemId` (giữ row RSVP) tại `CalendarSyncService`, `ItemWriteBackService.DeleteItemAsync`, `ItemRepository.DeleteByConnectionIdAsync`.

## [2026-07-16] Calendar PR review — partial-update reminders + invitation harden

- **Critical — reminders wipe:** `CalendarGateway.UpdateEventAsync` khi `Reminders == null` từng ghi `UseDefault=true` → PATCH event (đổi title/…) xóa reminder Google. **Sau:** `null` = giữ nguyên từ `Events.Get`; non-null (kể cả list rỗng) = ghi overrides. Contract test: `PatchEvent_WithoutReminders_PassesNullRemindersToGateway`.
- **Important — InviteeItem FK:** ban đầu định `NoAction` → SET NULL (migration `CalendarInvitationInviteeItemSetNull`) — **không khả thi trên SQL Server** (xem [2026-07-18]); giữ NoAction + null hoá ở service.
- **Important — RSVP pending:** `ReconcileSyncedEventAsync` khi push RSVP local lên Google lỗi (`ProviderException` / `ForbiddenException`) giữ `GoogleSyncPending=true` + Status local; không ghi đè bằng Google `needsAction`.

## [2026-07-15] Calendar reminder notification — format thời gian

- **Bug:** In-app reminder (`EventReminderProcessorService`) khi không có Snippet nhét `OccurredAt.ToString("o")` vào `preview` → toast/dropdown hiện raw ISO (`2026-07-15T00:00:00.0000000Z`).
- **Sau:** Body = `{ itemTitle, start, allDay, preview? }` — `preview` chỉ còn text snippet; FE `formatNotificationDisplay` format `start` qua `formatEventWhen` (all-day = ngày UTC + nhãn «Cả ngày»). Legacy row ISO trong `preview` vẫn được nhận diện và format lại.

## [2026-07-15] Calendar invitee: hiện event khi còn NeedsAction

- **Trước:** FE chỉ render invitation/synced item khi `Accepted`/`Tentative` → khách chưa RSVP không thấy event trên lịch (dù đã có noti «sự kiện mới»).
- **Sau:** giống Google — `NeedsAction` vẫn hiện; chỉ ẩn `Declined`. Click event chưa RSVP mở dialog phản hồi (không mở EventDetailPopup chồng).

## [2026-07-15] All-day reminder `timeOfDay` ↔ Google Calendar minutes

- **Bug:** Hub lưu/UI đúng `1 tuần · trước lúc · 14:00`, nhưng write-back chỉ gửi `weeks×10080` (bỏ `TimeOfDay`) → Google hiện `1 week before at 12:00am`. **Không phải** lệch timezone UTC↔ICT.
- **Docs Google:** [Reminders](https://developers.google.com/workspace/calendar/api/concepts/reminders) — API chỉ có `minutes` trước start; all-day start = 00:00 ngày event.
- **Code:** `GoogleCalendarReminderMapper` + `MapToGoogleReminders` / `SyncLocalReminders`. `InApp` không đẩy Google; sync chỉ thay `GooglePopup`/`GoogleEmail` (InApp rows giữ nguyên). Tests: `GoogleCalendarReminderMapperTests`.

### Design — §1 Mapping (lõi)

Google chỉ lưu **minutes** trước **00:00** ngày all-day. Hub lưu **offset + timeOfDay**.

#### Write Hub → Google (`MapToGoogleReminders`)

| Unit | `timeOfDay`? | `minutes` gửi Google |
| --- | --- | --- |
| Minutes / Hours | bỏ qua | như hiện tại (`value` / `value×60`) |
| Days / Weeks | không / `"00:00"` | `value × 1440` hoặc `× 10080` |
| Days / Weeks | có (vd `"14:00"`) | `unitMinutes − (h×60+m)` — vd 1 tuần @ 14:00 → **9240** |

`InApp` không đẩy Google (giữ như hiện tại).

#### Sync Google → Hub (`CalendarSyncService`)

| `minutes` | Kết quả Hub |
| --- | --- |
| `< 1440` | `OffsetUnit=Minutes`, `TimeOfDay=null` |
| `≥ 1440` | decode Google-style all-day: `days = ceil(minutes/1440)` (chia hết → `minutes/1440`); nếu `days % 7 == 0` → `Weeks = days/7`, else `Days`; `timeOfDayMinutes = days×1440 − minutes` → `"HH:mm"` (vd **9240 → 1 week @ 14:00**) |

InApp rows trên Hub **không** bị xóa khi sync Google overrides (chỉ thay `GooglePopup` / `GoogleEmail`).

> **Ghi chú implement:** với event **timed** (`allDay=false`) luôn giữ Minutes thô. Với event **all-day**, decode all-day style cả khi `minutes < 1440` (vd 900 → `1 Day` @ `09:00`) để round-trip UI Google không mất giờ.

#### Ví dụ đối chiếu

| Hub (UI) | Google `minutes` | Google UI |
| --- | --- | --- |
| 1 tuần · 14:00 | **9240** (`10080 − 840`) | 1 week before at **2:00pm** |
| 1 tuần · (không / 00:00) | **10080** | 1 week before at **12:00am** ← bug cũ vẫn gửi case này dù Hub là 14:00 |
| 1 ngày · 09:00 | **900** (`1440 − 540`) | 1 day before at **9:00am** |
| 30 phút (timed) | **30** | 30 minutes before |

## [2026-07-14] Calendar guest email prompt + dọn attendee metadata cũ

- Khi create/edit làm thay đổi danh sách khách, FE hiển thị hộp thoại ba lựa chọn giống Google Calendar: quay lại chỉnh sửa, lưu nhưng không gửi email, hoặc gửi email. API nhận `sendUpdates`; Calendar gateway map sang Google `none|all` (mặc định vẫn là `all` để tương thích client cũ).
- Quyết định về phạm vi email guest: dialog trong WorkspaceHub chỉ là UI chọn có để Google Calendar gửi notification hay không; request sang Google vẫn dùng `sendUpdates=all` khi chọn gửi và `sendUpdates=none` khi không gửi. Google Calendar API chỉ công khai ba mức `sendUpdates`: `all` (notifications sent to all guests), `externalOnly` (non-Google Calendar guests only), `none` (no notifications); không có tham số target riêng người vừa thêm/xóa, nên việc Google có tự lọc/suppress email theo diff là hành vi nội bộ không được API cam kết. Nguồn docs: https://developers.google.com/workspace/calendar/api/v3/reference/events/update, https://developers.google.com/workspace/calendar/api/v3/reference/events/patch, https://developers.google.com/workspace/calendar/api/v3/reference/events/insert.
- `sendUpdates=false` chỉ tắt email do Google Calendar gửi; invitation và notification in-app vẫn được reconcile để user WorkspaceHub nhận lời mời trong app.
- Fix lỗi xóa khách cuối cùng: Google trả `attendees=null`, backend nay xóa khóa `attendees` khỏi metadata local thay vì giữ danh sách cũ. Khi mở editor, FE ưu tiên attendee live từ endpoint calendar details để tự sửa cả snapshot cũ trước lần sync tiếp theo.
- Thêm unit test cho việc truyền lựa chọn không gửi email và dọn metadata khi attendee cuối cùng bị xóa.

## [2026-07-13] Calendar invitations + RSVP trong app + guest permissions

- Google Calendar là nguồn sự thật của event; create/update có attendees dùng `sendUpdates=all`, vì vậy email mời do Google Calendar gửi. Gmail chỉ được tái sử dụng cho contact suggestions, không gửi email mời trùng.
- Thêm `CalendarInvitations` để user nội bộ nhận notification và phản hồi `Accepted/Tentative/Declined` ngay trong WorkspaceHub. Event được reconcile giữa organizer/invitee bằng `iCalUID`; nếu invitee chưa connect GCal thì lưu `GoogleSyncPending` nhưng event accepted/tentative vẫn hiện trong app.
- Sync hai chiều cập nhật attendee response và liên kết `InviteeItemId`; thay đổi attendee từ phía Google cũng tạo/gỡ invitation nội bộ ở lần sync kế tiếp.
- Ba quyền Google (`guestsCanModify`, `guestsCanInviteOthers`, `guestsCanSeeOtherGuests`) được lưu metadata, ghi/đọc Google và enforce ở backend. UI ẩn sửa/xóa/guest list tương ứng; organizer luôn có toàn quyền.

## [2026-07-12] Jira deadline trên Calendar — sync `fields.duedate`

- **Gap:** FE overlay Jira đã có (violet, read-only) nhưng `JiraItemMapper` không map `fields.duedate` → `DueAt`/`metadata.dueDate` luôn null → ticket không lên lịch; overlap query còn match ticket theo `OccurredAt=updated` (sai).
- **BE:** `JiraGateway` request thêm field `duedate`; `JiraIssue.DueDate`; mapper: có due → `OccurredAt`=start ngày UTC, `DueAt`=end exclusive (+1 ngày, all-day), `metadata.dueDate`=`yyyy-MM-dd`; không due → `OccurredAt=updated`, `DueAt`=null`. `JiraSyncService` + `PatchTicketAsync` cập nhật `DueAt` khi re-sync/remap.
- **Query:** `ItemRepository` calendar overlap loại Ticket không có `DueAt` (chỉ deadline mới lên lịch).
- **Re-sync:** ticket đã sync trước đó cần sync lại connection Jira để populate deadline.
- **Tests:** `JiraItemMapperTests` (+2 case due date).

## [2026-07-12] Gỡ bỏ khái niệm "task" khỏi Calendar — chỉ Google Calendar Event

- **Lý do:** "task" chỉ là nhãn app-only (`metadata.calendarType="task"`) trên `ItemType.Event`, **không** dùng Google Tasks API. Gây phức tạp (nhánh `isTask` rải BE, merge metadata, toggle UI) mà không có giá trị thật.
- **BE:** xóa `CalendarType` khỏi `PatchItemRequest`/`CreateEventRequest`; `End` **luôn bắt buộc** khi create (all-day FE gửi ngày kế); bỏ mọi nhánh `isTask` trong `ItemWriteBackService` (luôn cho location/attendees); xóa `CalendarSyncMetadataMerge` → sync ghi thẳng metadata Google.
- **FE:** bỏ toggle Event/Task + `calendarType` khỏi modal/form utils/types, xóa i18n keys `typeEvent`/`typeTask`/`taskDueDate`/`taskNotes`/`createTask`…
- **DB:** không migration — row cũ có `calendarType` bị ignore, hiển thị như all-day event thường; sync sau ghi đè metadata.
- **Docs:** `docs/superpowers/specs/2026-07-12-remove-calendar-task-design.md`.
- **Tests:** bỏ `PatchEvent_TaskWithAllDayFalse_*` + `CalendarSyncMetadataMergeTests`; 334/334 pass.

## [2026-07-11] Calendar all-day ↔ timed write-back (Google PATCH vs UPDATE)

- **Bug:** Kéo event cả ngày xuống slot có giờ (week view) → Google **400 Invalid start time** → app **502**. Nguyên nhân: `Events.Patch` merge giữ `start.date` cũ cùng `start.dateTime` mới (Google cấm lẫn hai loại).
- **Fix:** `CalendarGateway.UpdateEventAsync` dùng **GET + `Events.Update`**; helper `ApplyUpdateTimes` thay whole `Start`/`End` (chỉ `date` hoặc chỉ `dateTime` + `TimeZone=UTC`).
- **Docs:** `docs/superpowers/specs/2026-07-11-calendar-all-day-timed-google-update-vs-patch.md`
- **Tests:** `ItemWriteBackServicePatchEventTests.cs` (9 case all-day/timed/task).

## [2026-07-11] Calendar sync Drive attachments + gộp gateway

- **Bug/gap đã fix:** Event gắn file Drive trên Google Calendar → sync về app → `metadataJson` có `driveAttachments` (map `Event.attachments[]`).
- **Match `fileId` → `driveItemIds`:** Google Calendar `attachment.fileId` = Drive Item `ExternalId` (cùng Google file id). Sync lookup `Item` type `File` theo user + `ExternalId` → ghi `metadata.driveItemIds` (Guid nội bộ) khi file đã sync Drive; không match thì chỉ có `driveAttachments` (link hiển thị vẫn OK).
- **Metadata merge:** Khi sync update, preserve `calendarType` (app-only); refresh attachment snapshot từ Google.
- **Refactor:** Gộp `GoogleCalendarGateway` vào `CalendarGateway` — một `ICalendarGateway` cho sync + CRUD. Xóa `IGoogleCalendarGateway`.
- **Docs:** `docs/superpowers/specs/2026-07-11-calendar-sync-attachments-gateway-merge-design.md`, `docs/superpowers/plans/2026-07-11-calendar-sync-attachments-gateway-merge-plan.md`.

## [2026-07-10] Calendar edit + sync hoàn thiện (BE + FE)

- **PATCH Event parity create:** `PatchItemRequest` thêm `allDay`; `UpdateEventAsync` mirror `InsertEventAsync` (all-day `Date`, Drive attachments); sau patch cập nhật `occurredAt`/`dueAt` + `metadata.start`/`end`/`allDay`/`description`/`driveItemIds`.
- **Sync:** `CalendarEventDto` có `ETag`/`AllDay`; mapper dùng `Start`/`End` cho `OccurredAt`/`DueAt`; incremental sync xóa Item khi Google trả `status=cancelled`.
- **Query lịch:** `GET /api/items?occurredFrom&occurredTo` (UTC, overlap) — Calendar FE chỉ tải event trong grid tháng/tuần (`limit` max 200).
- **FE:** một modal `CalendarEventEditorModal` cho create/edit (Calendar, Inbox toolbar, ItemDetail); invalidate `calendar-items` sau PATCH; drag week slot → `allDay=false`.

## [2026-07-10] Calendar workspace view (FE)

- **View thứ ba có kiểm soát:** thêm route `/calendar` cạnh Danh sách/Bảng; chỉ hiện nút Lịch ở **Tất cả mục**, nguồn **Google Calendar (Event)** và folder. Email/Jira/Drive chỉ có Danh sách–Bảng; nếu deep-link `/calendar?type=Email|Ticket|File` thì redirect về Danh sách đúng nguồn. View switcher và Sidebar giữ nguyên `?folder=` khi đổi view/context.
- **Hai chế độ:** Tháng + Tuần; tuần chia slot 30 phút (07:00–21:00) và có hàng **Cả ngày**. Click slot mở form với ngày/giờ có sẵn; điều hướng được các tháng/tuần và quay về hôm nay.
- **Ba lớp thời gian:** Google Calendar Event (amber, CRUD/write-back), ScheduledEmail Pending (blue, read-only, mở màn Email hẹn giờ), Jira deadline (violet, read-only, mở ItemDetail). Khi vào folder, chỉ Item Event/Ticket đã gắn folder được hiển thị; ScheduledEmail hiện chỉ có ở lịch chung vì schema chưa có FolderId.
- **Drag/drop:** chỉ Event có `draggable`; month drop giữ giờ hiện tại, week timed-slot drop đổi ngày+giờ, week all-day row đổi thành cả ngày. Jira/ScheduledEmail tuyệt đối read-only trên lịch.
- **Contract BE cần khớp:** FE gửi `allDay` kèm `start/end` để tương thích API hiện tại; BE Calendar cần map `allDay=true` sang `EventDateTime.Date` để Google lưu đúng event cả ngày. Jira overlay đọc `ItemResponse.dueAt` hoặc `metadata.dueDate`; Jira sync phải populate một trong hai field thì deadline mới xuất hiện.

## [2026-07-16] SCRUM-64 đổi hướng OTP: Email (Resend) thay cho SMS/Firebase

> **Quyết định scope:** OTP đăng ký chuyển sang **gửi qua email** dùng **Resend**. Bỏ CẢ hai hướng cũ: SMS Twilio (develop) và Firebase Phone Auth (nhánh `fix/login-ux`).

- **Vì sao không dùng Gmail cá nhân để gửi OTP:** luồng gửi mail hiện tại (`IGmailGateway.SendMessageAsync`) cần một `Connection` OAuth của user — mà OTP xảy ra TRƯỚC khi user đăng nhập/connect, nên phải là **sender hệ thống**. Dùng token Gmail cá nhân làm single-point-of-failure (hết hạn/revoke → sập đăng ký), giới hạn ~500 mail/ngày, dễ bị Google gắn cờ. → chọn Resend (HTTP API thuần, free 3k/tháng, tách abstraction như `ISmsSender` cũ).
- **Tên abstraction:** sender hệ thống đặt là `ISystemEmailSender`/`ITransactionalEmailSender` (KHÔNG đặt `IEmailSender` để tránh nhầm với `IGmailGateway` — luồng gửi mail nghiệp vụ qua Gmail của user: SendEmail/ScheduledEmails/mail mời kết bạn). Impl: `ResendEmailSender` + dev fallback `LogEmailSender`.
- **Giữ nguyên:** toàn bộ logic Redis/hash/cooldown/attempts trong `OtpService` (chỉ đổi kênh gửi SMS→Email + tham số phone→email). Chống enumeration ở `send-otp`/`verify-otp` giữ nguyên.
- **DB:** migration mới **rename `Users.PhoneVerified`→`EmailVerified` + drop cột `Phone`**. ⚠️ EF KHÔNG tự sinh `RenameColumn` (sẽ ra Drop+Add làm user chưa-verify thành `EmailVerified=true`) → phải **sửa tay** file migration thành `RenameColumn`. Cũng phải sửa `AppDbContext` (cấu hình `Phone`/`PhoneVerified`).
- **Login gate:** giữ chặn — chưa verify → 403, đổi mã `PHONE_NOT_VERIFIED`→`EMAIL_NOT_VERIFIED`.
- **Rủi ro cần chốt sớm (không phải lưu ý nhỏ):** (1) **verify domain `workspace-hub.space` trên Resend** — test mode chỉ gửi được tới chủ tài khoản Resend; giám khảo đăng ký bằng email của họ sẽ không nhận OTP. Cần thêm DNS SPF/DKIM (propagate lâu). (2) **Rate limit theo IP** cho `/auth/register` + `/auth/send-otp` (Program.cs chưa có `AddRateLimiter`) — email free tier bị spam sẽ đốt hết quota. (3) Bọc `try/catch` quanh gửi OTP trong `RegisterAsync` để lỗi provider không kẹt user trong DB.
- **Bỏ Firebase:** xoá `IFirebasePhoneVerifier`/`FirebasePhoneVerifier`, `frontend/src/lib/firebase.ts`, package `firebase` + nuget `FirebaseAdmin`, config `Firebase:ProjectId`. Giữ cải tiến của nhánh: `IGoogleTokenVerifier.VerifyAsync` trả `(Sub, Email, Name)` → user Google mới lấy FullName thật thay vì `email.split('@')`.

## [2026-07-10] Friend system nội bộ app (đổi hướng từ Google Contacts)

> **Quyết định scope:** bỏ hướng đồng bộ Google Contacts / People API (PR #100) — bạn bè chỉ có ý nghĩa TRONG app, không liên kết bên thứ 3. Kết bạn = nhập email gửi lời mời.

- **Model:** `Friendships` (1 row/cặp, Requester→Addressee, Status Pending/Accepted, tier `Friend`/`CloseFriend` lưu RIÊNG từng phía, 2 FK Users NoAction) + `FriendInvites` (email chưa có tài khoản: Token unique, hạn 14 ngày, UNIQUE(inviter,email)). Migration `AddFriendSystem`.
- **Luồng kết bạn:** email đã có tài khoản → Pending + notification (`FriendRequest`); phía kia mời mình trước → auto-accept; chưa có tài khoản → invite + **mail mời gửi qua chính Gmail connection của người mời** (không SMTP riêng; không gửi được → FE hiện link copy). Link `{App:FrontendBaseUrl}/register?inviteToken=`.
- **Consume khi đăng ký** (`AuthService` hook, best-effort không fail register): token khớp → bạn bè NGAY (bấm link = đồng ý); đăng ký/Google Sign-In trùng email không token → chuyển thành lời mời Pending in-app.
- **API:** `GET /api/friends` (overview), `POST /api/friends/requests`, `POST /api/friends/{id}/accept`, `DELETE /api/friends/{id}` (decline/hủy/unfriend), `PATCH /api/friends/{id}/tier`, `DELETE /api/friends/invites/{id}`, public `GET /api/friends/invites/by-token/{token}`. `POST /api/auth/register` thêm `inviteToken?`.
- **FE:** trang `/friends` (form kết bạn, lời mời đến/đi, invite email + copy link, badge Bạn thân ⭐, gửi mail nhanh cho bạn → `/send-email?to=`), sidebar mục Bạn bè, RegisterPage banner "X mời bạn" + prefill email. Config mới `App:FrontendBaseUrl` (prod: `App__FrontendBaseUrl` trong docker-compose).
- **Tương lai (chưa làm):** share folder cho bạn theo role, tạo event cùng bạn, nhóm bạn tuỳ biến.

## [2026-07-10] Jira description = Markdown subset 2 chiều + UX nháp/confirm

- **Description Jira đổi từ plain text → Markdown subset** (cùng chuẩn với comment): đọc `AdfConverter.ToMarkdown`, ghi `FromMarkdown` (trước là `ToPlainText`/`FromPlainText` — làm MẤT heading/bullet/bold/code khi sync). `AdfConverter` mở rộng: heading `#`→`######`, inline `` `code` ``, code block ``` fenced — 2 chiều ADF ⇄ markdown. `Snippet` list vẫn plain text. FE `miniMarkdown` render heading/code chip/code block; toolbar `RichCommentBox` thêm nút Heading + Code, dùng luôn cho sửa MÔ TẢ.
- **Lưu ý vận hành:** description các item Ticket đã sync trước đó vẫn là plain text — cần re-sync (reset `Connections.CursorValue` của Jira hoặc chờ issue đổi trên Jira) để nhận bản markdown.
- **UX nháp Gmail:** click nháp mở panel chi tiết (view-only, nháp hiện trong hội thoại với badge "Thư nháp", KHÔNG tự bung ô Reply); nút "Tiếp tục chỉnh sửa" → `/send-email` load nội dung THẬT từ Gmail qua `getThread` (metadata local dạng sync không chứa body; fallback `bodyPlainText` cho nháp text thuần).
- **Xoá email = xoá CẢ thread** (đã vậy từ trước ở `ItemWriteBackService`) — message confirm sửa lại cho đúng ngữ nghĩa.
- **`window.confirm` → `ConfirmDialog`** toàn app (bulk delete, ticket, dọn Trash/Spam, hủy nháp ×3, gỡ quyền Drive, khoá user admin).

## [2026-07-09] Google Drive — tạo folder & chia sẻ (SCRUM-79)

- **Phạm vi:** `ServiceType=Drive` — tạo folder trên Google (`POST /api/drive/folders`) + chia sẻ permissions + link anyone-with-link qua `/api/drive/items/{id}/*`. Write-back synchronous; **không** bảng DB permissions; **không** ETag conflict (khác write-back Items).
- **Entry UI:** Integrations + toolbar Inbox/Kanban + ItemDetail (Chia sẻ mọi File Drive; Tạo folder con khi `isFolder`).
- **Sync metadata:** `DriveItemMapper` set `metadataJson.isFolder` + `parents` khi sync đọc Drive — hỗ trợ parent dropdown và nhận diện folder.
- **Không làm v1:** cascade share từng item con trong app; quyền folder con do **kế thừa Google Drive** (hành vi provider), không logic riêng WH.
- **Docs:** `docs/API.md`, `docs/SPRINTS.md`, spec `docs/DRIVE_FOLDER_SHARING.md`.

## [2026-07-08] Notifications in-app + SignalR hub retry (SCRUM-68)

- **Sync → notification:** Mọi sync (`ConnectionSyncDispatcher`) khi `Created > 0` gọi `SyncItemNotificationService` — tối đa 10 item/sync (ưu tiên `IsImportant` nếu vượt). Lưu DB + push SignalR `ReceiveNotification`.
- **FE:** Chuông + badge unread (poll 45s) + dropdown OData phân trang + mark read/read-all + toast realtime + deep link `/inbox?item=`.
- **Copy i18n:** `Title` = key (`notifications.newEmailFrom`, …); `Body` = JSON `{ from?, itemTitle, preview }` — FE dịch theo lang.
- **SignalR resilience:** `useNotificationHub` — retry start vô hạn + backoff; rebuild hub (CSRF header mới) mỗi lần retry; BE exempt `/hubs/*` khỏi CSRF negotiate. `withAutomaticReconnect` sau connect.

## [2026-07-08] Google Contacts autocomplete (SCRUM-69)

- **Sync read-only:** Mỗi lần sync Gmail (cron định kỳ SCRUM-72 ~60s, nút Đồng bộ, hoặc lazy khi mở list items) kéo `connections.list` + `otherContacts.list` (People API) vào `GoogleContacts` — full replace theo `ConnectionId`. Best-effort: lỗi contact không fail mail sync.
- **Scopes optional:** `contacts.readonly` + `contacts.other.readonly` request kèm Gmail connect; thiếu scope → sync/suggest rỗng, user vẫn nhập tay.
- **Suggest từ cache DB:** `GET /api/emails/contacts/suggest?connectionId=` + OData in-memory (`$filter/$top/$orderby`). Không gọi Google lúc gõ. Cả `Contact` và `OtherContact`.
- **FE:** `EmailChipsInput` debounce 300ms + dropdown; wire `SendEmail` + `ScheduledEmails`.
- **Ticket sau:** write-back + trang `/contacts` — **SCRUM-76** (spec local `docs/CONTACTS_WRITEBACK.md`).
## [2026-07-07] Cron sync connections + FE auto-refresh (SCRUM-72)

> **Mở rộng SCRUM-16:** bổ sung sync **định kỳ** ngoài on-demand; webhook/push realtime vẫn ngoài scope.

- **BE cron batch sync:** `ProcessConnectionsSyncService` quét mọi Connection Active + Integration enabled → sync qua `IConnectionSyncDispatcher` (đủ Gmail/GCal/Drive/Jira). Mỗi connection lỗi không chặn batch.
- **BackgroundService (prod + dev):** `Cron:SyncAutoRun` + `Cron:SyncIntervalSeconds` — tách key riêng với cron email (`AutoRun` / `IntervalSeconds`). **Prod:** `docker-compose.prod.yml` bật `Cron__SyncAutoRun=true` (60s), cùng pattern scheduled email — **không** cần cron-job.org cho sync. Endpoint HTTP vẫn có cho test/thay thế khi tắt `SyncAutoRun`.
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

## [2026-06-30 — kế hoạch] Đại tu Auth: HttpOnly cookie + refresh token (Redis) + OTP đăng ký (Twilio)

> ⚠️ **Phần OTP của entry này đã bị thay thế** — SCRUM-64 đổi từ SMS (Twilio) sang **Email (Resend)**, xem entry [2026-07-16] ở đầu file. 62 (cookie) + 63 (refresh/Redis) vẫn đúng như mô tả dưới.

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
