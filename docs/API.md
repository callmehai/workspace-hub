# API Design — Workspace Hub (Mô hình B + 2 chiều)

> Cập nhật 2026-06-11: thêm Google Sign-In, write-back (PATCH items), Connections thay OAuthConnection/ServiceConnection. Lịch sử: CHANGELOG.md.
>
> File này là **spec target**. Status implement: Auth + Google Sign-In ✅ · Connections đang transitional (xem mục Connections) · Items GET/filter ✅, write-back ⏳ (SCRUM-37) · Scheduled ⏳ (SCRUM-30/31).

## Quy ước chung
- Auth: JWT Bearer. Claim: sub, email, role.
- DateTime ISO 8601 UTC. Pagination ?page&limit (default 20, max 100).
- Collection lớn → envelope `{items,total,page,limit}`; nhỏ → array.

## Status code (bổ sung cho write-back)
Như cũ, lưu ý: **403** thiếu scope ghi (connection cũ readonly) · **409** conflict ETag · **502** provider lỗi khi ghi/đọc live.

> Connection migrate từ mô hình A (SCRUM-34 copy data) vẫn giữ token scope readonly cũ → user phải **reconnect** để có scope ghi (gmail.modify+send / calendar / drive.file) trước khi dùng write-back.

---

## Auth (email/password) — không đổi
- `POST /api/auth/register` · `POST /api/auth/login` · `GET /api/auth/me` · `POST /api/auth/logout`

## Auth Google Sign-In ⭐ mới
- `POST /api/auth/google/start` — AllowAnonymous → {authorizationUrl, state}. Scope chỉ openid/email/profile.
- `POST /api/auth/google/callback` — {code, state} → verify id_token, tìm/tạo/link user, phát JWT. (400 CSRF, 401 token invalid / user khoá)
> KHÔNG tạo Connection. Chỉ tạo/tìm User. Auto-link nếu email trùng.

## Admin — không đổi
`GET /api/admin/users`, `/users/{id}`, `PATCH /users/{id}/lock`, `GET /api/admin/stats`, `DELETE /api/admin/connections/{id}`.

## Integrations
- `GET /api/integrations` — catalog cho user.
- `PATCH /api/admin/integrations/{key}/enable` — Admin bật/tắt integration (`IsEnabled`); tắt → user không initiate connection được (422). ⏳ SCRUM-48.
- ~~`PUT /api/connections/{key}/credentials`~~ — **sẽ xoá ở SCRUM-47**: admin không quản lý credentials nữa, ClientId/Secret đọc từ config/env (CHANGELOG 2026-06-12).

## Connections ⭐ (thay OAuth Connections + Service Connections)
Mô hình B: mỗi service authorize riêng, tạo 1 Connection.

> **Trạng thái transitional (sau SCRUM-34, trước SCRUM-35/36):** DB đã là mô hình B, nhưng endpoint hiện tại vẫn là `POST /api/connections/oauth/start` nhận `{integrationKey, redirectUri}` (chưa per-service) và `POST /api/connections/oauth/callback` **upsert 1 row Connection cho mỗi service được cấp**, response 201 trả `{integrationKey, providerAccountId, connections: [{id, serviceType, status}]}`. SCRUM-35/36 sẽ chuyển sang per-service đúng spec dưới đây — FE wire theo shape mới này.

- `POST /api/connections/start` — {provider, serviceType} → {authorizationUrl, state}. Scope = full của service đó (dev quyết). (404 integration, 422 disabled)
- `POST /api/connections/callback` — {code, state} → 201 tạo **1** Connection. (400 CSRF, 422 provider từ chối, 409 trùng service+account)
- `GET /api/connections` — array (token mask). Mỗi row = 1 service.
- `POST /api/connections/{id}/refresh` — refresh token. (422 invalid→Error)
- `DELETE /api/connections/{id}` — 204, xoá đúng service đó. Items giữ lại (ConnectionId=NULL). KHÔNG ảnh hưởng login hay service khác.
- `POST /api/connections/{id}/sync` — 202 trigger thủ công (fallback).

> Bỏ /api/services/* (mô hình A). Toggle = connect/disconnect cả Connection.

## Folders / Folder Shares / Tags / Important Contacts / Notifications
Không đổi. Xem bản trước.

## Items (thêm write-back ⭐)
- `GET /api/items?folderId&status&type&isImportant&search&page&limit` — envelope. Trả kèm ETag.
- `GET /api/items/{id}/detail` — metadata + body live. (403 Viewer, 502 provider)
- `POST /api/items/note` — tạo Note.
- `POST /api/items/event` ⭐ — tạo Event mới → đẩy lên Calendar.
- `PATCH /api/items/{id}` ⭐ — write-back, body theo Type:
  - Email: `{isUnread?, isStarred?, labels?[], isTrashed?}` (KHÔNG sửa nội dung)
  - Event: `{title?, start?, end?, location?, attendees?[]}`
  - File: `{name?, isTrashed?}`
  - → đẩy lên provider. (403 thiếu scope, 409 conflict ETag, 502 provider lỗi)
- `PATCH /api/items/{id}/status` — Kanban (local only).
- `PATCH /api/items/{id}/archive` — local only.
- `DELETE /api/items/{id}` ⭐ — trash/xoá trên provider + local.

## Item-Folder — không đổi
`POST/DELETE /api/folders/{id}/items`, `PATCH .../reorder`.

## Scheduled Emails (đổi ConnectionId ⭐)
- `POST /api/scheduled-emails` — {connectionId, to[], cc[], bcc[], subject, bodyHtml, sendAt} → 201. (404 connection, 422 connection không phải Gmail)
- `GET /api/scheduled-emails?status&page&limit` — envelope.
- `PATCH /api/scheduled-emails/{id}/cancel` — (422 đã gửi).
- `POST /api/internal/process-scheduled` — X-Cron-Secret. Lấy token từ Connections (Gmail).
