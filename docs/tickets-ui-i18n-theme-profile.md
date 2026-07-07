# Ticket drafts — UI polish + Theme + i18n + Profile + Avatar/R2

> Soạn 2026-07-07. Các task này **chưa có trên Jira** (board hiện tới SCRUM-72). Số `SCRUM-73→76` là **đề xuất tạm** — khi import Jira sẽ tự cấp key mới. Có thể import bằng CSV cuối file hoặc add tay.
>
> **Bối cảnh:** SCRUM-50 hiện đang gộp *"Responsive polish + dashboard chart + dark mode"*. Phần **dark mode** đã có nền tảng + shell/core (xem 76). Có thể tách riêng hoặc đánh dấu tiến độ trong SCRUM-50 — tuỳ nhóm.

---

## SCRUM-73 (đề xuất) — FE: Song ngữ VI/EN (i18n) đổi ngôn ngữ không remount

- **Type:** Story · **Sprint:** Sprint 4 · **Labels:** frontend, i18n
- **Status gợi ý:** 🔄 In Progress (hạ tầng + shell/core xong; page phụ mở rộng dần)

**Mô tả**
Dựng hạ tầng i18n nhẹ (không thêm lib): `I18nProvider` + `useI18n()` + từ điển phẳng VI/EN (`src/i18n/translations.ts`, key dạng `namespace.key`, nội suy `{var}`). Nút đổi ngôn ngữ ở Header + trang auth. Persist `localStorage['wh-lang']`, set `document.documentElement.lang`. **Đổi ngôn ngữ chỉ đổi context value → re-render, KHÔNG unmount/mount lại component** (không mất state form, không refetch query).

**Đã xong:** shell (Sidebar/Header), Login/Register/GoogleSignInButton, Profile, WorkspaceToolbar, nhãn chính Inbox/Kanban/Integrations.
**Còn lại (mở rộng dần):** ScheduledEmails, SendEmail, AdminDashboard, ItemDetail (drawer), các modal (CreateNote/CreateEvent/Folder), VerifyOtp, và các hằng số label (`TYPE_FILTERS`/`STATUS_FILTERS` trong `itemVisuals`, status/type map). Thêm key vào cả `vi` và `en` là đủ.

**Acceptance**
- [ ] Toggle VI↔EN ở Header/Profile, chữ đổi tức thì, không reload trang.
- [ ] Đang gõ form → đổi ngôn ngữ không mất nội dung đã nhập.
- [ ] Reload trình duyệt giữ nguyên ngôn ngữ đã chọn.

---

## SCRUM-74 (đề xuất) — FE: Trang Hồ sơ người dùng (Profile)

- **Type:** Story · **Sprint:** Sprint 4 · **Labels:** frontend, profile
- **Status gợi ý:** ✅ Done (hiển thị + preferences; avatar chờ SCRUM-75)

**Mô tả**
Route `/profile` (trong `MainLayout`), vào từ avatar ở Header và block user ở Sidebar. Hiển thị thông tin tài khoản (họ tên / email / vai trò) + tuỳ chọn hiển thị (Theme Sáng/Tối + Ngôn ngữ VI/EN qua segmented control). Vùng **Ảnh đại diện** đặt sẵn nút *"Đổi ảnh đại diện"* (disabled + badge "Sắp có") — chừa chỗ cho SCRUM-75. Hỗ trợ dark-mode + song ngữ.

**Acceptance**
- [ ] Mở được `/profile` từ Header + Sidebar.
- [ ] Đổi theme/ngôn ngữ ngay trong Profile có hiệu lực toàn app.
- [ ] Đủ dark-mode, không lệch layout.

---

## SCRUM-75 (đề xuất) — Avatar upload + lưu trữ Cloudflare R2  ← task kế tiếp

- **Type:** Story · **Sprint:** Sprint 4 · **Labels:** backend, frontend, storage, r2
- **Status gợi ý:** ⏳ To Do (task tiếp theo — owner đã báo trước)

