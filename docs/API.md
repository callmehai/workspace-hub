# API Design — Workspace Hub (Sprint 1–3 scope)

## Quy ước chung

- **Auth:** JWT Bearer, header `Authorization: Bearer <token>`. Claim: sub (UserId), email, role.
- **Content-Type:** application/json (trừ upload).
- **DateTime:** ISO 8601 UTC (`2026-05-25T14:30:00Z`).
- **Pagination:** `?page=1&limit=20`. Default 20, max 100.
- **Response collection:**
  - List lớn (items, users, notifications, scheduled-emails) → envelope `{ items, total, page, limit }`
  - List nhỏ (integrations, connections, folders, tags, shares) → array thẳng `[...]`

## Status code

| Code | Khi nào |
|---|---|
| 200 | GET/PUT/PATCH OK |
| 201 | POST tạo mới |
| 202 | Nhận xử lý bất đồng bộ (trigger sync) |
| 204 | DELETE OK, no body |
| 400 | Body sai, validation fail |
| 401 | Thiếu/hết hạn token |
| 403 | Có token nhưng không đủ quyền |
| 404 | Không tồn tại |
| 409 | Vi phạm unique constraint |
| 422 | Business rule fail |
| 429 | Rate limit |
| 500 | Lỗi BE |
| 502 | Provider thứ ba lỗi (khi gọi live) |

## Error format
```json
{
  "error": "ValidationError",
  "message": "...",
  "details": [{ "field": "email", "issue": "..." }],
  "traceId": "req_abc123"
}
```
Loại: ValidationError(400) · AuthenticationError(401) · AuthorizationError(403) · NotFoundError(404) · ConflictError(409) · BusinessRuleError(422) · RateLimitError(429) · InternalError(500) · UpstreamError(502)

---

## Auth
- `POST /api/auth/register` — {email, password, fullName} → 201. (400 email sai/pw<8, 409 trùng)
- `POST /api/auth/login` — {email, password} → 200 {accessToken, expiresIn, user}. (401 sai/khoá)
- `GET /api/auth/me` — Bearer → 200 user info
- `POST /api/auth/logout` — Bearer → 204 (stateless, FE xoá token)

## Admin
- `GET /api/admin/users?search=&page=&limit=` — Admin → envelope
- `GET /api/admin/users/{id}` — Admin → chi tiết + connections[]
- `PATCH /api/admin/users/{id}/lock` — Admin → {isActive, reason}
- `GET /api/admin/stats` — Admin → {totalUsers, activeUsers, lockedUsers, totalConnections, connectionsByStatus, totalItems, syncErrorsLast24h}

## Integrations
- `GET /api/integrations` — Bearer → array (kèm myConnectionId)
- `POST /api/admin/integrations` — Admin → tạo (clientSecret mask). 409 key trùng
- `PUT /api/admin/integrations/{id}` — Admin partial
- `PATCH /api/admin/integrations/{id}/disable` — Admin

## OAuth Connections
- `POST /api/connections/oauth/start` — Bearer → {authorizationUrl, state}. (404 integration, 422 disabled)
- `POST /api/connections/oauth/callback` — Bearer → {code, state} → 201 connection + services[]. (400 CSRF, 422 provider từ chối, 409 trùng account)
- `GET /api/connections` — Bearer → array (token mask, services[])
- `POST /api/connections/{id}/refresh` — Bearer → expiresAt mới. (422 refresh invalid→Error, 429)
- `DELETE /api/connections/{id}` — Bearer → 204. CASCADE services; Item giữ lại (ServiceConnectionId=NULL)
- `DELETE /api/admin/connections/{id}` — Admin force-disconnect; ghi Notification(sync_error)

## Service Connections
- `PATCH /api/services/{id}/toggle` — Bearer → {isEnabled}. (403 không thuộc user)
- `POST /api/services/{id}/sync` — Bearer → 202 {jobId, message}. (429 sync quá gần). **Fallback thủ công, không phải cơ chế chính**

## Folders
- `GET /api/folders?includeShared=true` — Bearer → array (id, name, color, icon, sortOrder, isArchived, itemCount, isOwner, permission, ownerName)
- `POST /api/folders` — Bearer → {name, color, icon} → 201
- `PUT /api/folders/{id}` — Bearer (Owner)
- `DELETE /api/folders/{id}` — Bearer (Owner) → 204. CASCADE ItemFolders+FolderShares; Item giữ lại

## Folder Shares
- `POST /api/folders/{folderId}/shares` — Owner → {email, permission} → 201. (404 email, 409 đã share)
- `GET /api/folders/{folderId}/shares` — Owner → array
- `GET /api/shares/pending` — Bearer
- `PATCH /api/shares/{id}/accept` | `/decline` — Bearer → 200/204. (403 không phải người được mời)
- `DELETE /api/shares/{id}` — Owner → 204

## Items
- `GET /api/items?folderId=&status=&type=&isImportant=&search=&page=&limit=` — Bearer → envelope. **Phần search/filter/pagination chính**
- `GET /api/items/{id}/detail` — Bearer → metadata + body live từ provider. (403 Viewer không xem body, 502 provider lỗi)
- `PATCH /api/items/{id}/status` — Bearer → {status} (Kanban)
- `POST /api/items/note` — Bearer → {title, contentMarkdown, folderId?, tagIds?}
- `PATCH /api/items/{id}/archive` — Bearer → {isArchived}

## Item-Folder
- `POST /api/folders/{folderId}/items` — Owner → {itemId, position} → 201. (409 đã thuộc)
- `DELETE /api/folders/{folderId}/items/{itemId}` — Owner → 204
- `PATCH /api/folders/{folderId}/items/reorder` — Owner → {orderedItemIds[]}

## Tags
- `GET /api/tags` — Bearer → array {id,name,color,itemCount}
- `POST /api/tags` — Bearer → {name,color} → 201
- `PUT /api/tags/{id}` — Bearer
- `DELETE /api/tags/{id}` — Bearer → 204 (CASCADE assignments)
- `POST` / `DELETE /api/items/{itemId}/tags/{tagId}` — Bearer → 201/204. (409 assign trùng)

## Important Contacts
- `GET /api/important-contacts` — Bearer
- `POST /api/important-contacts` — {type, identifier, label} → 201. (409 trùng)
- `PUT /api/important-contacts/{id}` — partial
- `DELETE /api/important-contacts/{id}` → 204

## Scheduled Emails
- `POST /api/scheduled-emails` — Bearer → {serviceConnectionId, to[], cc[], bcc[], subject, bodyHtml, sendAt} → 201. (400 sendAt quá khứ, 404 connection, 422 không phải Gmail)
- `GET /api/scheduled-emails?status=&page=&limit=` — Bearer → envelope
- `PATCH /api/scheduled-emails/{id}/cancel` — Bearer → Cancelled (chỉ khi Pending). (422 đã gửi)
- `POST /api/internal/process-scheduled` — **X-Cron-Secret, KHÔNG JWT**. Cron gọi mỗi 5 phút, gửi Pending tới hạn

## Notifications
- `GET /api/notifications?unreadOnly=&page=&limit=` — Bearer → {items, total, page, limit, unreadCount}
- `GET /api/notifications/unread-count` — Bearer → {count}
- `PATCH /api/notifications/{id}/read` — Bearer
- `PATCH /api/notifications/read-all` — Bearer → {markedCount}
- `DELETE /api/notifications/{id}` — Bearer → 204
