#!/usr/bin/env python3
"""Sinh JSON sequence-diagram, MỖI USE CASE 1 file. Trả về dict {filename: spec}."""
import json

S = {}  # filename (không đuôi) -> spec

# ============================ 4.1 CONNECTION & OAUTH ============================
S["4.1.2.1-connect-service-sequence"] = {
 "title": "UC 4.1.1 — Connect service (mô hình B)",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"cc","label":"ConnectionsController"},{"id":"cs","label":"ConnectionsService"},
   {"id":"cache","label":"Redis\n(state)"},{"id":"st","label":"IProviderStrategy\nGoogle | Jira"},
   {"id":"prov","label":"Google / Atlassian"},{"id":"repo","label":"Connection /\nIntegration Repo"},
   {"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"bật service (Gmail / Calendar / Drive / Jira)"},
   {"from":"fe","to":"cc","label":"POST /api/connections/oauth/start { integrationKey, serviceType, redirectUri }"},
   {"from":"cc","to":"cs","label":"InitiateConnectionAsync(key, serviceType, redirectUri, userId)"},
   {"from":"cs","to":"repo","label":"GetByKeyAsync(integrationKey)"},
   {"from":"repo","to":"db","label":"SELECT Integrations"},
   {"from":"db","to":"cs","label":"integration","return":True},
   {"note":"!IsEnabled → BusinessRuleException · không có strategy → NotFoundException","over":"cs"},
   {"from":"cs","to":"cache","label":"SetString(oauth:state:{state}, payload, TTL 10 phút)"},
   {"from":"cs","to":"st","label":"BuildAuthUrlAsync(clientId, redirectUri, state, serviceType)"},
   {"from":"st","to":"cs","label":"InitiateConnectionResult(authorizationUrl, state)","return":True},
   {"from":"cc","to":"fe","label":"200 { authorizationUrl, state }","return":True},
   {"from":"fe","to":"prov","label":"redirect → consent (FULL scope của service)"},
   {"from":"prov","to":"fe","label":"redirect về app: ?code & state","return":True},
   {"from":"fe","to":"cc","label":"POST /api/connections/oauth/callback { code, state }"},
   {"from":"cc","to":"cs","label":"CompleteConnectionAsync(code, state, userId)"},
   {"from":"cs","to":"cache","label":"GetString + RemoveAsync (one-time)"},
   {"from":"cache","to":"cs","label":"OAuthStatePayload","return":True},
   {"note":"state sai / payload.UserId != userId → CsrfException (403)","over":"cs"},
   {"from":"cs","to":"st","label":"ExchangeCodeAsync(code, clientId, clientSecret)"},
   {"from":"st","to":"prov","label":"POST /token (+ [Jira] GET accessible-resources → cloudId)"},
   {"from":"prov","to":"st","label":"access/refresh token, scope","return":True},
   {"from":"st","to":"cs","label":"TokenExchangeResult(providerAccountId, grantedServices)","return":True},
   {"note":"ITokenProtector.Protect(token) — ASP.NET Data Protection","over":"cs"},
   {"from":"cs","to":"repo","label":"GetByUniqueKeyAsync(...) → null (chưa có)"},
   {"from":"cs","to":"repo","label":"AddAsync(new Connection{ Status=Active, CursorValue=null }) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT Connections"},
   {"from":"cc","to":"fe","label":"201 Created (CompleteConnectionResponse)","return":True},
   {"note":"Sync lần đầu KHÔNG chạy ở callback — chạy on-demand (GET /api/items) hoặc bấm resync","over":"cs"},
   {"from":"fe","to":"u","label":"service hiển thị Connected","return":True}]}

S["4.1.2.2-reconnect-service-sequence"] = {
 "title": "UC 4.1.2 — Reconnect service (cấp lại quyền)",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"cc","label":"ConnectionsController"},{"id":"cs","label":"ConnectionsService"},
   {"id":"st","label":"IProviderStrategy"},{"id":"prov","label":"Google / Atlassian"},
   {"id":"repo","label":"ConnectionRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"bấm Kết nối lại (service đã từng connect)"},
   {"from":"fe","to":"prov","label":"lại luồng OAuth consent → ?code & state"},
   {"from":"fe","to":"cc","label":"POST /api/connections/oauth/callback { code, state }"},
   {"from":"cc","to":"cs","label":"CompleteConnectionAsync(code, state, userId)"},
   {"from":"cs","to":"st","label":"ExchangeCodeAsync(...)"},
   {"from":"st","to":"prov","label":"đổi token"},
   {"from":"prov","to":"cs","label":"TokenExchangeResult(providerAccountId, grantedServices)","return":True},
   {"from":"cs","to":"repo","label":"GetByUniqueKeyAsync(userId, provider, svc, providerAccountId)"},
   {"from":"repo","to":"cs","label":"connection (ĐÃ tồn tại)","return":True},
   {"note":"reconnect: cập nhật AccessTokenEncrypted, RefreshTokenEncrypted (nếu provider trả mới),\nExpiresAt, Status = Active — GIỮ NGUYÊN CursorValue → không mất tiến độ sync, KHÔNG 409","over":"cs"},
   {"from":"cs","to":"repo","label":"Update(connection) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"UPDATE Connections"},
   {"from":"cc","to":"fe","label":"201 Created","return":True},
   {"from":"fe","to":"u","label":"kết nối được khôi phục","return":True}]}

