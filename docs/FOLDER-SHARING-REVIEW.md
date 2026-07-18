# System Folder Sharing — Bức tranh toàn cảnh & Review

> Tài liệu review tính năng **System Folder Sharing** (nhánh `feat/System-Folder-Sharing`) do Huy code phần nền tảng.
> Mục đích: để nhóm cùng verify hiện trạng, thống nhất ưu/nhược điểm và chốt kế hoạch hoàn thiện.
> Người review: Lộc · Ngày: 2026-07-18
>
> 👉 **Người mới tiếp quản:** đọc `docs/FOLDER-SHARING-HANDOVER.md` trước — bản tóm tắt bug đã fix,
> khuôn mẫu lỗi hay lặp, hạn chế đã chấp nhận và checklist test tay. File này là phần phân tích sâu.

---

## 0. TL;DR (đọc nhanh)

- **Hướng đi của Huy là ĐÚNG:** người B (được share) không có token Google/Jira, nên B thao tác item của A bằng cách **"mượn" connection của A qua tầng server** ("owner connection as proxy"). Đây gần như là lựa chọn duy nhất khả thi.
- **Token của A được giấu kín tốt** — không rò rỉ ra response, không cho mượn chéo connection.
- **Nhưng còn 6 điểm cần khắc phục**, trong đó **2 điểm là bug/lỗ hổng thật** (delete thread desync, Jira thao tác bị chặn cho Editor) và **2 điểm là nhược điểm mô hình nghiêm trọng** (disconnect xóa sạch item đang share, logout không thu hồi quyền).
- Migration DB rất nhỏ và an toàn (chỉ thêm 1 cột `FolderShares.CreatedAt`).

---

## 1. Những gì Huy đã làm — Hướng đi

### 1.1. Ý tưởng cốt lõi: "Owner connection as proxy"

Vấn đề nền tảng: item (email/event/file/ticket) thuộc về owner A và chỉ fetch/edit được bằng **token OAuth của A**. Người B được share **không có** token đó.

Giải pháp của Huy: **Item không bao giờ đổi chủ.** `Item.ConnectionId` luôn trỏ về connection của A. Khi B tác động lên item:

| Bước | Cơ chế |
|------|--------|
| 1 | Check ownership thường: `GetByIdAndUserAsync(itemId, B)` → B không sở hữu → `null` |
| 2 | Fallback share-check: `IsItemSharedWithUserAsEditorAsync` (ghi) / `IsItemSharedWithUserAsync` (đọc) — kiểm tra item có nằm trong folder được share (đã Accepted) cho B không |
| 3 | Nếu pass → nạp item bằng khóa hệ thống `GetByIdAsync(itemId)` (không kèm userId) |
| 4 | Dùng `item.ConnectionId` (của A) → gọi gateway Google/Jira **bằng token của A** |

→ **B không cần token riêng.** Ranh giới bảo mật nằm ở lớp share-check (bước 2), không nằm ở token.

### 1.2. Backend

- **Domain/DB:**
  - Enum `SharePermission` thêm giá trị `Editor` (trước chỉ có `Viewer`). Enum lưu **string** → không cần đổi cột DB.
  - Thêm cột `FolderShares.CreatedAt` (migration `AddFolderShareCreatedAt`, migration thứ 15).
  - Bảng `FolderShares` bản thân đã có từ `InitialCreate` (không phải mới).
- **DTO + Validator:** `InviteFolderShareRequest`, `UpdateFolderShareRequest`, `FolderShareDto`, `SharedFolderDto` + validator (role ∈ {Viewer, Editor}, email hợp lệ).
- **FolderService (sharing):** chỉ share được với **bạn bè đã Accepted**; chặn share cho chính mình; chỉ **Owner** mới invite/đổi role/revoke; tạo Notification cho người được mời.
- **Share-check được nhúng vào 3 service** để B (shared) đọc/ghi item của A:
  - `ItemWriteBackService` — `PatchItemAsync` / `DeleteItemAsync` (yêu cầu Editor).
  - `ItemService` — `UpdateStatusAsync` (Editor), `GetItemByIdAsync` (chỉ cần shared).
  - `SendEmailService` — đọc thread/attachment (`requireEditor:false`), reply/forward (`requireEditor:true`), gom trong helper `GetAndValidateConnectionAndItemAsync`.