**Mô tả**
Cho phép user tải ảnh đại diện, lưu trên **Cloudflare R2** (S3-compatible).
- **BE:** thêm cột `Users.AvatarUrl` (migration mới); `IObjectStorage`/`R2Storage` (AWS S3 SDK trỏ endpoint R2); `POST /api/users/me/avatar` (multipart, validate mime image + size ≤ ~2MB) + `DELETE`; cập nhật `UserDto` trả `avatarUrl`. Config qua env `R2:AccountId/AccessKeyId/SecretAccessKey/Bucket/PublicBaseUrl` — **KHÔNG hardcode secret** (đọc config như OAuth).
- **FE:** nút "Đổi ảnh đại diện" ở Profile → chọn (crop optional) → upload (progress) → cập nhật avatar ở Header/Sidebar/Profile; xoá ảnh → về initial.
- **Quyết định cần chốt:** bucket public + CDN domain **hay** proxy tải qua API; có cần presigned URL upload trực tiếp không.

**Acceptance**
- [ ] Upload ảnh → hiện ngay ở Header/Sidebar/Profile, còn sau reload.
- [ ] Validate loại/size, báo lỗi rõ.
- [ ] Secret R2 lấy từ env, không commit.

---

## SCRUM-76 (đề xuất, tuỳ chọn) — FE: Theme Sáng/Tối (toggle, persist, no remount)

- **Type:** Story · **Sprint:** Sprint 4 · **Labels:** frontend, theme
- **Status gợi ý:** 🔄 In Progress (nền tảng + shell/core xong) — **hoặc** gộp vào SCRUM-50.

**Mô tả**
`darkMode:'class'` (Tailwind) + `ThemeProvider` toggle class `.dark` trên `<html>` (thao tác DOM thuần → **không remount**), persist `localStorage['wh-theme']`, inline script trong `index.html` set class trước paint (chống FOUC). Toggle ở Header + Profile. Gộp luôn **thống nhất palette**: `brand` = indigo (trước đây Login/Header dùng blue-600 lệch tông với sidebar/inbox indigo-600).

**Đã style:** shell (MainLayout/Sidebar/Header), Login/Register, Profile, Inbox, KanbanBoard, Integrations, Select, PageSizeSelect, WorkspaceToolbar, Toaster.
**Còn lại:** ScheduledEmails, SendEmail, AdminDashboard, ItemDetail, các modal.

**Acceptance**
- [ ] Toggle Sáng/Tối tức thì, không reload/không mất state.
- [ ] Reload giữ theme; không nháy sáng→tối khi đang ở Tối.

---

## CSV import Jira (Summary, Issue Type, Description, Sprint, Labels, Status)

```csv
Summary,Issue Type,Description,Sprint,Labels,Status
"FE: Song ngữ VI/EN (i18n) không remount",Story,"Hạ tầng i18n (I18nProvider + useI18n + từ điển VI/EN, key phẳng, nội suy {var}); nút đổi ngôn ngữ ở Header + auth; persist localStorage wh-lang; đổi lang chỉ re-render context (không remount). Đã dịch shell/auth/profile/toolbar + nhãn chính Inbox/Kanban/Integrations; còn ScheduledEmails/SendEmail/Admin/ItemDetail/modals mở rộng dần.",Sprint 4,"frontend;i18n",In Progress
"FE: Trang Hồ sơ người dùng (Profile)",Story,"Route /profile trong MainLayout, vào từ avatar Header + block user Sidebar. Hiển thị họ tên/email/vai trò + tuỳ chọn Theme + Ngôn ngữ. Vùng avatar có nút Đổi ảnh đại diện (disabled, badge Sắp có) chừa chỗ cho ticket avatar/R2. Dark-mode + song ngữ.",Sprint 4,"frontend;profile",Done
"Avatar upload + lưu trữ Cloudflare R2",Story,"Cột Users.AvatarUrl + migration; IObjectStorage/R2Storage (S3 SDK trỏ R2); POST/DELETE /api/users/me/avatar (validate mime image, size); UserDto trả avatarUrl; config env R2:* không hardcode. FE: nút Đổi ảnh đại diện ở Profile -> upload -> cập nhật Header/Sidebar/Profile. Chốt: bucket public+CDN hay proxy qua API.",Sprint 4,"backend;frontend;storage;r2",To Do
"FE: Theme Sáng/Tối (toggle, persist, no remount)",Story,"darkMode:class + ThemeProvider toggle class .dark trên <html> (không remount), persist wh-theme, inline script chống FOUC; toggle ở Header + Profile; thống nhất palette brand=indigo. Đã style shell/auth/profile/Inbox/Kanban/Integrations/Select/toolbar; còn ScheduledEmails/SendEmail/Admin/ItemDetail/modals. Có thể gộp vào SCRUM-50.",Sprint 4,"frontend;theme",In Progress
```