S["4.1.2.3-resync-connection-sequence"] = {
 "title": "UC 4.1.3 — Resync connection (đồng bộ thủ công)",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"cc","label":"ConnectionsController"},{"id":"disp","label":"ConnectionSyncDispatcher"},
   {"id":"sync","label":"Sync service\nGmail/Cal/Drive/Jira"},{"id":"prov","label":"Google / Jira"},
   {"id":"noti","label":"SyncItemNotification\nService"},{"id":"repo","label":"ConnectionRepository /\nItemRepository"},
   {"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"bấm Đồng bộ ngay"},
   {"from":"fe","to":"cc","label":"POST /api/connections/{id}/sync"},
   {"from":"cc","to":"disp","label":"SyncAsync(id, CurrentUserId, markProviderError = true)"},
   {"from":"disp","to":"repo","label":"GetByIdAsync(id) → check UserId"},
   {"from":"disp","to":"repo","label":"GetExistingExternalIdsAsync(id) → beforeExternalIds"},
   {"from":"disp","to":"sync","label":"switch ServiceType → SyncConnectionAsync(connection, batch)"},
   {"from":"sync","to":"prov","label":"list messages / events / files / issues"},
   {"from":"prov","to":"sync","label":"dữ liệu mới","return":True},
   {"from":"sync","to":"repo","label":"AddRangeAsync(items mới) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT Items"},
   {"from":"sync","to":"disp","label":"SyncResult(Scanned, Created, Skipped, NewCursor)","return":True},
   {"note":"lỗi provider → MarkConnectionErrorAsync (Status = Error, LastError) + ProviderException (502)","over":"disp"},
   {"from":"disp","to":"noti","label":"Created > 0 → NotifyNewItemsAsync(id, userId, beforeExternalIds)"},
   {"from":"cc","to":"fe","label":"200 SyncResult","return":True},
   {"from":"fe","to":"u","label":"item mới xuất hiện + toast thông báo","return":True}]}

S["4.1.2.4-disconnect-service-sequence"] = {
 "title": "UC 4.1.4 — Disconnect service",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"cc","label":"ConnectionsController"},{"id":"cs","label":"ConnectionsService"},
   {"id":"repo","label":"Connection / Item /\nScheduledEmail Repo"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"Ngắt kết nối service"},
   {"from":"fe","to":"cc","label":"DELETE /api/connections/{id}"},
   {"from":"cc","to":"cs","label":"DisconnectAsync(connectionId, CurrentUserId)"},
   {"from":"cs","to":"repo","label":"GetByIdTrackedAsync(id)"},
   {"from":"repo","to":"cs","label":"connection","return":True},
   {"note":"null → NotFoundException 404 · connection.UserId != userId → ForbiddenException 403","over":"cs"},
   {"from":"cs","to":"repo","label":"ExecuteInTransactionAsync:"},
   {"from":"cs","to":"repo","label":"DeleteByConnectionIdAsync(Items)"},
   {"from":"cs","to":"repo","label":"DeleteByConnectionIdAsync(ScheduledEmails)"},
   {"from":"cs","to":"repo","label":"Remove(connection) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"DELETE Items, ScheduledEmails, Connection"},
   {"from":"cc","to":"fe","label":"204 No Content","return":True},
   {"from":"fe","to":"u","label":"cập nhật trang Integrations","return":True}]}

# ============================ 4.2 ITEM & WORKSPACE ============================
S["4.2.2.1-view-items-lazy-sync-sequence"] = {
 "title": "UC 4.2.1 — Xem danh sách item (lazy / on-demand sync)",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"ic","label":"ItemsController"},{"id":"is","label":"ItemService"},
   {"id":"hc","label":"ConnectionHealthChecker"},{"id":"disp","label":"SyncDispatcher\n+ Sync services"},
   {"id":"prov","label":"Google / Jira"},{"id":"repo","label":"ItemRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"mở Inbox / Kanban"},
   {"from":"fe","to":"ic","label":"GET /api/items?folderId&statuses&page&limit"},
   {"from":"ic","to":"is","label":"GetItemsAsync(userId, request)"},
   {"from":"is","to":"hc","label":"EnsureAllSyncedAsync(userId)  (try/catch — lỗi không chặn list)"},
   {"note":"debounce 30s theo LastSyncedAt · timeout 15s/connection\ntoken sắp hết hạn → RefreshConnectionAsync","over":"hc"},
   {"from":"hc","to":"disp","label":"SyncAsync(connectionId, userId)"},
   {"from":"disp","to":"prov","label":"SyncConnectionAsync → list dữ liệu mới"},
   {"from":"prov","to":"disp","label":"messages / events / files / issues","return":True},
   {"from":"disp","to":"repo","label":"AddRangeAsync(items mới) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT Items"},
   {"from":"hc","to":"is","label":"done","return":True},
   {"from":"is","to":"repo","label":"GetPagedAsync(targetOwnerId, filter, page, limit)"},
   {"from":"repo","to":"db","label":"SELECT Items + ItemFolders + Tags"},
   {"from":"db","to":"repo","label":"rows","return":True},
   {"from":"repo","to":"is","label":"(items, totalCount, threadCounts)","return":True},
   {"from":"is","to":"ic","label":"PagedResult<ItemResponse>","return":True},
   {"from":"ic","to":"fe","label":"200 OK","return":True},
   {"from":"fe","to":"u","label":"hiển thị danh sách / bảng Kanban","return":True}]}

