# Workspace Hub — Prototype (Claude Design export)

Bản prototype clickable export từ **claude.ai/design** (project "Workspace hub design files").
Đây là **reference cho FE**, KHÔNG phải code chạy thật. Dùng để code lại thành React + TS + Tailwind trong `frontend/` theo `docs/CONVENTIONS.md`.

## Nội dung

| File | Là gì |
|------|-------|
| `workspace-v2.html` | **Prototype v2 (2026-07-07) — chuẩn hiện hành**: self-contained, mở thẳng bằng browser. Cơ chế Folder = context + 2 view Danh sách/Bảng, drag-drop, drawer, dark mode, URL contract + 7 nhóm spec cho dev. **FE code/fix theo file này**; các file dc.html cũ chỉ còn giá trị tham khảo màn phụ. |
| `Workspace Hub.dc.html` | Prototype chính, cú pháp `<x-dc>` của Claude Design (8 màn: Auth, Inbox, Kanban, Item detail slide-over, Connections, Scheduled, Folders+Share, Admin). |
| `support.js` | Runtime của Claude Design (render `<x-dc>` template). Generated — không sửa tay. |
| `wh-data.js` | Mock data (folders, items 5 loại, connections, scheduled, users, admin stats). Tham chiếu khi code FE. |
| `uploads/workspace-hub-design-brief.md` | Design brief gốc (design system, token màu, danh sách màn, states). |
| `uploads/workspace-hub-design-prompts.md` | Prompt từng màn + map màn ↔ ticket SCRUM. |
| `screenshots/` | Ảnh preview từng màn — **chưa copy** (7 file JPEG trên project gốc: 01-login, 02-inbox, 02-03-kanban, 02-04-detail, 01-03-kanban, 01-04-detail, 06-share). Nói nếu muốn mình tải thêm. |

## Xem prototype

`Workspace Hub.dc.html` cần `support.js` + React/ReactDOM (CDN) để render `<x-dc>`. Đây là export tĩnh; cách xem trực quan nhất là mở lại project trên claude.ai/design, hoặc đối chiếu `screenshots/`.

## Design system (tóm tắt — đầy đủ ở brief)

- Primary indigo `#4f46e5`, nền `slate-50`, card trắng radius 12px, input/button radius 8px, viền `slate-200`, font Inter.
- Item type màu: Email=blue · Event=amber · File=emerald · Note=slate · Ticket=violet.
- Kanban status: Cần xem=slate · Đang xử lý=blue · Done=emerald.
- Connection status: Active=emerald · Disconnected=slate · Error=red.
- Có light + dark mode (qua CSS variables trong dc.html).

## Map màn ↔ ticket

Auth→SCRUM-42 · Inbox/Items→SCRUM-44 · Kanban+Folder→SCRUM-45 · Item detail/write-back→SCRUM-46 · Connections→SCRUM-43 · Scheduled→SCRUM-47 · Admin→SCRUM-49 · States/toast→SCRUM-48 · Responsive/dark→SCRUM-50.

> Lưu ý phase: theo `CLAUDE.md`, Admin (SCRUM-49) và Jira/Ticket nằm ở phase sau — prototype có sẵn nhưng đừng code khi chưa tới lượt.
