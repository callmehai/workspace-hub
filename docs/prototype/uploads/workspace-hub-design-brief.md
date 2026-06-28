# Workspace Hub — Design Brief

> Context để nạp vào Claude Design. Mục tiêu: dựng prototype clickable cho TẤT CẢ các màn, sau đó dùng làm reference code FE (Vite + React + TS + Tailwind).

---

## 1. Sản phẩm

Web app gom **Gmail · Google Calendar · Google Drive** (và Jira ở phase sau) về một nơi, quản lý theo **Folder context** (dự án/khách hàng/chủ đề) với giao diện **Kanban 3 cột**. Có chia sẻ folder (Viewer-only), hẹn giờ gửi email, và dashboard admin. Đồng bộ 2 chiều (đọc + ghi ngược lên Google).

**Đối tượng:** người dùng văn phòng cần một inbox/board hợp nhất. Cảm giác: gọn, sạch, productivity-tool, không màu mè.

**Ngôn ngữ UI:** Tiếng Việt. Light mode là chính, có dark mode.

**Item có 5 loại:** Email · Event · File · Note · Ticket (Jira).

---

## 2. Design system (token — bám sát để code FE thẳng ra Tailwind)

**Màu**
- Primary: indigo `#4f46e5` (Tailwind `indigo-600`), hover `indigo-700`.
- Nền trang: `slate-50` `#f8fafc`. Surface/card: trắng. Viền: `slate-200`. Text: `slate-900` (chính) / `slate-500` (phụ) / `slate-400` (hint).
- Màu theo **Item type** (để quét nhanh trên Kanban):
  - Email → blue (`bg-blue-50 text-blue-700`)
  - Event → amber (`bg-amber-50 text-amber-700`)
  - File → emerald (`bg-emerald-50 text-emerald-700`)
  - Note → slate (`bg-slate-100 text-slate-600`)
  - Ticket → violet (`bg-violet-50 text-violet-700`)
- Màu theo **Status (Kanban)**: Cần xem → slate · Đang xử lý → blue · Done → emerald.
- Màu **Connection status**: Active → emerald, Disconnected → slate, Error → red.

**Typography:** Inter / system. H1 22px, H2 18px, H3 16px (weight 500). Body 14px. Caption 12px. Hai weight: 400 và 500. Sentence case, không Title Case, không ALL CAPS.

**Spacing & shape:** lưới 4–8px. Card radius `12px`, input/button radius `8px`. Border `1px slate-200`. Bóng: hầu như không, chỉ focus ring + shadow rất nhẹ cho dropdown/slide-over.

**Icon:** outline (Lucide / Tabler style), 16–20px.

---

## 3. Component cơ bản (định nghĩa 1 lần, dùng lại mọi màn)

- **Button**: primary (indigo fill), secondary (viền slate, nền trắng), ghost (chỉ text). Cao 36px, radius 8px.
- **Input / Select / Textarea**: cao 36px, viền slate-200, focus ring indigo.
- **Badge / Pill**: dùng cho type, status, label — nền nhạt + chữ đậm cùng tông (xem màu type ở trên).
- **Card**: nền trắng, viền slate-200, radius 12px, padding 16px.
- **Item row** (list): icon-type | tiêu đề + snippet | meta (from/time) | badge status | sao Important.
- **Kanban card**: chip type nhỏ + tiêu đề + tag folder + meta time; có affordance kéo-thả (cursor grab, shadow nhẹ khi hover).
- **Toast**: react-hot-toast style, góc trên phải; success (emerald), error (red). Map từ error format `{ error, message, details[], traceId }`.
- **Slide-over panel**: trượt từ phải, overlay mờ nền.
- **Empty / Loading / Error state**: mỗi list phải có cả 3 (skeleton khi load, illustration nhẹ + CTA khi rỗng, thông báo + nút thử lại khi lỗi).

---

## 4. App shell (khung dùng chung)

- **Sidebar trái** (~220px): logo "Workspace Hub" trên cùng; nav chính: Inbox · Kanban · Connections · Scheduled · Admin (Admin chỉ hiện với role Admin); mục **Folders** bên dưới = list folder có chấm màu, nút "+ New folder"; đáy sidebar: avatar + tên user + nút logout. Mobile: sidebar thu thành drawer (nút hamburger).
- **Topbar**: ô search toàn cục (trái) · cụm filter chip (folder / status / type / important) · nút "Connect service" (indigo) · chuông notification · avatar.
- **Khu nội dung**: render màn được chọn.