- **Repository:** `FolderRepository` thêm các query share + 3 helper phân quyền: `IsItemSharedWithUserAsync`, `IsItemSharedWithUserAsEditorAsync`, `IsConnectionSharedWithUserAsEditorAsync`.
- **Backward-compat:** thêm constructor overload cho `ItemWriteBackService` (truyền `_folders = null!`) để 370 unit test cũ pass.

### 1.3. Frontend

- Types + `foldersApi` (axios) cho 8 endpoint share.
- `FolderShareDialog` (UI kiểu Google Drive): search bạn bè, đổi role Viewer/Editor, hiện Owner, remove.
- Sidebar tách "My Folders" / "Shared with Me" + context menu tương ứng (Owner: Sửa/Chia sẻ/Xóa; Shared: Rời thư mục).
- i18n VI/EN. Tách query key `['folders', {includeShared}]` để tránh xung đột cache + invalidate ở mọi tác vụ add/remove item.

### 1.4. API endpoints (8)

| # | Method | Route | Mô tả |
|---|--------|-------|-------|
| 1 | GET | `/api/folders/shared-with-me` | Danh sách folder được share |
| 2 | POST | `/api/folders/{id}/shares` | Mời share cho bạn bè |
| 3 | GET | `/api/folders/{id}/shares` | Danh sách người được share (Owner) |
| 4 | PATCH | `/api/folders/{id}/shares/{shareId}` | Đổi role Viewer/Editor (Owner) |
| 5 | DELETE | `/api/folders/{id}/shares/{shareId}` | Thu hồi share (Owner) |
| 6 | POST | `/api/folders/shares/{shareId}/accept` | Chấp nhận lời mời |
| 7 | POST | `/api/folders/shares/{shareId}/decline` | Từ chối lời mời |
| 8 | DELETE | `/api/folders/{id}/leave` | Người được share tự rời |

### 1.5. Thay đổi DB — chi tiết & ảnh hưởng

- **Chỉ 1 thay đổi schema:** thêm cột `CreatedAt` (`datetime2`, NOT NULL) vào `FolderShares`.
- **An toàn khi deploy:** `AddColumn` NOT NULL có default → không mất dữ liệu, không khóa bảng lâu. Prod tự chạy (`Db__AutoMigrate=true`).
- ⚠️ **Điểm bẩn dữ liệu:** default là `0001-01-01` (`DateTime.MinValue`), nên **row share cũ (nếu có)** sẽ hiển thị "SharedAt = năm 0001" và bị đẩy xuống đáy khi sort. Ở đồ án này gần như vô hại (tính năng mới, chưa có data cũ). Fix chuẩn: default `SYSUTCDATETIME()` ở tầng SQL — nhưng **không sửa migration đã commit**, nếu cần thì tạo migration mới UPDATE các row `CreatedAt = '0001-01-01'`.

---

## 2. Ưu / Nhược điểm cách làm của Huy

### 2.1. Ưu điểm

- ✅ **Hướng kiến trúc đúng.** "Owner connection as proxy" là cách khả thi duy nhất để B thao tác dữ liệu Google/Jira của A mà B không có (và không nên có) token.
- ✅ **Token của A được giấu kín ở tầng dữ liệu:**
  - Endpoint item **không nhận `connectionId` từ client** → B không thể chỉ định connection tùy ý để mượn token người khác.
  - Kể cả khi truyền `connectionId` (reply/forward), có check `item.ConnectionId != connectionId → throw` và `connection.UserId != item.UserId → throw`.
  - `AccessTokenEncrypted`/`RefreshTokenEncrypted` mã hóa bằng Data Protection, **không nằm trong `ItemResponse`** (DTO chỉ có `ConnectionId` là GUID vô hại).
- ✅ **Phân biệt Viewer/Editor rõ ràng** qua tham số `requireEditor` — đọc chỉ cần shared, ghi cần Editor.
- ✅ **Tôn trọng convention repo:** enum lưu string, migration nhỏ gọn, layered architecture, DTO tách entity.
- ✅ **Chặn đầu vào tốt ở tầng share:** chỉ share cho bạn bè Accepted, chỉ Owner quản lý share, chống share cho chính mình.

### 2.2. Nhược điểm