S["4.2.2.2-writeback-item-sequence"] = {
 "title": "UC 4.2.2 — Ghi ngược thao tác lên provider (write-back + conflict 409)",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"ic","label":"ItemsController"},{"id":"wb","label":"ItemWriteBackService"},
   {"id":"guard","label":"IWriteBackGuard"},{"id":"gw","label":"Gateway\nGmail/Cal/Drive/Jira"},
   {"id":"prov","label":"Google / Jira"},{"id":"repo","label":"Item / Folder Repo"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"đánh dấu đã đọc / gắn sao / đổi tên file / sửa event"},
   {"from":"fe","to":"ic","label":"PATCH /api/items/{id} { isUnread, isStarred, title, ... }"},
   {"from":"ic","to":"wb","label":"PatchItemAsync(itemId, userId, payload)"},
   {"from":"wb","to":"repo","label":"GetByIdAndUserAsync(itemId, userId)"},
   {"note":"null → IsItemSharedWithUserAsEditorAsync · viewer → 403 · không quyền → 404","over":"wb"},
   {"from":"wb","to":"gw","label":"GetMessageETagAsync / GetEventAsync / GetFileAsync / GetIssueAsync"},
   {"from":"gw","to":"prov","label":"GET (đọc version hiện tại)"},
   {"from":"prov","to":"wb","label":"providerEtag (Jira: fields.updated)","return":True},
   {"from":"wb","to":"guard","label":"EnsureNoConflict(item.ETag, providerEtag)"},
   {"note":"ALT — providerEtag ≠ item.ETag:\nConflictException → KHÔNG gọi gateway ghi, KHÔNG SaveChanges → 409 (FE refetch ETag mới)\n(Email luôn bỏ qua guard — Gmail ETag đổi liên tục)","over":"guard"},
   {"from":"guard","to":"wb","label":"OK (khớp / rỗng)","return":True},
   {"from":"wb","to":"gw","label":"ModifyMessageAsync / UpdateEventAsync / UpdateFileAsync / UpdateIssueAsync"},
   {"from":"gw","to":"prov","label":"ghi thật lên provider"},
   {"from":"prov","to":"wb","label":"newETag","return":True},
   {"from":"wb","to":"repo","label":"cập nhật Title / OccurredAt / MetadataJson / ETag + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"UPDATE Items"},
   {"from":"ic","to":"fe","label":"200 ItemResponse","return":True},
   {"from":"fe","to":"u","label":"UI phản ánh thay đổi","return":True}]}

S["4.2.2.3-create-item-sequence"] = {
 "title": "UC 4.2.3 — Tạo item mới (Event / Ticket / Note)",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"ic","label":"ItemsController"},{"id":"wb","label":"ItemWriteBackService"},
   {"id":"is","label":"ItemService"},{"id":"gw","label":"Calendar / Jira\nGateway"},
   {"id":"prov","label":"Google / Jira"},{"id":"repo","label":"ItemRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"note":"Event / Ticket — tạo trên provider rồi lưu Item","over":"ic"},
   {"from":"u","to":"fe","label":"tạo sự kiện / ticket mới"},
   {"from":"fe","to":"ic","label":"POST /api/items/event | /api/items/ticket"},
   {"from":"ic","to":"wb","label":"CreateEventAsync / CreateTicketAsync(userId, payload)"},
   {"from":"wb","to":"gw","label":"InsertEventAsync / CreateIssueAsync(connection, dto)"},
   {"from":"gw","to":"prov","label":"POST event / issue"},
   {"from":"prov","to":"wb","label":"externalId + ETag / fields.updated","return":True},
   {"from":"wb","to":"repo","label":"AddAsync(new Item{ Type, ExternalId, ETag }) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT Items"},
   {"from":"ic","to":"fe","label":"201 ItemResponse","return":True},
   {"note":"Note — item local, KHÔNG ConnectionId / ExternalId (không gọi provider)","over":"ic"},
   {"from":"fe","to":"ic","label":"POST /api/items/note { title, snippet, folderId? }"},
   {"from":"ic","to":"is","label":"CreateNoteAsync(userId, request)"},
   {"from":"is","to":"repo","label":"AddAsync(Item{ Type=Note }) (+ ItemFolder nếu có folderId) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT Items (+ ItemFolders)"},
   {"from":"ic","to":"fe","label":"201 ItemResponse","return":True},
   {"from":"fe","to":"u","label":"item hiển thị ở Inbox / Kanban","return":True}]}

S["4.2.2.4-delete-item-sequence"] = {
 "title": "UC 4.2.4 — Xoá item",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"ic","label":"ItemsController"},{"id":"wb","label":"ItemWriteBackService"},
   {"id":"gw","label":"Gateway\nGmail/Cal/Drive/Jira"},{"id":"prov","label":"Google / Jira"},
   {"id":"repo","label":"ItemRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"xoá item"},
   {"from":"fe","to":"ic","label":"DELETE /api/items/{id}"},
   {"from":"ic","to":"wb","label":"DeleteItemAsync(itemId, userId)  (không cần check ETag)"},
   {"from":"wb","to":"repo","label":"GetByIdAndUserAsync(itemId, userId)"},
   {"note":"Email có ThreadId → xoá cả thread:\nTrashThreadAsync / DeleteThreadAsync + _items.DeleteThreadAsync","over":"wb"},
   {"from":"wb","to":"gw","label":"DeleteMessage / DeleteEvent / TrashFile / DeleteIssue"},
   {"from":"gw","to":"prov","label":"xoá trên provider (Event: nuốt NotFound)"},
   {"from":"prov","to":"wb","label":"OK","return":True},
   {"from":"wb","to":"repo","label":"Remove(item) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"DELETE Items (+ ItemFolders, TagAssignments)"},
   {"from":"ic","to":"fe","label":"204 No Content","return":True},
   {"from":"fe","to":"u","label":"item biến mất khỏi UI","return":True}]}

# ============================ 4.3 FOLDER / KANBAN / TAG ============================
S["4.3.2.1-create-folder-sequence"] = {
 "title": "UC 4.3.1 — Tạo Folder",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"fc","label":"FoldersController"},{"id":"fs","label":"FolderService"},
   {"id":"repo","label":"FolderRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"tạo folder dự án mới"},
   {"from":"fe","to":"fc","label":"POST /api/folders { name, color, icon }"},
   {"note":"CreateFolderValidator.ValidateAndThrowAsync","over":"fc"},
   {"from":"fc","to":"fs","label":"CreateAsync(userId, request)"},
   {"from":"fs","to":"repo","label":"GetMaxSortOrderAsync(userId)"},
   {"from":"repo","to":"fs","label":"maxSort","return":True},
   {"from":"fs","to":"repo","label":"AddAsync(Folder{ SortOrder = maxSort + 1 }) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT Folders"},
   {"from":"fs","to":"repo","label":"GetByIdWithOwnerAsync(folder.Id)  (nạp OwnerName)"},
   {"from":"fs","to":"fc","label":"FolderResponse","return":True},
   {"from":"fc","to":"fe","label":"201 Created","return":True},
   {"from":"fe","to":"u","label":"folder mới trong sidebar","return":True}]}