---

## 5. Danh sách màn (dựng đủ, clickable)

### 5.1 Auth — Login & Register
Card giữa màn, nền slate-50. Login: email + password + nút "Đăng nhập" (indigo) + đường kẻ "hoặc" + nút **Đăng nhập bằng Google** (viền, logo G). Link sang Register. Register thêm Full name + confirm password. Validation inline (email hợp lệ, password ≥ 8). State lỗi: "Sai email hoặc mật khẩu", "Email đã tồn tại", "Tài khoản bị khoá".

### 5.2 Inbox / Items
App shell + list. Trên cùng: filter bar (chip folder/status/type/important) + search + nút view (list/board). List các Item row, mỗi row có icon-type màu, tiêu đề, snippet ~1 dòng, meta (người gửi / thời gian), badge status, sao Important toggle. Footer pagination (`{items,total,page,limit}`, default 20). Có empty + loading skeleton.

### 5.3 Kanban board
3 cột: **Cần xem · Đang xử lý · Done**. Mỗi cột có header (tên + đếm số) và list Kanban card kéo-thả được. Sidebar folder context lọc card theo folder. Kéo card đổi cột = đổi status. Card click → mở Item detail.

### 5.4 Item detail (slide-over)
Trượt từ phải khi click một Item. Hiển thị: tiêu đề, chip type, badge status, folder tag, toàn bộ metadata theo type, và body live. **Write-back actions theo type**:
- Email: toggle read/unread, star, gắn label, trash; nút "Soạn email mới". KHÔNG sửa nội dung email.
- Event: sửa title/start/end/location/attendees (form), nút xoá.
- File: rename, trash, link mở trên Drive.
- Ticket (Jira): sửa summary/description, đổi status (transition), assignee, priority, thêm comment, xoá.
Hiển thị mẫu **toast 409 conflict** ("Bản trên provider đã đổi — tải lại") và state 403 thiếu scope (gợi ý reconnect).

### 5.5 Connections
Grid card mỗi service: Gmail · Google Calendar · Google Drive · Jira. Mỗi card: icon, tên, badge status (Active/Disconnected/Error), "Last synced ...", nút Connect / Disconnect / Refresh. Card chưa connect ở trạng thái mờ + nút "Kết nối". Giải thích ngắn: Google Sign-In ≠ Connect service.

### 5.6 Scheduled email
2 phần: **Compose** (to / cc / bcc / subject / body rich-ish / chọn thời gian gửi / chọn connection Gmail) + **List** lịch đã đặt với badge status (Pending/Sent/Failed/Cancelled), nút Cancel (disable nếu đã gửi). Empty state khi chưa có lịch.

### 5.7 Folders & Share
List folder (chấm màu, đổi tên, archive). **Modal Share**: nhập email người nhận, quyền chỉ **Viewer-only** (ghi rõ "Người được chia sẻ chỉ xem, không thấy nội dung body"), list người đang được chia sẻ + nút gỡ.

### 5.8 Admin dashboard
4 **stat card**: Total users · Active/Locked · Total connections (kèm breakdown Active/Error/Disconnected) · Sync errors 24h. Một **chart** (bar/line) thống kê. Bảng **users** (email, full name, role, isActive, lastLoginAt, connectionCount, itemCount) + search + pagination. Chỉ role Admin.

---

## 6. States & patterns (áp cho mọi màn)

- Mọi list: skeleton loading → empty state có CTA → error state có nút thử lại.
- Mọi mutation: nút có loading + toast success/error.
- 409 conflict (write-back): toast + tự reload item, không ghi đè mù.
- 403 thiếu scope: banner/toast gợi ý reconnect service.
- Responsive: ≥1024 sidebar cố định; <1024 sidebar drawer; Kanban cuộn ngang trên mobile.

---

## 7. Output mong muốn từ Claude Design

- **Prototype clickable**: dựng đủ các màn ở mục 5, nối được nhau (sidebar nav chuyển màn, click item mở detail).
- Light mode trước; nếu kịp thêm biến thể dark.
- UI copy tiếng Việt như mô tả.
- Bám design system mục 2–3 để khi export sang React + Tailwind là gần như 1-1.
- Mỗi màn kèm sẵn empty/loading state để FE không phải nghĩ lại.