- ⚠️ **Ranh giới bảo mật rải rác, không phòng thủ theo tầng.** Logic "owner OR shared(editor?)" bị **lặp ở 4 nơi** với biến thể khác nhau. Nếu thêm endpoint mới mà quên gọi share-check thì token của A bị dùng vô điều kiện. Cần gom về **một cổng** (`IItemAccessResolver`).
- ⚠️ **Áp dụng KHÔNG nhất quán.** Cùng là "B là Editor thao tác item của A", nhưng:
  - Write-back Google + UpdateStatus + đọc email: **có** share-check → hoạt động.
  - **Jira comment/attachment (`JiraTicketService`): KHÔNG có share-check** → B là Editor bị **404** (xem mục 3, điểm E).
- ⚠️ **Constructor overload `null!` là code smell.** Service có thể ở trạng thái "không có folder repo" (mọi nhánh share phải guard `_folders != null`); nếu DI dùng nhầm constructor, tính năng share **im lặng biến mất** thay vì fail rõ.
- ⚠️ **Không có audit/log** khi B thao tác phá hủy (xóa/sửa) trên dữ liệu của A.
- ⚠️ **Phụ thuộc hoàn toàn vào sức khỏe connection của A** — kéo theo loạt nhược điểm ở mục 4.

---

## 3. Các điểm cần khắc phục (bug & thiết kế phân quyền)

> Ký hiệu: 🔴 nghiêm trọng · 🟠 trung bình · 🟡 nhẹ/UX

### 🔴 A. DELETE thread email bị desync (bug thật)

**Vị trí:** `ItemWriteBackService.DeleteItemAsync` → `ItemRepository.DeleteThreadAsync(userId, threadId)`.

**Vấn đề:** `DeleteThreadAsync` lọc `WHERE Item.UserId == userId`. Khi B (Editor) xóa, `userId = B`, nhưng các row thread thuộc `UserId = A` → **filter không match row nào → DB local KHÔNG xóa gì**. Trong khi đó bước phía trên đã gọi `TrashThreadAsync`/`DeleteThreadAsync` **trên Gmail của A rồi**.

**Hậu quả:** email đã bị trash/xóa trên Gmail của A, nhưng item vẫn còn trong DB → **desync**: B thấy item "ma", A mở app vẫn thấy item nhưng trên Gmail đã mất. Item trơ lại cho tới lần sync sau (nếu có).

**Ghi chú:** đây KHÔNG phải leo thang xóa dữ liệu của A (ban đầu lo lắng), mà là **xóa hụt local** → lệch trạng thái. Vẫn cần sửa.

### 🔴 B. Đọc rò rỉ qua "shared in ANY folder", không giới hạn theo folder cụ thể

**Vị trí:** `IsItemSharedWithUserAsync` / `IsItemSharedWithUserAsEditorAsync`.

**Vấn đề:** query chỉ hỏi *"item này có nằm trong **bất kỳ** folder nào share cho B không"*. Vì Item ↔ Folder là **many-to-many** (`ItemFolders`), một item của A có thể nằm đồng thời ở folder-X (private) và folder-Y (share cho B). Logic hiện tại **không kiểm tra item có thực sự nằm trong folder mà B đang thao tác**.

**Hậu quả hiện tại:** chưa lộ trực tiếp vì check là "tồn tại share chứa item". Nhưng nó **phá nguyên tắc least-privilege** và sẽ thành lỗ hổng khi có tính năng share/unshare per-folder tinh hơn. Nên siết: truyền `folderId` vào và verify `item ∈ folderId ∧ B có quyền trên folderId`.

### 🟠 C. Thiếu check `connection.Status` ở luồng write-back Google

**Vị trí:** `ItemWriteBackService.PatchItemAsync` / `DeleteItemAsync` (nhánh Email/Event/File).

**Vấn đề:** không kiểm tra `connection.Status == Active` trước khi gọi gateway (Jira thì có check). Nếu connection của A ở trạng thái `Error` (refresh token hỏng), B thao tác sẽ nhận lỗi 500/502 khó hiểu thay vì thông báo có ngữ nghĩa.

### 🟠 D. Reply/Forward gửi dưới danh nghĩa A (cần chủ đích hóa)

**Vị trí:** `SendEmailService` (reply/forward, `requireEditor:true`).

**Vấn đề:** B (Editor) reply → email gửi **từ Gmail của A** (`connection.ProviderAccountId = A`). Người nhận thấy mail đến từ A. Về nghiệp vụ có thể đúng ("B đại diện A xử lý"), nhưng đây là **cho phép B gửi mail nhân danh A** → cần là quyết định có chủ đích, ghi rõ, và owner phải ý thức được.