S["4.3.2.2-add-item-to-folder-sequence"] = {
 "title": "UC 4.3.2 — Kéo item vào Folder",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"fc","label":"FoldersController"},{"id":"fs","label":"FolderService"},
   {"id":"repo","label":"Folder / Item Repo"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"kéo-thả item vào folder"},
   {"from":"fe","to":"fc","label":"POST /api/folders/{id}/items { itemId }"},
   {"from":"fc","to":"fs","label":"AddItemToFolderAsync(userId, folderId, request)"},
   {"from":"fs","to":"repo","label":"ExistsByOwnerAsync(folderId, userId)"},
   {"note":"false → ForbiddenException(FolderOwnerOnly) 403","over":"fs"},
   {"from":"fs","to":"repo","label":"GetByIdAndUserAsync(itemId, userId)"},
   {"note":"null → ForbiddenException(ItemNotOwned) 403","over":"fs"},
   {"from":"fs","to":"repo","label":"ItemFolderExistsAsync(itemId, folderId)"},
   {"note":"true → ConflictException(ItemAlreadyInFolder) 409","over":"fs"},
   {"from":"fs","to":"repo","label":"GetMaxItemPositionAsync(folderId)"},
   {"from":"repo","to":"fs","label":"maxPos","return":True},
   {"from":"fs","to":"repo","label":"AddItemFolderAsync(ItemFolder{ Position = maxPos + 1 }) + Save"},
   {"from":"repo","to":"db","label":"INSERT ItemFolders"},
   {"from":"fc","to":"fe","label":"201 ItemFolderResponse(ItemId, FolderId, Position, AddedAt)","return":True},
   {"from":"fe","to":"u","label":"item nằm trong folder","return":True}]}

S["4.3.2.3-change-kanban-status-sequence"] = {
 "title": "UC 4.3.3 — Đổi trạng thái Kanban",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"ic","label":"ItemsController"},{"id":"is","label":"ItemService"},
   {"id":"repo","label":"Item / Folder Repo"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"kéo card sang cột Đang xử lý / Done"},
   {"from":"fe","to":"ic","label":"PATCH /api/items/{id}/status { status: Doing }"},
   {"from":"ic","to":"is","label":"UpdateStatusAsync(userId, itemId, request)"},
   {"from":"is","to":"repo","label":"GetByIdAndUserAsync(itemId, userId)"},
   {"note":"null → IsItemSharedWithUserAsEditorAsync(itemId, userId)\nviewer → 403 SharedViewerReadOnly · không quyền → 404","over":"is"},
   {"from":"is","to":"repo","label":"item.Status = request.Status → Update + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"UPDATE Items SET Status  (không đụng ItemFolder.Position)"},
   {"from":"is","to":"ic","label":"ItemResponse","return":True},
   {"from":"ic","to":"fe","label":"200 OK → invalidateQueries(items)","return":True},
   {"from":"fe","to":"u","label":"card ở cột mới","return":True}]}

S["4.3.2.4-manage-tag-sequence"] = {
 "title": "UC 4.3.4 — Quản lý Tag (tạo & gán)",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"tc","label":"TagsController"},{"id":"ts","label":"TagService"},
   {"id":"repo","label":"Tag / Item Repo"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"note":"Tạo tag","over":"tc"},
   {"from":"u","to":"fe","label":"tạo tag mới"},
   {"from":"fe","to":"tc","label":"POST /api/tags { name, color }"},
   {"from":"tc","to":"ts","label":"CreateAsync(userId, request)"},
   {"from":"ts","to":"repo","label":"NameExistsAsync(userId, name) → 409 nếu trùng (UNIQUE UserId,Name)"},
   {"from":"ts","to":"repo","label":"AddAsync(Tag) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT Tags"},
   {"from":"tc","to":"fe","label":"201 TagResponse","return":True},
   {"note":"Gán tag cho item","over":"tc"},
   {"from":"u","to":"fe","label":"chọn tag cho 1 item"},
   {"from":"fe","to":"tc","label":"POST /api/tags/{id}/items { itemId }"},
   {"from":"tc","to":"ts","label":"AssignAsync(userId, tagId, request)"},
   {"from":"ts","to":"repo","label":"GetByIdAndUserAsync(tagId, userId) → 404 nếu không phải tag của mình"},
   {"from":"ts","to":"repo","label":"item của user HOẶC IsItemSharedWithUserAsync (Viewer cũng được)"},
   {"from":"ts","to":"repo","label":"AssignmentExistsAsync(tagId, itemId) → 409 nếu đã gắn"},
   {"from":"ts","to":"repo","label":"AddAssignmentAsync(TagAssignment) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT TagAssignments"},
   {"from":"tc","to":"fe","label":"201 TagAssignmentResponse","return":True},
   {"from":"fe","to":"u","label":"tag hiển thị trên item","return":True}]}

