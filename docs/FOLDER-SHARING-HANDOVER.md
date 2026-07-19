# Folder Sharing — Handover cho người tiếp quản

> **Đọc file này trước** khi động vào tính năng chia sẻ thư mục. Nhánh: `feat/System-Folder-Sharing` (PR #118).
> Phân tích thiết kế đầy đủ + hạn chế đã chấp nhận: `docs/FOLDER-SHARING-REVIEW.md`.
> Lịch sử quyết định: `docs/CHANGELOG.md` (các entry 2026-07-17 → 2026-07-19).

---

## 1. Mô hình — hiểu cái này trước, mọi bug đều bắt nguồn từ đây

**"Owner connection as proxy".**

Người B (được chia sẻ) **KHÔNG có token Google/Jira**. Item luôn giữ `ConnectionId` của **owner A**.
B thao tác được là nhờ: server kiểm tra share-check → nếu pass thì dùng **token của A** để gọi provider.

```
B gọi API  →  share-check (IsItemSharedWithUserAsync / ...AsEditorAsync)
           →  lấy Connection theo item.ConnectionId  (= connection của A)
           →  gọi Google/Jira bằng token của A
```

**Ranh giới bảo mật nằm ở lớp share-check, không nằm ở token.** Token của A không bao giờ ra khỏi server
(`ItemResponse` chỉ có `ConnectionId` — GUID vô hại), và endpoint item không nhận `connectionId` từ client
nên không mượn chéo connection được.

### Hệ quả trực tiếp: hai lỗi lặp đi lặp lại

Đây là **hai khuôn mẫu bug** đã xuất hiện nhiều lần trên nhánh này. Khi viết code mới đụng tới Item, kiểm tra ngay:

| Khuôn mẫu sai | Vì sao sai | Viết đúng |
|---|---|---|
| `GetByIdAndUserAsync(itemId, userId)` rồi `?? throw NotFound` | Item thuộc A, `userId` là B → không khớp → 404 dù B có quyền | Thêm nhánh share-check trước khi throw |
| `if (conn.UserId != userId) throw Forbidden` | Connection thuộc **owner của item**, không thuộc người gọi | `if (conn.UserId != item.UserId)` |

> Khuôn mẫu thứ hai đặc biệt dễ lọt vì nó **trông rất giống một check bảo mật đúng đắn**.
> Nó an toàn khi và chỉ khi share-check đã chạy trước đó.

### Phân biệt ĐỌC / GHI khi thêm endpoint mới

| Loại thao tác | Ai được phép | Helper |
|---|---|---|
| ĐỌC (xem chi tiết, list) | owner + **Viewer** + Editor | `IsItemSharedWithUserAsync` |
| GHI lên provider (sửa/xoá/RSVP/comment) | owner + **Editor** | `IsItemSharedWithUserAsEditorAsync` |
| Dữ liệu **local private** (tag) | owner + **Viewer** + Editor | `IsItemSharedWithUserAsync` — xem §3 |

Viewer cố ghi → gọi `ThrowNoWriteAccessAsync(itemId, userId, ct)` (trong `ItemService`), nó tự phân biệt:
- item được share nhưng chỉ Viewer → **403** kèm message tiếng Việt dễ hiểu
- ngược lại → **404** như cũ

---

## 2. Bug đã fix — tra cứu nhanh

Nhóm theo nguyên nhân gốc, không theo thứ tự thời gian.

### 2.1. Thiếu share-check (khuôn mẫu 1)

| # | Triệu chứng | Vị trí | Cách sửa |
|---|---|---|---|
| 1 | Editor xoá email → Gmail đã trash nhưng item còn lại thành **"email ma"** | `ItemWriteBackService.DeleteItemAsync` | `DeleteThreadAsync(item.UserId, …)` thay vì `userId` — xoá local theo **owner**, không theo người thao tác |
| 2 | Editor **đánh dấu quan trọng** → 404 (dù xoá/đổi trạng thái thì được) | `ItemService.ToggleImportantAsync` | Thêm nhánh Editor fallback |
| 3 | Editor thao tác **Jira** (comment/attachment) → 404 | `JiraTicketService` | Thêm `IFolderRepository` + Editor fallback + Viewer 403 |
| 4 | Editor **reply email** trong folder chia sẻ → lỗi | `SendEmailService` | `CanAccessDraftAsync` — Editor truy cập draft qua email gốc cùng thread |
| 5 | Người B mở **event** → *"Could not load the details"* | `ItemService.GetCalendarEventDetailAsync` | Thêm share-check (Viewer đọc được) |
| 6 | Người B gắn **tag** → *"Item with id '…' was not found"* | `TagService.AssignAsync` | Thêm `IFolderRepository` + share-check |

### 2.2. So sánh `conn.UserId` sai (khuôn mẫu 2)

| # | Triệu chứng | Vị trí |
|---|---|---|
| 7 | Mọi truy cập chia sẻ tới calendar bị 403 | `GetCalendarEventDetailAsync` + `RsvpEventAsync` — đổi sang `conn.UserId != item.UserId` |

### 2.3. Rò rỉ dữ liệu

| # | Vấn đề | Vị trí |
|---|---|---|
| 8 | **IDOR attachment**: B truyền `messageId` của message KHÁC trong mailbox owner để tải trộm | `SendEmailService.GetAttachmentAsync` — verify messageId thuộc thread của item |
| 9 | **Draft ownership**: `SaveDraft` tạo item `UserId = caller(B)` → cửa hậu bỏ share-check, sống sót qua revoke | Đổi `UserId = connection.UserId` (owner) |
| 10 | **Google Contacts**: gợi ý danh bạ mức-connection cho Editor lộ toàn bộ address book của owner | Giới hạn owner-only |
| 11 | **Tag lẫn giữa các user**: item chia sẻ mang tag của nhiều người → A nhìn thấy tag riêng của B | `ItemService.MapToResponse` — lọc `Tag.UserId == currentUserId` |

> ⚠️ **#11 là hệ quả trực tiếp của #6.** Cho phép B gắn tag mà quên lọc khi đọc = tạo ra rò rỉ mới.
> Bài học: mở quyền ghi ở đâu, rà lại đường đọc tương ứng ở đó.

### 2.4. Không liên quan chia sẻ (phát hiện khi test)

| # | Triệu chứng | Nguyên nhân thật |
|---|---|---|
| 12 | Mark important trên **Jira/Drive ticket** bị mất sau re-sync | Sync ghi đè `IsImportant` — nhưng mapper luôn trả `false` vì provider không có khái niệm này. Đây là field **thuần local** → không đụng tới khi sync.<br>**Gmail cố ý KHÔNG fix** — `IMPORTANT` của Gmail là một label, để độc lập với mark-important của app |
| 13 | Reply/Send mail có nút đính kèm nhưng **file không được gửi** | Attachment bị rơi ở màn Send + Schedule |
| 14 | Xoá event → **`ProviderError` 502** | Google Calendar trả **410 Gone** (KHÔNG phải 404) khi event đã bị xoá. 410 rơi vào nhánh `ProviderException`.<br>→ `GoogleApiExceptionHandler` map **404 VÀ 410** → `NotFoundException` |
| 15 | Thư mục **Drive mở ra trống** trong workspace folder | Filter `folderId` và `driveParentId` **AND** với nhau, nhưng user chỉ gán *thư mục* Drive vào workspace folder — file con không được gán → giao rỗng.<br>→ Khi duyệt vào trong một thư mục Drive, bỏ qua filter workspace-folder |
| 16 | `ConnectionHealthChecker` timeout 5s → initial sync bị cancel, `LastSyncedAt` null → **retry vô hạn** | Nới lên 15s |

### 2.5. UI/UX

| # | Nội dung |
|---|---|
| 17 | Dropdown native → component `Select` tự style; **portal + auto flip-up** để không bị dialog cắt (`overflow` cắt theo hình học, z-index không cứu được) |
| 18 | `FriendMultiSelect` mới: tìm kiếm bỏ dấu tiếng Việt (gõ "hai" khớp "Hải"), checkbox chọn nhiều, nút chọn nhanh toàn bộ bạn thân |
| 19 | i18n toàn bộ UI chia sẻ (31 key `share.*` + 7 key `sidebar.*`, vi + en) |
| 20 | **Ẩn nút chỉ owner dùng được** khi `item.isOwner === false`: mở trong Gmail/Drive/Calendar/Jira, chia sẻ file Drive, và ở `EventDetailPopup` thì ẩn Sửa/Xoá/Email khách/"View on Google Calendar". Copy-link dùng link in-app thay vì `htmlLink` của owner |
| 21 | Lỗi 403 không còn **đá người dùng sang màn Integrations** — chỉ hiện toast |
| 22 | Bỏ **auto-save draft 2 giây** ở màn reply (gây nhiễu khi đang gõ) |

---

## 3. Quyết định thiết kế cần biết

**Tag là nhãn PRIVATE per-user.** Gắn tag không đụng dữ liệu provider của owner → **Viewer cũng gắn được**
tag riêng lên item của người khác. Mỗi người chỉ thấy tag của chính mình trên item đó. Đây là lý do
`TagService.AssignAsync` dùng `IsItemSharedWithUserAsync` (Viewer) chứ không phải bản `AsEditor`.

**Contract lỗi.** Viewer cố ghi → **403** + message tiếng Việt, KHÔNG phải 404 lộ GUID.

**`ItemResponse.IsOwner`** = `item.UserId == currentUserId`. FE dùng để ẩn hành động chỉ owner làm được.
Optional trailing param, mặc định `true` — item cũ/cache không có field này vẫn hiển thị như owner.

---

## 4. Hạn chế ĐÃ CHẤP NHẬN — không phải bug, đừng "sửa" nhầm

Nhóm đã chốt để nguyên và **ghi lại để giải thích khi bảo vệ**. Chi tiết: `FOLDER-SHARING-REVIEW.md` §3, §4.

| Hạn chế | Nội dung |
|---|---|
| **Editor = toàn quyền ghi** | Editor thao tác trên tài khoản provider của owner như chính owner. Hệ quả tất yếu của mô hình proxy |
| **Share-check theo "bất kỳ folder nào"** | `IsItemSharedWithUser*` chỉ hỏi *"item có nằm trong bất kỳ folder nào share cho B không"*, không kiểm tra item có thực sự thuộc folder B đang thao tác. Item ↔ Folder là many-to-many nên có kẽ hở lý thuyết (điểm B trong review) |
| **A logout → B không bị ảnh hưởng** | Đúng thiết kế: logout ≠ thu hồi quyền. Token của A vẫn nằm trong DB |
| **A disconnect → xoá cứng item đã chia sẻ** | Nhược điểm lớn nhất của mô hình (điểm 4.2). Dữ liệu của B biến mất |
| **Thiếu check `connection.Status`** ở write-back Google | Điểm C trong review |
| **Constructor overload `_folders = null!`** | Điểm F. Nợ kỹ thuật — **đã một lần làm vỡ build khi merge** (develop thêm tham số thứ 9 → CS1503 hàng loạt). Gỡ hẳn phải sửa ~6 file test |

---

## 5. Bẫy khi làm tiếp

1. **Merge develop về là phải rà lại share-check.** Đã xảy ra thật: develop viết lại toàn bộ Google Calendar,
   code mới viết theo giả định "item luôn thuộc người gọi" (đúng ở thời điểm nó được viết) → **4 bug cùng lúc**
   (#5, #6, #7, #14). Đây là **lỗi hệ thống lặp lại**, không phải sự cố lẻ.
   Cách rà: `grep GetByIdAndUserAsync` và `grep "conn.UserId"` trong code mới.

2. **`dotnet build` và `npm run build` KHÔNG đủ.** CI chạy cả `npm run lint`, và lint từng chặn merge
   với 6 lỗi trong khi build xanh. Chạy đủ **bốn** lệnh trước khi push:
   ```
   dotnet build && dotnet test        # backend
   npm run lint && npm run build      # frontend
   ```

3. **Đừng dùng `Set-Content -Encoding utf8` trên PowerShell 5.1** để sửa file có tiếng Việt — nó đọc UTF-8
   bằng ANSI rồi ghi lại kèm BOM → hỏng toàn bộ comment. Đã xảy ra một lần với `IFolderRepository.cs`.
   Dùng `[System.IO.File]::WriteAllText($f, $txt, [System.Text.UTF8Encoding]::new($false))`.

4. **API dev server khoá DLL** khi build (`MSB3027 … file is locked by WorkspaceHub.Api`).
   Tắt server trước khi `dotnet build`, đây không phải lỗi code.

---

## 6. CHƯA làm — cần test tay

Unit test (436 pass) **không phủ được full stack**. Những luồng sau mới chỉ verify bằng đọc code:

- [ ] **B là Editor:** mark important · xoá email (kiểm tra không còn "email ma", thử cả `BulkActionBar`) · comment/đính kèm Jira · reply email trong folder chia sẻ
- [ ] **B là Viewer:** xoá / đổi trạng thái / mark important / comment Jira → toast rõ nghĩa, **không** bị đá sang Integrations
- [ ] **Mở item của A:** không thấy nút "Open in Gmail/Drive/Calendar"
- [ ] **Event trong folder chia sẻ** — luồng Calendar đã bị develop viết lại hoàn toàn, chưa test lại lần nào sau merge
- [ ] **Mark important ticket Jira** → re-sync → cờ còn nguyên
- [ ] **Reply/Send có đính kèm** → người nhận nhận được file (kiểm tra cả màn Scheduled)
- [ ] **Thư mục Drive trong workspace folder** hiện đúng file con (#15 — mới chỉ suy luận từ code, **chưa chạy thực tế**)
- [ ] **Dropdown quyền ở dòng share cuối** → bung lên trên, không bị cắt

Ngoài ra: develop vừa thêm **multi-account** (nhiều tài khoản Gmail/Drive mỗi service) — các luồng chia sẻ
chưa được thử với cấu hình này.

---

## 7. File hay phải đụng

| File | Vai trò |
|---|---|
| `Application/Services/ItemService.cs` | `ThrowNoWriteAccessAsync`, `MapToResponse` (+`IsOwner`, lọc tag), calendar-details, rsvp |
| `Application/Services/ItemWriteBackService.cs` | Write-back + xoá; còn overload `_folders = null!` |
| `Application/Services/SendEmailService.cs` | Draft/reply/attachment + `CanAccessDraftAsync` |
| `Application/Services/TagService.cs` | Gắn/gỡ tag (Viewer được phép) |
| `Application/Services/JiraTicketService.cs` | Comment/attachment Jira |
| `Infrastructure/Repositories/FolderRepository.cs` | `IsItemSharedWithUserAsync` / `...AsEditorAsync` — **trái tim của phân quyền** |
| `Infrastructure/Repositories/ItemRepository.cs` | `GetPagedAsync` — filter folder/Drive |
| `Infrastructure/Services/GoogleApiExceptionHandler.cs` | Map lỗi Google → domain exception (404/410 → NotFound) |
| `frontend/src/components/folders/FolderShareDialog.tsx` | UI chia sẻ |
| `frontend/src/components/calendar/EventDetailPopup.tsx` | Popup event — gating theo `isOwner` |
| `frontend/src/components/Select.tsx` | Dropdown portal (dùng ở 9 chỗ) |