### 🟠 E. Jira comment/attachment chặn nhầm Editor (bug phân quyền)

**Vị trí:** `JiraTicketService.ResolveAsync`.

**Vấn đề:** chỉ dùng `GetByIdAndUserAsync(itemId, userId)` — **không có fallback share-check**. Nên B là Editor của folder chứa ticket sẽ bị **404** ở tất cả thao tác comment/attachment Jira, dù đáng lẽ có quyền. Không nhất quán với write-back item (vốn cho Editor).

### ✅ G. Code Calendar mới (merge từ develop) không biết gì về folder chia sẻ — ĐÃ FIX 2026-07-19

**Bối cảnh:** develop viết lại toàn bộ Google Calendar sau khi nhánh này tách ra. Các endpoint mới
(`calendar-details`, `rsvp`) và `EventDetailPopup` được viết theo giả định **"item luôn thuộc người đang gọi"**
— giả định này đúng trước khi có folder chia sẻ.

**Triệu chứng thực tế khi test:** người B mở event trong folder được chia sẻ → *"Could not load the details"*;
xoá event → `ProviderError` 502; gắn tag → *"Item with id '…' was not found"*.

**Đã sửa:**
1. `GetCalendarEventDetailAsync` — thêm share-check (Viewer đọc được).
2. `conn.UserId != userId` → `conn.UserId != item.UserId` ở cả `calendar-details` và `rsvp`
   (trong mô hình proxy, connection thuộc **owner của item**, không thuộc người gọi — so sánh cũ chặn mọi truy cập chia sẻ).
3. `RsvpEventAsync` — là ghi → yêu cầu Editor, Viewer nhận 403 rõ nghĩa.
4. `TagService.AssignAsync` — Viewer gắn được tag riêng (tag là nhãn private, không đụng provider của owner);
   kèm theo đó `MapToResponse` phải **lọc tag theo `Tag.UserId`** nếu không A sẽ thấy tag riêng của B.
5. `EventDetailPopup` — ẩn Sửa/Xoá/Email khách/"View on Google Calendar" khi `item.isOwner === false`.

**Bài học cho phần còn lại của đồ án:** mỗi lần merge code mới đụng tới Item, phải rà lại xem nó có
dùng `GetByIdAndUserAsync` mà thiếu nhánh share-check hay không. Đây là **lỗi hệ thống lặp lại**, không phải sự cố lẻ.

### 🟡 F. Constructor overload `null!` (code smell)

**Vị trí:** `ItemWriteBackService` (constructor 8 tham số gọi lại constructor 9 tham số với `_folders = null!`).

**Vấn đề:** để 370 test cũ pass. Tạo trạng thái service "không có folder repo". Nên cập nhật test inject mock `IFolderRepository` (trả `false`) rồi xóa overload.

---

## 4. Nhược điểm của mô hình "B mượn connection của A"

> Phần này trả lời trực tiếp: điều gì xảy ra khi connection của A **logout / disconnect / hết hạn**.

### 4.1. Khi A **logout** (đăng xuất, KHÔNG xóa tài khoản) → B KHÔNG bị ảnh hưởng

- Auth đăng nhập app (JWT cookie + refresh token Redis) **tách biệt hoàn toàn** với OAuth connection để sync.
- Logout chỉ hủy phiên app của A; token Google/Jira (`AccessTokenEncrypted`/`RefreshTokenEncrypted`) **vẫn nằm nguyên trong DB**.
- → **B vẫn đọc/sửa được item của A kể cả khi A đã logout.** `TokenService` tự refresh access token mới mà không cần A hiện diện.
- **Đánh giá:** đúng thiết kế (giống Google Drive: share xong logout người kia vẫn xem được), **KHÔNG phải bug**. Nhưng phải **ghi rõ**: *logout KHÔNG thu hồi quyền của B*. Chỉ Disconnect hoặc revoke ở phía Google mới thu hồi.

### 4.2. Khi A **disconnect** → 🔴 NHƯỢC ĐIỂM LỚN NHẤT (phá hủy dữ liệu của B)

- `ConnectionsService.DisconnectAsync` → `ItemRepository.DeleteByConnectionIdAsync` **XÓA CỨNG toàn bộ Item + ItemFolder + TagAssignment** của connection đó.
- **Hậu quả với sharing:** A disconnect Gmail → mọi email-item của A bị xóa → **các folder A đã share với B bỗng trống rỗng**, item B đang xem/thao tác **biến mất không cảnh báo**, cũng không notify B.
- Lưu ý: CLAUDE.md ghi "service layer set NULL khi disconnect" nhưng **code thực tế XÓA** chứ không set null. Sharing khiến tác dụng phụ này lan sang dữ liệu người khác đang phụ thuộc.

