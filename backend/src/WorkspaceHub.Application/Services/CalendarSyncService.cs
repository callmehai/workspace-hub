using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkspaceHub.Application.Abstractions;
using WorkspaceHub.Application.Interfaces.Repositories;
using WorkspaceHub.Application.Interfaces.Services;
using WorkspaceHub.Application.Mapping;
using WorkspaceHub.Domain.Entities;
using WorkspaceHub.Domain.Enums;

namespace WorkspaceHub.Application.Services
{
    public class CalendarSyncService : ICalendarSyncService
    {
        private readonly IGoogleCalendarGateway _gateway;
        private readonly ICalendarItemMapper _mapper;
        private readonly IItemRepository _items;
        private readonly IConnectionRepository _connections;
        public CalendarSyncService(
            IGoogleCalendarGateway gateway,
            ICalendarItemMapper mapper,
            IItemRepository items,
            IConnectionRepository connections)
        {
            _gateway = gateway;
            _mapper = mapper;
            _items = items;
            _connections = connections;
        }
        private async Task<Connection> GetValidConnectionAsync(Guid connectionId, Guid userId, CancellationToken ct)
        {
            var conn = await _connections.GetByIdAsync(connectionId, ct);
            if (conn is null || conn.UserId != userId)
                throw new WorkspaceHub.Application.Common.NotFoundException("Connection", connectionId);
            if (conn.ServiceType != ServiceType.GCal)
                throw new WorkspaceHub.Application.Common.BusinessRuleException("Kết nối này không phải Google Calendar");
            return conn;
        }
        public async Task<SyncResult> SyncAsync(Guid connectionId, Guid userId, CancellationToken ct = default)
        {
            var conn = await GetValidConnectionAsync(connectionId, userId, ct);
            return await SyncConnectionAsync(conn, ct);
        }
        public async Task<SyncResult> SyncConnectionAsync(Connection connection, CancellationToken ct = default)
        {
            // 1. Lấy danh sách các ID sự kiện đã lưu trong Database để tránh trùng
            var existing = await _items.GetExistingExternalIdsAsync(connection.Id, ct);
            var newItems = new List<Item>();

            // 2. Lấy cái đánh dấu trang (SyncToken) của lần đồng bộ trước (nếu có)
            string? syncToken = connection.CursorType == CursorType.SyncToken ? connection.CursorValue : null;
            // 3. Gọi Gateway để kéo dữ liệu về
            var result = await _gateway.SyncEventsAsync(connection, syncToken, ct);
            // NẾU token bị hết hạn do quá lâu không sync (Google xoá mất thẻ đánh dấu), 
            // Gateway sẽ trả về Expired = true. Mình phải ra lệnh cho nó gọi lại Full Sync từ đầu
            if (result.Expired)
            {
                result = await _gateway.SyncEventsAsync(connection, null, ct);
            }
            int scanned = result.Events.Count;
            int created = 0;
            int skipped = 0;
            // 4. Lọc trùng lặp & Dịch thuật (Mapping)
            foreach (var ev in result.Events)
            {
                if (existing.Contains(ev.Id))
                {
                    skipped++;
                    continue;
                }
                var item = _mapper.ToItem(ev, connection.UserId, connection.Id);
                newItems.Add(item);
                existing.Add(ev.Id);
                created++;
            }
            // 5. Lưu vào Database
            if (newItems.Any())
            {
                await _items.AddRangeAsync(newItems, ct);
                await _items.SaveChangesAsync(ct);
            }
            // 6. Cập nhật thẻ đánh dấu mới (SyncToken) cho lần đồng bộ kế tiếp
            connection.CursorType = CursorType.SyncToken;
            connection.CursorValue = result.NextSyncToken;
            connection.LastSyncedAt = DateTime.UtcNow;
            connection.Status = ConnectionStatus.Active;
            connection.LastError = null;
            _connections.Update(connection);
            await _connections.SaveChangesAsync(ct);
            return new SyncResult(scanned, created, skipped, result.NextSyncToken);
        }
    }
}
