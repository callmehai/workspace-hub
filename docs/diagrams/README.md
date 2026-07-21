# Diagrams — Workspace Hub

Sơ đồ vẽ bằng **draw.io** (`.drawio` = nguồn, `.drawio.png` = ảnh có nhúng XML → mở lại bằng draw.io vẫn sửa được, `.svg` = vector cho báo cáo/slide).
Nội dung lấy từ code thật trên nhánh `develop`, không phải thiết kế trên giấy.

## System Architecture (§2.1 của SDS)

| File | Dùng cho |
|---|---|
| `backend-architecture.drawio` | §2.1.1 BE architecture |
| `frontend-architecture.drawio` | §2.1.2 FE architecture |

Bản Mermaid tương đương (render trên GitHub): `docs/ARCHITECTURE.md`.

## Database Design (§3.1 của SDS)

| File | Dùng cho |
|---|---|
| `erd.drawio` | §3.1 ERD — **ký hiệu Chen**: 15 thực thể + 20 hình thoi quan hệ + 1/M, không vẽ thuộc tính (bản chèn vào báo cáo) |
| `erd-full-columns.drawio` | Bản phụ: đủ cột / kiểu dữ liệu / PK-FK / cascade (17 bảng, 26 khoá ngoại, crow's foot) — để tra cứu |

Cả hai đều **sinh từ schema thật**, không vẽ tay:

```bash
# ERD Chen (bản chính)
python3 docs/diagrams/detailed-design/src/chen.py        # → chen-graph.json
python3 ~/.claude/skills/drawio-skill/scripts/autolayout.py chen-graph.json -o erd.drawio --tune
python3 docs/diagrams/detailed-design/src/chennote.py erd.drawio   # thêm tiêu đề + chú thích
```

Bản đủ cột:

```bash
cd backend && dotnet ef dbcontext script -p src/WorkspaceHub.Infrastructure -s src/WorkspaceHub.Api -o model.sql
python3 docs/diagrams/detailed-design/src/erdspec.py    # model.sql  → erd.json (spec + vị trí)
python3 docs/diagrams/detailed-design/src/erdgraph.py   # erd.json   → erd-graph.json
python3 ~/.claude/skills/drawio-skill/scripts/autolayout.py erd-graph.json -o erd.drawio --tune
python3 docs/diagrams/detailed-design/src/addnote.py erd.drawio      # thêm tiêu đề + chú thích
```

Lưu ý: dùng `dotnet ef dbcontext script` (schema hiện tại) chứ **không** dùng `dotnet ef migrations script` — bản migrations là tích luỹ nên còn cả bảng đã bỏ (`OAuthConnections`, `ServiceConnections`) và thiếu cột thêm sau bằng `ALTER TABLE`.

## Detailed Design (§4 của SDS) — `detailed-design/`

Mỗi nhóm use case: **1 class diagram** (`4.x.1`) + **nhiều sequence diagram, mỗi use case một cái** (`4.x.2.n`).
Mỗi tên file có 3 đuôi: `.drawio` (nguồn), `.drawio.png` (nhúng XML, mở lại sửa được), `.svg`.

**4.1 — Connection & OAuth**

| Loại | File |
|---|---|
| Class | `4.1.1-connection-oauth-class` |
| UC Connect service | `4.1.2.1-connect-service-sequence` |
| UC Reconnect service | `4.1.2.2-reconnect-service-sequence` |
| UC Resync (đồng bộ thủ công) | `4.1.2.3-resync-connection-sequence` |
| UC Disconnect service | `4.1.2.4-disconnect-service-sequence` |

**4.2 — Item & Unified Workspace**

| Loại | File |
|---|---|
| Class | `4.2.1-item-workspace-class` |
| UC Xem danh sách item (lazy sync) | `4.2.2.1-view-items-lazy-sync-sequence` |
| UC Ghi ngược (write-back + conflict 409) | `4.2.2.2-writeback-item-sequence` |
| UC Tạo item (Event/Ticket/Note) | `4.2.2.3-create-item-sequence` |
| UC Xoá item | `4.2.2.4-delete-item-sequence` |

**4.3 — Folder, Kanban & Tag**

| Loại | File |
|---|---|
| Class | `4.3.1-folder-kanban-tag-class` |
| UC Tạo Folder | `4.3.2.1-create-folder-sequence` |
| UC Kéo item vào Folder | `4.3.2.2-add-item-to-folder-sequence` |
| UC Đổi trạng thái Kanban | `4.3.2.3-change-kanban-status-sequence` |
| UC Quản lý Tag (tạo & gán) | `4.3.2.4-manage-tag-sequence` |

**4.4 — Email Workspace**

| Loại | File |
|---|---|
| Class | `4.4.1-email-workspace-class` |
| UC Gửi email ngay | `4.4.2.1-send-email-sequence` |
| UC Trả lời / Chuyển tiếp | `4.4.2.2-reply-forward-email-sequence` |
| UC Hẹn giờ gửi | `4.4.2.3-schedule-email-sequence` |
| UC Cron gửi email đến hạn | `4.4.2.4-cron-send-scheduled-sequence` |

**4.5 — Folder Sharing**

| Loại | File |
|---|---|
| Class | `4.5.1-folder-sharing-class` |
| UC Mời chia sẻ Folder | `4.5.2.1-invite-share-sequence` |
| UC Chấp nhận / từ chối lời mời | `4.5.2.2-accept-share-sequence` |
| UC Viewer đọc item (kể cả file Drive con) | `4.5.2.3-viewer-read-drive-child-sequence` |

**4.6 — Important Contact & Notification**

| Loại | File |
|---|---|
| Class | `4.6.1-contact-notification-class` |
| UC Đánh dấu liên hệ quan trọng | `4.6.2.1-mark-important-contact-sequence` |
| UC Sinh thông báo & đẩy realtime | `4.6.2.2-sync-notification-realtime-sequence` |
| UC Đánh dấu đã đọc | `4.6.2.3-mark-read-notification-sequence` |

**4.7 — Admin**

| Loại | File |
|---|---|
| Class | `4.7.1-admin-class` |
| UC Xem dashboard thống kê | `4.7.2.1-admin-dashboard-sequence` |
| UC Khoá / mở khoá user | `4.7.2.2-admin-lock-user-sequence` |
| UC Bật / tắt integration | `4.7.2.3-admin-toggle-integration-sequence` |

Tổng: **7 class + 25 sequence** diagram.

### Vài điểm sơ đồ ghi lại (khác với suy đoán thông thường)

- **4.1** Reconnect = cập nhật token trên row Connection cũ, **giữ nguyên `CursorValue`** (không 409). Sync lần đầu **không** chạy trong callback — chạy on-demand hoặc khi bấm resync.
- **4.2** Write-back Email **bỏ qua** kiểm ETag (Gmail ETag/HistoryId đổi liên tục → false-positive 409); Jira dùng `fields.updated` làm version-token.
- **4.3** Kanban đổi trạng thái qua `PATCH /api/items/{id}/status`, **không** đụng `ItemFolder.Position`.
- **4.4** Attachment của scheduled email lưu **base64 trong DB** (`ScheduledEmails.AttachmentsJson`), không lên R2. Gửi lỗi → `Failed`, mà cron chỉ quét `Pending` ⇒ **không tự retry**.
- **4.5** Sau khi share-check pass, connection gọi Google phải thuộc **chủ item**: `conn.UserId != item.UserId` (không phải `!= userId`) — xem `docs/FOLDER-SHARING-HANDOVER.md`.
- **4.6** SignalR chỉ đẩy 1 chiều (thông báo mới); mark-read không push ngược.
- **4.7** **Chưa có** endpoint admin force-disconnect; disconnect chỉ self-service. `Users.LockedReason` có trong entity nhưng chưa được dùng.

## Sinh lại sơ đồ

Nguồn JSON + script nằm ở `detailed-design/src/`:

```bash
cd docs/diagrams/detailed-design
# class diagram
python3 src/classgen.py src/4.1.1-class.json -o 4.1.1-connection-oauth-class.drawio

# sequence diagram — mỗi use case 1 JSON, tất cả định nghĩa trong src/uc_seq.py
python3 src/uc_seq.py                       # sinh 25 file 4.x.2.n-*.json trong thư mục hiện tại
python3 ~/.claude/skills/drawio-skill/scripts/seqlayout.py 4.1.2.1-connect-service-sequence.json \
        -o 4.1.2.1-connect-service-sequence.drawio

# export (png nhúng XML + svg)
drawio -x -f png -e -s 2 -o 4.1.1-connection-oauth-class.drawio.png 4.1.1-connection-oauth-class.drawio
python3 ~/.claude/skills/drawio-skill/scripts/repair_png.py 4.1.1-connection-oauth-class.drawio.png
```

Thêm/sửa một use case: sửa dict `S` trong `src/uc_seq.py` rồi chạy lại 3 lệnh cuối cho file tương ứng.