### 4.3. Khi token của A **hết hạn** → 🟡 an toàn nhưng UX của B kém

- Access token hết hạn (< 5 phút) → `TokenService` **tự refresh ngầm** bằng refresh token của A → **B không bị ảnh hưởng** (trường hợp tốt).
- Refresh token **bị thu hồi/hỏng** (A đổi mật khẩu Google, gỡ app ở Google Account, refresh token hết hạn) → `TokenResponseException` → `connection.Status = Error`.
  - **Không có lỗ hổng** (không mượn được token hỏng).
  - Nhưng B nhận lỗi khó hiểu (write-back Google không check `Status` — xem điểm C), và **B không tự sửa được** (chỉ A mới reconnect).

### 4.4. Bảng tổng hợp

| Kịch bản | Bảo mật | Trải nghiệm B | Cần xử lý |
|----------|---------|---------------|-----------|
| Giấu token của A | ✅ Kín | — | Gom share-check về 1 cổng |
| A **logout** | ✅ Không ảnh hưởng | B vẫn dùng bình thường | Ghi rõ: logout KHÔNG thu hồi quyền B |
| A **disconnect** | ✅ An toàn | 🔴 Item của B **biến mất im lặng** | Cảnh báo A + notify B; cân nhắc archive thay vì xóa |
| Token **hết hạn (tự refresh)** | ✅ | ✅ Ngầm, mượt | — |
| Token **hỏng/thu hồi** (`Status=Error`) | ✅ | 🟡 Lỗi khó hiểu, B không tự sửa được | Trả lỗi ngữ nghĩa + FE hiển thị rõ |

---

## 5. Plan hoàn thiện (chi tiết)

> Sắp theo độ ưu tiên. Mỗi hạng mục ghi rõ file đụng tới + tiêu chí done.

### 5.0. ⭐ Scope thực tế cho đồ án — ĐỌC TRƯỚC

> **Bối cảnh:** đây là đồ án môn học, không phải sản phẩm production. Mục tiêu = chạy được + demo + bảo vệ được.
> Một **hạn chế đã biết mà giải thích được khi defense** thường tốt hơn một feature "làm chuẩn business" nhưng dở dang.
> Vì vậy plan chi tiết bên dưới (P0→P2) **giữ nguyên làm tài liệu phân tích** (đúng và có giá trị), nhưng
> **khối lượng thực tế cần làm co lại như sau** — đừng ôm hết.
>
> **Đã verify toàn bộ claim với code trên nhánh:** A–F + 4.2 đều đúng. Riêng editor share-check **CÓ** kiểm
> `AcceptedAt != null` → **không có lỗ pending-invite** (yên tâm). Bug A **chỉ** ở nhánh xoá **thread email**;
> nhánh xoá đơn lẻ (Event/File/Ticket/1-message) dùng `_items.Remove(item)` đã đúng.

| Mục | Quyết định cho đồ án | Lý do |
|-----|----------------------|-------|
| **A — email ma** | ✅ **LÀM** (fix ~1 dòng) | Lộ ngay khi demo (xoá email còn item ma), fix cực rẻ. Đúng kiểu "đơn giản mà giá trị". |
| **E — Jira chặn Editor** | ⚙️ **LÀM NẾU** demo có share ticket Jira (~5 dòng). Không demo Jira-share thì ghi known-limitation. | Nhỏ, nhưng chỉ đáng khi thực sự trình diễn. |
| **4.2 — disconnect xoá item share** | 📝 **KHÔNG code** bản đầy đủ (archive+notify+confirm). Chỉ **ghi known-limitation**; cùng lắm thêm 1 dòng confirm cảnh báo nếu rảnh. | Xác suất A disconnect giữa demo ~0. Machinery archive/notify là "chuẩn thật" → phức tạp thừa. |
| **B — share-check any-folder** | 📝 **KHÔNG code** — ghi known-limitation | Không lộ khi dùng bình thường; siết folder-scope đụng cả FE → phức tạp thừa. |
| **C — thiếu check Status (Google)** | 📝 **KHÔNG code** (hoặc 1 dòng nếu tiện) | Chỉ ảnh hưởng thông báo lỗi khi connection A hỏng — hiếm trong demo. |
| **F — constructor `null!`** | 📝 **ĐỂ NGUYÊN** | Sửa phải đụng 370 test, zero giá trị demo. |
| **P1-1 resolver / P2 audit / siết folder-scope / ma trận test** | ❌ **BỎ** | Đây là "làm chuẩn như production" — ngoài tầm đồ án. |

