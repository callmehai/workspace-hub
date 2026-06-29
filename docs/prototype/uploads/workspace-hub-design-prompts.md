# Workspace Hub — Prompts cho Claude Design

> Dùng kèm `workspace-hub-design-brief.md`. Thứ tự: nạp brief → chạy Master prompt → refine từng màn.

---

## Cách dùng trong Claude Design

1. Mở **claude.ai/design**, chọn template **Prototype** (không phải Slides/Document).
2. Nạp context: dán toàn bộ nội dung `workspace-hub-design-brief.md` vào ô brief, **hoặc** upload file đó qua nút `+`. (Tùy chọn: tạo một Design system để giữ token màu/typography đồng bộ.)
3. Model để **Claude Opus 4.8**.
4. Chạy **Master prompt** bên dưới để có khung + vài màn chính.
5. Sau đó dùng **prompt từng màn** để dựng/đánh bóng lần lượt. Refine bằng câu ngắn ("làm card connection gọn hơn", "thêm empty state cho list này").
6. Xong thì export (HTML/CSS hoặc ảnh từng màn) để chuyển sang code FE — xem mục cuối.

Mẹo: đừng ép pixel-perfect. Wireframe rõ ràng + đúng cấu trúc component là đủ để code; polish (responsive/dark mode) làm ở tầng code sau.

---

## Master prompt (chạy đầu tiên)

```
Dựng prototype clickable cho web app "Workspace Hub" theo design brief đã nạp. App gom Gmail/Calendar/Drive về một nơi, quản lý theo Folder + Kanban 3 cột, đồng bộ 2 chiều.

Yêu cầu khung:
- App shell dùng chung: sidebar trái (logo, nav: Inbox/Kanban/Connections/Scheduled/Admin, mục Folders có chấm màu, avatar+logout ở đáy) + topbar (search, filter chips, nút Connect service indigo, chuông, avatar).
- Design system: primary indigo #4f46e5, nền slate-50, card trắng radius 12px, input/button radius 8px, viền slate-200, font Inter, body 14px. Màu theo Item type: Email=blue, Event=amber, File=emerald, Note=slate, Ticket=violet. UI tiếng Việt, light mode.
- Sidebar nav phải CHUYỂN MÀN được (clickable).

Dựng trước 3 màn chính, nối với nhau:
1) Inbox/Items: filter bar (folder/status/type/important) + search + list Item row (icon-type, tiêu đề, snippet, meta, badge status, sao Important) + pagination.
2) Kanban 3 cột "Cần xem / Đang xử lý / Done", card kéo-thả, đếm số mỗi cột.
3) Connections: grid card mỗi service (Gmail/Calendar/Drive/Jira) với badge status + nút Connect/Disconnect/Refresh.

Mỗi list có sẵn loading skeleton + empty state. Dùng mock data thực tế (email công việc, sự kiện, file, note).
```

---

## Prompt từng màn (chạy sau master, mỗi lần một màn)

### Auth (Login/Register)
```
Thêm màn Auth riêng (ngoài app shell): card giữa nền slate-50. Login = email + password + nút "Đăng nhập" indigo + "hoặc" + nút "Đăng nhập bằng Google" (viền, logo G). Link sang Register (thêm Full name + confirm password). Validation inline (email hợp lệ, password ≥ 8). Kèm 3 state lỗi: sai mật khẩu, email đã tồn tại, tài khoản bị khoá.
```

### Item detail (slide-over)
```
Thêm panel Item detail trượt từ phải khi click một Item trong list/Kanban, overlay mờ nền. Hiển thị tiêu đề, chip type, badge status, folder tag, metadata theo type, body live. Write-back actions theo type:
- Email: toggle read/star/label/trash (KHÔNG sửa nội dung).
- Event: form sửa title/start/end/location/attendees + nút xoá.
- File: rename/trash + link Drive.
- Ticket(Jira): sửa summary/description, đổi status/assignee/priority, comment, xoá.
Kèm mẫu toast "409 — bản trên provider đã đổi, tải lại" và banner "thiếu scope, hãy reconnect".
```

### Scheduled email
```
Thêm màn Scheduled email: phần Compose (to/cc/bcc/subject/body/chọn thời gian gửi/chọn connection Gmail) + phần List lịch đã đặt với badge status (Pending/Sent/Failed/Cancelled) và nút Cancel (disable nếu đã gửi). Có empty state khi chưa có lịch.
```

### Folders & Share
```
Thêm màn quản lý Folders (list folder có chấm màu, đổi tên, archive) + modal Share: nhập email người nhận, quyền chỉ Viewer-only (ghi rõ "chỉ xem, không thấy nội dung body"), list người đang được chia sẻ + nút gỡ.
```

### Admin dashboard
```
Thêm màn Admin dashboard (chỉ role Admin): 4 stat card (Total users / Active-Locked / Total connections kèm breakdown Active-Error-Disconnected / Sync errors 24h) + 1 bar chart thống kê + bảng users (email, full name, role, isActive, lastLoginAt, connectionCount, itemCount) có search + pagination.
```

### States & responsive (chạy cuối)
```
Rà lại toàn bộ: mỗi list có loading skeleton + empty state + error state (nút thử lại). Mỗi mutation có loading + toast. Thêm biến thể responsive: <1024px sidebar thành drawer (hamburger), Kanban cuộn ngang. Nếu kịp, thêm biến thể dark mode.
```

---

## Chuyển design sang code FE (làm ở Claude Code, không phải Claude Design)

Sau khi có prototype, mở repo `frontend/` trong Claude Code và dán:

```
Đây là design màn [TÊN MÀN] của Workspace Hub (đính kèm HTML/CSS export + ảnh). Code lại thành React + TypeScript + Tailwind trong frontend/src, BÁM docs/CONVENTIONS.md:
- axios instance duy nhất src/lib/api.ts (JWT interceptor); KHÔNG tạo instance thứ hai.
- Server state qua TanStack Query (useQuery/useMutation); KHÔNG useEffect+fetch thủ công.
- Type API khai trong src/types/ và khớp DTO backend.
- Gọi đúng endpoint + envelope như docs/API.md (vd GET /api/items trả {items,total,page,limit}).
Đây là ticket [SCRUM-xx]. Xong cập nhật docs/SPRINTS.md.
```

Map màn ↔ ticket: Auth→SCRUM-42 · Inbox/Items→SCRUM-44 · Kanban+Folder→SCRUM-45 · Item detail/write-back→SCRUM-46 · Connections→SCRUM-43 · Scheduled→SCRUM-47 · Admin→SCRUM-49 · States/toast→SCRUM-48 · Responsive/dark→SCRUM-50.