# ============================ 4.4 EMAIL WORKSPACE ============================
S["4.4.2.1-send-email-sequence"] = {
 "title": "UC 4.4.1 — Gửi email ngay (kèm đính kèm)",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"ec","label":"EmailsController"},{"id":"ses","label":"SendEmailService"},
   {"id":"gw","label":"IGmailGateway"},{"id":"gmail","label":"Gmail API"},
   {"id":"repo","label":"ConnectionRepository"}],
 "messages": [
   {"from":"u","to":"fe","label":"soạn email + đính kèm file"},
   {"from":"fe","to":"ec","label":"POST /api/emails/send { connectionId, to, cc, subject, bodyHtml, attachments[] }"},
   {"note":"SendEmailRequestValidator + RequestSizeLimit 40 MB","over":"ec"},
   {"from":"ec","to":"ses","label":"SendAsync(userId, request)"},
   {"from":"ses","to":"repo","label":"GetByIdTrackedAsync(connectionId)"},
   {"from":"repo","to":"ses","label":"connection","return":True},
   {"note":"UserId lệch → 404 · ServiceType != Gmail / Status != Active → 400","over":"ses"},
   {"from":"ses","to":"gw","label":"SendMessageAsync(conn, to, cc, bcc, subject, html, attachments)"},
   {"from":"gw","to":"gmail","label":"users.messages.send (MIME RFC 2822, From = ProviderAccountId)"},
   {"from":"gmail","to":"gw","label":"messageId","return":True},
   {"from":"ses","to":"ec","label":"SendEmailResult(messageId, sentAt)","return":True},
   {"from":"ec","to":"fe","label":"200 OK","return":True},
   {"note":"Không tạo Item ngay — email vào Inbox sau lần GmailSync kế tiếp","over":"ses"},
   {"from":"fe","to":"u","label":"đã gửi","return":True}]}

S["4.4.2.2-reply-forward-email-sequence"] = {
 "title": "UC 4.4.2 — Trả lời / Chuyển tiếp trong thread",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"ec","label":"EmailsController"},{"id":"ses","label":"SendEmailService"},
   {"id":"gw","label":"IGmailGateway"},{"id":"gmail","label":"Gmail API"},
   {"id":"repo","label":"Item / Connection Repo"}],
 "messages": [
   {"from":"u","to":"fe","label":"mở email → bấm Reply / Forward"},
   {"from":"fe","to":"ec","label":"POST /api/emails/reply | /api/emails/forward"},
   {"from":"ec","to":"ses","label":"ReplyAsync / ForwardAsync(userId, request)"},
   {"from":"ses","to":"repo","label":"GetAndValidateConnectionAndItemAsync(requireEditor = true)"},
   {"note":"threadId + rfc822 messageId đọc từ item.MetadataджSON[\"threadId\"]\nthiếu → BusinessRuleException","over":"ses"},
   {"from":"ses","to":"gw","label":"[Forward] GetMessageAsync(item.ExternalId) — dựng block Forwarded + tải attachment"},
   {"from":"gw","to":"gmail","label":"users.messages.get / attachments.get"},
   {"from":"gmail","to":"ses","label":"nội dung gốc","return":True},
   {"from":"ses","to":"gw","label":"SendInThreadAsync(conn, threadId, inReplyTo, subject 'Re:'/'Fwd:', ...)"},
   {"from":"gw","to":"gmail","label":"users.messages.send (cùng thread)"},
   {"from":"gmail","to":"ses","label":"messageId","return":True},
   {"from":"ses","to":"ec","label":"SendInThreadResult(messageId, threadId, sentAt)","return":True},
   {"from":"ec","to":"fe","label":"200 OK","return":True},
   {"from":"fe","to":"u","label":"tin nhắn nằm trong thread","return":True}]}

S["4.4.2.3-schedule-email-sequence"] = {
 "title": "UC 4.4.3 — Hẹn giờ gửi email",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"sc","label":"ScheduledEmail\nCommandsController"},{"id":"ss","label":"ScheduledEmailsService"},
   {"id":"repo","label":"Connection / ScheduledEmail Repo"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"soạn email + chọn thời điểm gửi"},
   {"from":"fe","to":"sc","label":"POST /api/scheduled-emails { connectionId, to, subject, bodyHtml, attachments, sendAt }"},
   {"from":"sc","to":"ss","label":"CreateAsync(userId, request)"},
   {"from":"ss","to":"repo","label":"load connection → check UserId + ServiceType == Gmail"},
   {"from":"ss","to":"repo","label":"AddAsync(ScheduledEmail{ Status = Pending, AttachmentsJson = base64 }) + Save"},
   {"from":"repo","to":"db","label":"INSERT ScheduledEmails  (attachment lưu base64 trong DB)"},
   {"from":"ss","to":"sc","label":"ScheduledEmailDto","return":True},
   {"from":"sc","to":"fe","label":"201 Created","return":True},
   {"note":"Huỷ: PATCH /api/scheduled-emails/{id}/cancel — Sent/Failed → 400,\nPending → Status = Cancelled","over":"sc"},
   {"from":"fe","to":"u","label":"email nằm trong danh sách chờ gửi","return":True}]}