> **Tổng việc thực tế: 1 dòng (A) + tuỳ chọn ~5 dòng (E).** Phần còn lại là viết ghi chú "hạn chế đã biết" để
> trình bày khi bảo vệ. Toàn bộ mục P0→P2 dưới đây đọc như **backlog lý tưởng**, không phải checklist bắt buộc.

### P0 — Bug/lỗ hổng phải sửa trước nghiệm thu

#### P0-1. Sửa desync khi DELETE thread email (điểm A)
- **File:** `ItemWriteBackService.DeleteItemAsync`, `ItemRepository.DeleteThreadAsync`.
- **Việc:** khi người xóa là shared-Editor (không phải owner), phải xóa local theo **owner của item** (`item.UserId`) chứ không theo `userId` của B. Truyền `item.UserId` vào `DeleteThreadAsync`, hoặc đổi chữ ký để nhận `ownerUserId` riêng.
- **Phạm vi (đã verify):** CHỈ nhánh xoá **thread email** (`_items.DeleteThreadAsync(userId, …)`) dính lỗi — sửa đúng dòng đó. Nhánh xoá đơn lẻ Event/File/Ticket/1-message dùng `_items.Remove(item)` trên đúng entity của owner → **đã đúng, đừng động vào**. Nhớ test cả đường **BulkActionBar** (xoá hàng loạt) vì nó lặp lại nhánh thread này.
- **Done khi:** B (Editor) xóa 1 email trong folder share → cả Gmail của A và item local **đều mất đồng bộ**, không còn item "ma".

#### P0-2. Bọc disconnect an toàn với item đang share (điểm 4.2)
- **File:** `ConnectionsService.DisconnectAsync` (+ có thể `FolderService`/`NotificationService`).
- **Việc (tối thiểu):**
  1. Trước khi xóa: đếm số item của connection đang nằm trong folder **có share Accepted**. Nếu > 0, trả về cảnh báo cho A (FE confirm: *"N mục đang được chia sẻ với người khác sẽ bị xóa"*).
  2. Gửi notification cho từng B bị ảnh hưởng: *"Folder '…' đã mất nội dung do chủ sở hữu ngắt kết nối."*
- **Việc (lý tưởng, nếu kịp):** với item đang được share, chuyển `IsArchived = true` thay vì hard-delete, để B không mất trắng đột ngột.
- **Done khi:** disconnect không còn khiến folder share "trống rỗng im lặng"; A được cảnh báo, B được thông báo.

#### P0-3. Cho Editor thao tác Jira comment/attachment (điểm E)
- **File:** `JiraTicketService.ResolveAsync`.
- **Việc:** thêm fallback share-check (Editor) giống `ItemWriteBackService` — nếu không owner thì kiểm tra `IsItemSharedWithUserAsEditorAsync` rồi `GetByIdAsync`.
- **Done khi:** B là Editor của folder chứa ticket comment/upload/xóa attachment được (không còn 404 nhầm).

### P1 — Củng cố phân quyền & tính nhất quán

#### P1-1. Gom authorization về một cổng `IItemAccessResolver`
- **File mới:** `Application/Services/ItemAccessResolver.cs` (+ interface).
- **Việc:** một method `ResolveAsync(itemId, userId, requiredLevel)` trả `(Item, Connection, AccessLevel)` hoặc ném `NotFound/Forbidden`. Thay thế logic lặp ở `ItemWriteBackService`, `ItemService`, `SendEmailService`, `JiraTicketService`.
- **Lợi ích:** hết lệch lạc giữa 4 nơi, dễ test, là chỗ duy nhất để cắm audit (P2-1) và check `Status` (P1-3).
- **Done khi:** cả 4 service gọi qua resolver; không còn nhánh `GetByIdAndUser → fallback share-check` viết tay rải rác.

#### P1-2. Siết share-check theo folder cụ thể (điểm B)
- **File:** `IFolderRepository` + `FolderRepository` (query nhận thêm `folderId`), các call site truyền `folderId` (từ context FE khi mở item trong folder).
- **Việc:** verify `item ∈ folderId ∧ B có quyền trên folderId` thay vì "any folder".
- **Done khi:** item nằm ở folder private của A (không share) không thể bị B đọc qua đường "chung item với 1 folder share khác".
- **Lưu ý:** cần FE truyền `folderId` theo ngữ cảnh — cân nhắc phạm vi, có thể để P2 nếu ảnh hưởng nhiều FE.

#### P1-3. Thêm check `connection.Status` cho write-back Google (điểm C)
- **File:** `ItemWriteBackService` (hoặc gom vào `ItemAccessResolver`).
- **Việc:** nếu `connection.Status != Active` → ném lỗi ngữ nghĩa rõ (vd `OWNER_CONNECTION_INACTIVE`, map 409/422).
- **Done khi:** B thao tác khi connection A hỏng → nhận lỗi rõ ràng, FE hiển thị *"Chủ sở hữu cần kết nối lại"*.

#### P1-4. Bỏ constructor overload `null!` (điểm F)
- **File:** `ItemWriteBackService` + các test dùng constructor cũ.
- **Việc:** cập nhật 370 test inject mock `IFolderRepository` (mock trả `false` cho các `IsItemShared*` → giữ hành vi cũ), rồi xóa overload.
- **Done khi:** service còn 1 constructor; `dotnet test` vẫn 370/370 pass.

### P2 — An toàn & trải nghiệm

#### P2-1. Audit + notification khi shared-user write-back phá hủy
- **Việc:** khi B (Editor) xóa/sửa item của A, ghi log audit và/hoặc gửi notification cho A.
- **Done khi:** A truy được "ai đã đổi/xóa item của tôi trong folder chia sẻ".

#### P2-2. Chủ đích hóa hành vi reply/forward nhân danh A (điểm D)
- **Việc:** quyết định của nhóm: có cho Editor gửi mail nhân danh A không? Nếu có → hiển thị rõ trên UI cho cả A và B ("Bạn đang gửi từ hộp thư của A"). Nếu không → chặn.
- **Done khi:** hành vi được ghi vào CHANGELOG + UI phản ánh đúng.

#### P2-3. Xử lý default `CreatedAt = 0001-01-01` (mục 1.5)
- **Việc:** nếu prod có row share cũ bị `0001-01-01` → tạo migration mới UPDATE về `SYSUTCDATETIME()` hoặc `CreatedAt` gần đúng. Đồ án chưa có data thật thì có thể bỏ qua, chỉ ghi chú.
- **Done khi:** không có row share hiển thị năm 0001; hoặc xác nhận không có data cũ nên skip.

### Cập nhật tài liệu (sau khi code xong — theo quy ước repo)
- `docs/SPRINTS.md` — trạng thái ticket Folder Sharing.
- `docs/API.md` — 8 endpoint share + mã lỗi mới (`OWNER_CONNECTION_INACTIVE`…).
- `docs/DATABASE.md` — cột `FolderShares.CreatedAt`, giá trị enum `Editor`.
- `docs/CHANGELOG.md` — quyết định: logout không thu hồi quyền B; hành vi disconnect; reply nhân danh A.

---

## 6. Câu hỏi cần nhóm chốt

> Đáp án đề xuất bên dưới đã theo **scope đồ án** (mục 5.0) — mặc định "đơn giản, đủ demo". Nhóm chỉ cần xác nhận.

1. **Disconnect:** cho phép hard-delete item đang share (chỉ cảnh báo) hay chuyển sang archive để giữ cho B?
   → **Chốt đề xuất: KHÔNG code, ghi known-limitation.** Xác suất A ngắt kết nối giữa demo ~0. Không xây archive+notify.
2. **Reply/Forward nhân danh A:** giữ (Editor đại diện A) hay chặn?
   → **Chốt đề xuất: GIỮ** (đúng mục đích Editor), chỉ ghi 1 câu vào CHANGELOG để defense giải thích. Không thêm UI.
3. **Siết share-check theo folder (P1-2):** làm ngay hay để sau nghiệm thu (vì đụng FE truyền `folderId`)?
   → **Chốt đề xuất: BỎ/để sau.** Không lộ khi dùng bình thường; siết vào đụng FE = phức tạp thừa.