S["4.4.2.4-cron-send-scheduled-sequence"] = {
 "title": "UC 4.4.4 — Cron gửi email đến hạn",
 "participants": [
   {"id":"cron","label":"Cron\n(X-Cron-Secret)","actor":True},{"id":"intc","label":"InternalController"},
   {"id":"pss","label":"ProcessScheduled\nEmailsService"},{"id":"gw","label":"IGmailGateway"},
   {"id":"gmail","label":"Gmail API"},{"id":"repo","label":"ScheduledEmailRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"cron","to":"intc","label":"POST /api/internal/process-scheduled  (header X-Cron-Secret)"},
   {"note":"ValidateCronSecret: FixedTimeEquals với Cron:Secret — sai/thiếu → 401\n(CsrfMiddleware bỏ qua path này)","over":"intc"},
   {"from":"intc","to":"pss","label":"ProcessDueEmailsAsync(maxBatch = 50)"},
   {"from":"pss","to":"repo","label":"GetPendingDueEmailsAsync(utcNow, 50)"},
   {"from":"repo","to":"db","label":"SELECT WHERE Status=Pending AND SendAt<=now ORDER BY SendAt"},
   {"from":"db","to":"pss","label":"due[]","return":True},
   {"from":"pss","to":"pss","label":"loop: lấy connection (cache) + DecodeAttachments(base64)"},
   {"note":"connection null / không Gmail / không Active → MarkFailed(Status=Failed, RetryCount++, LastError)","over":"pss"},
   {"from":"pss","to":"gw","label":"SendMessageAsync(conn, ...)"},
   {"from":"gw","to":"gmail","label":"users.messages.send"},
   {"from":"gmail","to":"pss","label":"messageId → Status = Sent, SentAt = UtcNow","return":True},
   {"from":"pss","to":"repo","label":"SaveChangesAsync (một lần cho cả batch)"},
   {"from":"repo","to":"db","label":"UPDATE ScheduledEmails"},
   {"from":"pss","to":"intc","label":"ProcessScheduledResult(total, sent, failed)","return":True},
   {"note":"Fail → Status = Failed; cron chỉ quét Pending ⇒ KHÔNG auto-retry lượt sau","over":"pss"},
   {"from":"intc","to":"cron","label":"200 { total, sent, failed }","return":True}]}

# ============================ 4.5 FOLDER SHARING ============================
S["4.5.2.1-invite-share-sequence"] = {
 "title": "UC 4.5.1 — Owner mời chia sẻ Folder",
 "participants": [
   {"id":"a","label":"Owner A","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"fc","label":"FoldersController"},{"id":"fs","label":"FolderService"},
   {"id":"ns","label":"NotificationService\n+ SignalR"},{"id":"b","label":"Viewer B","actor":True},
   {"id":"repo","label":"Folder / Friendship Repo"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"a","to":"fe","label":"chọn bạn bè + quyền (Viewer / Editor)"},
   {"from":"fe","to":"fc","label":"POST /api/folders/{id}/shares { friendUserId, permission }"},
   {"from":"fc","to":"fs","label":"InviteShareAsync(folderId, requestingUserId = A, request)"},
   {"from":"fs","to":"repo","label":"GetByIdWithOwnerAsync(folderId)"},
   {"note":"folder.OwnerId != A → 403 · friendUserId == A → 400","over":"fs"},
   {"from":"fs","to":"repo","label":"GetBetweenAsync(A, B) → Friendship.Status"},
   {"note":"không phải bạn bè (Accepted) → BusinessRuleException · ShareExistsAsync → 409","over":"fs"},
   {"from":"fs","to":"repo","label":"AddShareAsync(FolderShare{ Permission, AcceptedAt = null }) + Save"},
   {"from":"repo","to":"db","label":"INSERT FolderShares"},
   {"from":"fs","to":"ns","label":"CreateAndSendAsync(B, ShareInvite, ...)  (best-effort)"},
   {"from":"ns","to":"b","label":"toast realtime 'A đã chia sẻ folder ...'","async":True},
   {"from":"fc","to":"fe","label":"201 FolderShareDto (Status = Pending)","return":True},
   {"from":"fe","to":"a","label":"lời mời đã gửi","return":True}]}

S["4.5.2.2-accept-share-sequence"] = {
 "title": "UC 4.5.2 — Người nhận chấp nhận / từ chối lời mời",
 "participants": [
   {"id":"b","label":"Viewer B","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"fc","label":"FoldersController"},{"id":"fs","label":"FolderService"},
   {"id":"repo","label":"FolderRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"b","to":"fe","label":"mở trang Shared with me"},
   {"from":"fe","to":"fc","label":"GET /api/folders/shared-with-me"},
   {"from":"fc","to":"fs","label":"GetFoldersSharedWithMeAsync(B)"},
   {"from":"fs","to":"repo","label":"GetSharesForUserAsync(B)"},
   {"from":"fs","to":"fc","label":"SharedFolderDto[] (Status = Pending | Accepted)","return":True},
   {"from":"fc","to":"fe","label":"200 OK","return":True},
   {"from":"b","to":"fe","label":"bấm Chấp nhận"},
   {"from":"fe","to":"fc","label":"POST /api/folders/shares/{shareId}/accept"},
   {"from":"fc","to":"fs","label":"AcceptShareAsync(shareId, B)"},
   {"note":"share.SharedWithUserId != B → 403 · đã accept → 409\n(Từ chối: POST .../decline → DeclineShareAsync)","over":"fs"},
   {"from":"fs","to":"repo","label":"share.AcceptedAt = UtcNow + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"UPDATE FolderShares"},
   {"from":"fc","to":"fe","label":"200 OK","return":True},
   {"from":"fe","to":"b","label":"folder xuất hiện trong danh sách","return":True}]}

S["4.5.2.3-viewer-read-drive-child-sequence"] = {
 "title": "UC 4.5.3 — Viewer đọc item trong folder được share (kể cả file Drive con)",
 "participants": [
   {"id":"b","label":"Viewer B","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"is","label":"ItemService /\nDriveContentService"},{"id":"perm","label":"FolderRepository\n(share-check)"},
   {"id":"conn","label":"ConnectionRepository"},{"id":"gw","label":"IDriveGateway"},
   {"id":"google","label":"Google Drive API"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"b","to":"fe","label":"mở folder được chia sẻ"},
   {"from":"fe","to":"is","label":"GET /api/items?folderId={id}"},
   {"from":"is","to":"perm","label":"ExistsByOwnerAsync(folderId, B) → false"},
   {"from":"is","to":"perm","label":"GetShareByFolderAndUserAsync(folderId, B)"},
   {"note":"share == null hoặc AcceptedAt == null ⇒ trả PagedResult RỖNG (không 403)","over":"is"},
   {"from":"is","to":"db","label":"GetPagedAsync(targetOwnerId = A, folderId, ...)"},
   {"from":"db","to":"is","label":"items của A","return":True},
   {"from":"is","to":"fe","label":"200 PagedResult (IsOwner = false)","return":True},
   {"from":"fe","to":"is","label":"GET /api/items/{fileId}/content  (file con trong folder Drive)"},
   {"from":"is","to":"perm","label":"IsItemSharedWithUserAsync | IsConnectionSharedWithUserAsync"},
   {"from":"perm","to":"is","label":"true (share đã Accepted)","return":True},
   {"from":"is","to":"conn","label":"GetByIdAsync(item.ConnectionId)"},
   {"from":"conn","to":"is","label":"connection của A","return":True},
   {"note":"BẮT BUỘC: if (conn.UserId != item.UserId) → 404\n(so với chủ item, KHÔNG so với người gọi)","over":"is"},
   {"from":"is","to":"gw","label":"DownloadFile / Thumbnail (token của OWNER A)"},
   {"from":"gw","to":"google","label":"files.get"},
   {"from":"google","to":"is","label":"nội dung file","return":True},
   {"from":"is","to":"fe","label":"200 file content","return":True},
   {"from":"fe","to":"b","label":"hiển thị (chỉ đọc — thao tác ghi → 403)","return":True}]}

# ============================ 4.6 CONTACT & NOTIFICATION ============================
S["4.6.2.1-mark-important-contact-sequence"] = {
 "title": "UC 4.6.1 — Đánh dấu liên hệ quan trọng",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"icc","label":"ImportantContacts\nController"},{"id":"ics","label":"ImportantContactService"},
   {"id":"repo","label":"ImportantContactRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"đánh dấu 'sếp@company.com' (email / Jira account) là quan trọng"},
   {"from":"fe","to":"icc","label":"POST /api/importantcontacts { type, identifier, label }"},
   {"note":"CreateImportantContactRequestValidator","over":"icc"},
   {"from":"icc","to":"ics","label":"CreateAsync(userId, request)"},
   {"from":"ics","to":"repo","label":"ExistsAsync(userId, type, identifier)"},
   {"note":"đã tồn tại → ConflictException 409","over":"ics"},
   {"from":"ics","to":"repo","label":"AddAsync(ImportantContact) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT ImportantContacts"},
   {"from":"icc","to":"fe","label":"201 ImportantContactResponse","return":True},
   {"note":"Ảnh hưởng khi SYNC sau này: GmailSync set Item.IsImportant nếu người gửi ∈ danh sách này","over":"ics"},
   {"from":"fe","to":"u","label":"liên hệ nằm trong danh sách quan trọng","return":True}]}

S["4.6.2.2-sync-notification-realtime-sequence"] = {
 "title": "UC 4.6.2 — Sinh thông báo item mới & đẩy realtime",
 "participants": [
   {"id":"disp","label":"SyncDispatcher"},{"id":"gsync","label":"GmailSyncService\n+ Mapper"},
   {"id":"sins","label":"SyncItemNotification\nService"},{"id":"ns","label":"NotificationService"},
   {"id":"pub","label":"SignalR Publisher\n+ NotificationHub"},{"id":"fe","label":"Frontend SPA"},
   {"id":"u","label":"User","actor":True},{"id":"repo","label":"Item / Notification Repo"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"disp","to":"repo","label":"GetExistingExternalIdsAsync(connectionId) → beforeExternalIds"},
   {"from":"disp","to":"gsync","label":"SyncConnectionAsync(connection, batch = 100)"},
   {"from":"gsync","to":"repo","label":"GetIdentifiersAsync(userId, ImportantContactType.Email)"},
   {"from":"gsync","to":"gsync","label":"ToItem(message): From ∈ importantEmails → IsImportant = true"},
   {"from":"gsync","to":"repo","label":"AddRangeAsync(items mới) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT Items"},
   {"from":"gsync","to":"disp","label":"SyncResult(Created > 0)","return":True},
   {"from":"disp","to":"sins","label":"NotifyNewItemsAsync(connectionId, userId, beforeExternalIds)"},
   {"from":"sins","to":"repo","label":"lấy items sau sync → lọc ExternalId mới"},
   {"note":"bỏ email label TRASH / SPAM · > 10 item → chỉ giữ IsImportant, tối đa 10","over":"sins"},
   {"from":"sins","to":"ns","label":"CreateAndSendAsync(userId, ImportantEmail | ItemSynced, titleKey, body, /inbox?item=...)"},
   {"from":"ns","to":"repo","label":"AddAsync(Notification{ IsRead = false }) + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"INSERT Notifications"},
   {"from":"ns","to":"pub","label":"PublishToUserAsync(userId, dto)"},
   {"from":"pub","to":"fe","label":"Clients.User(userId).ReceiveNotification(dto)","async":True},
   {"note":"FE: dedupe id → +1 unreadCount → prepend danh sách → showNotificationToast","over":"fe"},
   {"from":"fe","to":"u","label":"toast 'Email quan trọng từ ...' (bấm → /inbox?item=...)","return":True}]}

S["4.6.2.3-mark-read-notification-sequence"] = {
 "title": "UC 4.6.3 — Đánh dấu thông báo đã đọc",
 "participants": [
   {"id":"u","label":"User","actor":True},{"id":"fe","label":"Frontend SPA"},
   {"id":"ncc","label":"Notification\nCommandsController"},{"id":"ns","label":"NotificationService"},
   {"id":"repo","label":"NotificationRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"u","to":"fe","label":"mở dropdown / bấm 1 thông báo"},
   {"from":"fe","to":"ncc","label":"PATCH /api/notifications/{id}/read"},
   {"from":"ncc","to":"ns","label":"MarkAsReadAsync(userId, notificationId)"},
   {"from":"ns","to":"repo","label":"GetByIdForUserAsync(userId, id)"},
   {"note":"null → NotFoundException 404 (scope theo user) · đã IsRead → không save lại","over":"ns"},
   {"from":"ns","to":"repo","label":"IsRead = true + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"UPDATE Notifications"},
   {"from":"ncc","to":"fe","label":"204 No Content","return":True},
   {"from":"fe","to":"ncc","label":"POST /api/notifications/read-all"},
   {"from":"ncc","to":"ns","label":"MarkAllAsReadAsync(userId) → bulk update (không publish)"},
   {"from":"ncc","to":"fe","label":"204 No Content","return":True},
   {"from":"fe","to":"u","label":"badge unread về 0 (không có push ngược từ server)","return":True}]}

# ============================ 4.7 ADMIN ============================
S["4.7.2.1-admin-dashboard-sequence"] = {
 "title": "UC 4.7.1 — Xem dashboard thống kê",
 "participants": [
   {"id":"ad","label":"Admin","actor":True},{"id":"fe","label":"Frontend\nAdminDashboard"},
   {"id":"jwt","label":"JWT + Authorize\n(Roles = Admin)"},{"id":"ac","label":"AdminController"},
   {"id":"as","label":"AdminService"},{"id":"repo","label":"AppDbContext"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"ad","to":"fe","label":"mở trang Admin"},
   {"from":"fe","to":"jwt","label":"GET /api/admin/stats  (cookie wh_access)"},
   {"note":"role != Admin → 403 Forbidden","over":"jwt"},
   {"from":"jwt","to":"ac","label":"GetStats(ct)"},
   {"from":"ac","to":"as","label":"GetStatsAsync(ct)"},
   {"from":"as","to":"repo","label":"CountAsync(Users) / CountAsync(IsActive) / GroupBy(Connection.Status) / CountAsync(Items)"},
   {"from":"as","to":"repo","label":"đếm Connection Status=Error trong 24h (LastSyncedAt >= cutoff)"},
   {"from":"repo","to":"db","label":"SELECT COUNT / GROUP BY"},
   {"from":"db","to":"as","label":"số liệu","return":True},
   {"from":"as","to":"ac","label":"AdminStatsDto(TotalUsers, ActiveUsers, LockedUsers, ...)","return":True},
   {"from":"ac","to":"fe","label":"200 OK","return":True},
   {"from":"fe","to":"jwt","label":"GET /api/admin/users?search&page&limit"},
   {"from":"jwt","to":"as","label":"GetUsersAsync(request)  (validator: page≥1, limit 1..100)"},
   {"from":"as","to":"repo","label":"projection { User, ConnCount, ItemCount } + Skip/Take"},
   {"from":"as","to":"fe","label":"200 PagedResult<AdminUserDto>","return":True},
   {"from":"fe","to":"ad","label":"hiển thị số liệu + danh sách user","return":True}]}

S["4.7.2.2-admin-lock-user-sequence"] = {
 "title": "UC 4.7.2 — Khoá / mở khoá tài khoản user",
 "participants": [
   {"id":"ad","label":"Admin","actor":True},{"id":"fe","label":"Frontend"},
   {"id":"ac","label":"AdminController"},{"id":"as","label":"AdminService"},
   {"id":"repo","label":"AppDbContext"},{"id":"db","label":"SQL Server"},
   {"id":"auth","label":"AuthService\n(lần login sau)"}],
 "messages": [
   {"from":"ad","to":"fe","label":"bấm Khoá tài khoản"},
   {"from":"fe","to":"ac","label":"POST /api/admin/users/{id}/toggle-active"},
   {"from":"ac","to":"as","label":"ToggleUserActiveAsync(id, CurrentUserId)  (lấy từ claim)"},
   {"note":"id == currentAdminId → BusinessRuleException ('không thể tự khoá chính mình')","over":"as"},
   {"from":"as","to":"repo","label":"Users.FindAsync(id)"},
   {"from":"repo","to":"as","label":"user (null → NotFoundException 404)","return":True},
   {"note":"user.Role == Admin && user.IsActive → BusinessRuleException ('Không thể khoá Admin')","over":"as"},
   {"from":"as","to":"repo","label":"user.IsActive = !user.IsActive + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"UPDATE Users"},
   {"from":"as","to":"repo","label":"đếm lại ConnectionCount / ItemCount"},
   {"from":"ac","to":"fe","label":"200 AdminUserDto → refetch danh sách","return":True},
   {"from":"ad","to":"auth","label":"(user bị khoá) đăng nhập lại"},
   {"from":"auth","to":"ad","label":"SignInAsync thấy !IsActive → UnauthorizedException ('Account is locked')","return":True}]}

S["4.7.2.3-admin-toggle-integration-sequence"] = {
 "title": "UC 4.7.3 — Bật / tắt integration",
 "participants": [
   {"id":"ad","label":"Admin","actor":True},{"id":"fe","label":"Frontend"},
   {"id":"aic","label":"AdminIntegrations\nController"},{"id":"cs","label":"ConnectionsService"},
   {"id":"repo","label":"IIntegrationRepository"},{"id":"db","label":"SQL Server"}],
 "messages": [
   {"from":"ad","to":"fe","label":"tắt integration atlassian"},
   {"from":"fe","to":"aic","label":"PATCH /api/admin/integrations/atlassian/enable { isEnabled: false }"},
   {"note":"[Authorize(Roles = Admin)] — role khác → 403","over":"aic"},
   {"from":"aic","to":"cs","label":"ToggleIntegrationAsync(key, isEnabled)"},
   {"from":"cs","to":"repo","label":"GetByKeyAsync(key)"},
   {"from":"repo","to":"cs","label":"integration (null → NotFoundException 404)","return":True},
   {"from":"cs","to":"repo","label":"integration.IsEnabled = false → Update + SaveChangesAsync"},
   {"from":"repo","to":"db","label":"UPDATE Integrations"},
   {"from":"cs","to":"aic","label":"IntegrationResponse(Id, Key, DisplayName, IsEnabled)","return":True},
   {"from":"aic","to":"fe","label":"200 OK","return":True},
   {"note":"InitiateConnectionAsync kiểm !IsEnabled → user thường không connect service này nữa","over":"cs"},
   {"from":"fe","to":"ad","label":"integration hiển thị Disabled","return":True}]}

for name, spec in S.items():
    json.dump(spec, open(name + ".json", "w"), ensure_ascii=False, indent=1)
print(len(S), "use-case sequence specs")