4. **Phạm vi refactor `IItemAccessResolver` (P1-1):** làm trọn trước nghiệm thu hay chỉ vá 3 bug P0 trước, refactor sau?
   → **Chốt đề xuất: chỉ vá bug, BỎ refactor.** Logic lặp 4 nơi chấp nhận được ở đồ án.

---

## 7. Hướng dẫn code cho người làm (copy-paste) — CHỈ 2 việc

> Toàn bộ scope thực tế = **1 dòng (A)** + **tuỳ chọn ~10 dòng (E)**. Đã verify với code nhánh, chép vào là chạy.
> Sau khi sửa: `cd backend && dotnet build && dotnet test` phải xanh.

### 7.1. ✅ BẮT BUỘC — Fix email "ma" (điểm A) · 1 dòng

**File:** `backend/src/WorkspaceHub.Application/Services/ItemWriteBackService.cs`
Trong `DeleteItemAsync`, nhánh xoá thread email (khoảng dòng 444). Đổi `userId` → `item.UserId`:

```csharp
// TRƯỚC (sai — userId là của B, không khớp item của owner A → không xoá local):
await _items.DeleteThreadAsync(userId, item.ThreadId, ct);

// SAU (đúng — xoá local theo owner của item):
await _items.DeleteThreadAsync(item.UserId, item.ThreadId, ct);
```

**Chỉ đúng 1 dòng đó.** Đừng đụng các nhánh khác (Event/File/Ticket dùng `_items.Remove(item)` đã đúng).
**Test tay:** B (Editor) xoá 1 email trong folder share → mail mất trên Gmail của A **và** item biến mất khỏi list (không còn "ma"). Thử cả xoá hàng loạt ở BulkActionBar.

### 7.2. ⚙️ TUỲ CHỌN — Cho Editor thao tác Jira (điểm E) · ~10 dòng · CHỈ làm nếu demo có share ticket Jira

**File:** `backend/src/WorkspaceHub.Application/Services/JiraTicketService.cs`
DI **không cần đổi** (`IFolderRepository` đã đăng ký, constructor tự inject). `using` đã sẵn có.

**Bước 1** — thêm field + tham số constructor:

```csharp
    private readonly IItemRepository _items;
    private readonly IConnectionRepository _connections;
    private readonly IJiraGateway _gateway;
    private readonly IFolderRepository _folders;                 // ⬅ THÊM

    public JiraTicketService(IItemRepository items, IConnectionRepository connections,
        IJiraGateway gateway, IFolderRepository folders)         // ⬅ THÊM tham số folders
    {
        _items = items;
        _connections = connections;
        _gateway = gateway;
        _folders = folders;                                      // ⬅ THÊM
    }
```

**Bước 2** — trong `ResolveAsync`, đổi dòng lấy item đầu tiên thành có fallback share-check (Editor):

```csharp
        // TRƯỚC:
        var item = await _items.GetByIdAndUserAsync(itemId, userId, ct)
            ?? throw new NotFoundException("Item", itemId);

        // SAU (owner OR shared-Editor — mirror ItemWriteBackService):
        var item = await _items.GetByIdAndUserAsync(itemId, userId, ct);
        if (item == null && await _folders.IsItemSharedWithUserAsEditorAsync(itemId, userId, ct))
            item = await _items.GetByIdAsync(itemId, ct);
        if (item == null) throw new NotFoundException("Item", itemId);
```

Giữ nguyên các check còn lại (Type/ExternalId/ConnectionId/ServiceType/Status).
**Test tay:** B (Editor) của folder chứa ticket → comment / đính kèm file được (trước đây 404).
**Lưu ý:** nếu có test khởi tạo `new JiraTicketService(...)` trực tiếp thì thêm mock `IFolderRepository` (trả `false`). Hiện chưa có `JiraTicketServiceTests` nên nhiều khả năng không cần đụng test.

### 7.3. 📝 Còn lại — KHÔNG code, chỉ ghi vào CHANGELOG (để bảo vệ)

Thêm 1 đoạn ngắn "Folder Sharing — hạn chế đã biết" vào `docs/CHANGELOG.md`:
- Logout KHÔNG thu hồi quyền của B (đúng thiết kế, giống Google Drive).
- Disconnect xoá item → folder share có thể trống (chưa chặn — hạn chế đã biết).
- Reply/Forward: Editor gửi mail từ hộp thư của owner A (có chủ đích).
- Share-check ở mức "folder bất kỳ có chứa item", chưa siết theo folder cụ thể (đủ dùng phạm vi đồ án).
