import { useState, useCallback, useRef, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { itemsApi, foldersApi } from '../lib/itemsApi';
import { handleApiError } from '../lib/errorUtils';
import { isEmailUnread } from '../lib/itemMeta';
import type { ItemType, ItemStatus, ItemResponse, PagedResult } from '../types/items';
import {
  Star, AlertCircle, Inbox as InboxIcon,
  ChevronLeft, ChevronRight,
} from 'lucide-react';
import { ItemDetail } from '../components/ItemDetail';
import { BulkActionBar } from '../components/BulkActionBar';
import { WorkspaceToolbar } from '../components/workspace/WorkspaceToolbar';
import { typeIcon } from '../lib/itemVisuals';
import { PageSizeSelect } from '../components/PageSizeSelect';
import { formatDistanceToNow } from 'date-fns';
import { vi } from 'date-fns/locale';

// ─── helpers ────────────────────────────────────────────────────────────────

function formatTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  return formatDistanceToNow(d, { addSuffix: true, locale: vi });
}

function typeLabel(t: ItemType): string {
  const map: Record<ItemType, string> = {
    Email: 'Email', Event: 'Sự kiện', File: 'Tệp', Note: 'Ghi chú', Ticket: 'Ticket',
  };
  return map[t] ?? t;
}

function typeTileClass(t: ItemType): string {
  const map: Record<ItemType, string> = {
    Email: 'bg-blue-50 text-blue-600',
    Event: 'bg-amber-50 text-amber-600',
    File: 'bg-emerald-50 text-emerald-600',
    Note: 'bg-slate-100 text-slate-500',
    Ticket: 'bg-purple-50 text-purple-600',
  };
  return map[t] ?? 'bg-slate-100 text-slate-500';
}

// "Đã xem" = Inbox + Email đã đọc → xám (đã lướt mắt). Các Inbox khác = "Chưa xem" → cam (cần chú ý).
function isSeen(item: ItemResponse): boolean {
  return item.status === 'Inbox' && item.type === 'Email' && !isEmailUnread(item);
}

function statusChipClass(item: ItemResponse): string {
  if (item.status === 'Inbox') {
    return isSeen(item) ? 'bg-slate-100 text-slate-500' : 'bg-amber-50 text-amber-700';
  }
  const map: Record<string, string> = {
    Doing: 'bg-blue-50 text-blue-700',
    Done: 'bg-emerald-50 text-emerald-700',
  };
  return map[item.status] ?? 'bg-slate-100 text-slate-500';
}

function statusDotClass(item: ItemResponse): string {
  if (item.status === 'Inbox') {
    return isSeen(item) ? 'bg-slate-300' : 'bg-amber-500';
  }
  const map: Record<string, string> = {
    Doing: 'bg-blue-500',
    Done: 'bg-emerald-500',
  };
  return map[item.status] ?? 'bg-slate-400';
}

function statusLabel(s: ItemStatus): string {
  const map: Record<ItemStatus, string> = {
    Inbox: 'Chưa xem', Doing: 'Đang xử lý', Done: 'Hoàn thành',
  };
  return map[s] ?? s;
}

function getItemStatusLabel(item: ItemResponse): string {
  if (item.status === 'Inbox' && item.type === 'Email' && !isEmailUnread(item)) {
    return 'Đã xem';
  }
  return statusLabel(item.status);
}

/*
 * Style đã đọc / chưa đọc kiểu Gmail — CHỈ áp cho Email:
 *  - Chưa đọc: nền TRẮNG + tiêu đề đậm + chấm xanh + thời gian xanh đậm.
 *  - Đã đọc:  nền xám nhạt + chữ thường, màu dịu.
 *  - Loại khác (Event/File/Note/Ticket): trung tính như chưa đọc nhưng không chấm xanh.
 */
function rowVisual(item: ItemResponse, selected: boolean, checked: boolean) {
  const unread = isEmailUnread(item);
  const readEmail = item.type === 'Email' && !unread;
  return {
    unread,
    row: selected
      ? 'bg-indigo-50/60'
      : checked
        ? 'bg-indigo-50/40'
        : readEmail
          ? 'bg-slate-100/70 hover:bg-slate-100'
          : 'bg-white hover:bg-slate-50',
    title: unread
      ? 'font-bold text-slate-900'
      : readEmail
        ? 'font-medium text-slate-600'
        : 'font-semibold text-slate-800',
    snippet: unread ? 'text-slate-600' : readEmail ? 'text-slate-400' : 'text-slate-500',
    time: unread ? 'text-blue-600 font-semibold' : 'text-slate-400',
  };
}

// ─── skeleton row ────────────────────────────────────────────────────────────
function SkeletonRow() {
  return (
    <div className="flex items-center gap-3 px-4 py-[13px] border-b border-slate-100 animate-pulse">
      <div className="w-9 h-9 rounded-lg bg-slate-100 flex-shrink-0" />
      <div className="flex-1 space-y-2">
        <div className="h-3.5 bg-slate-100 rounded w-2/3" />
        <div className="h-3 bg-slate-100 rounded w-full" />
      </div>
      <div className="flex-shrink-0 space-y-1.5 flex flex-col items-end">
        <div className="h-3 bg-slate-100 rounded w-10" />
        <div className="h-5 bg-slate-100 rounded-full w-20" />
      </div>
    </div>
  );
}

// ─── pagination ──────────────────────────────────────────────────────────────
function buildPageNumbers(current: number, total: number): (number | '…')[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const pages: (number | '…')[] = [];
  const delta = 2;
  const left = current - delta;
  const right = current + delta;
  let prev: number | null = null;
  for (let p = 1; p <= total; p++) {
    if (p === 1 || p === total || (p >= left && p <= right)) {
      if (prev !== null && p - prev > 1) pages.push('…');
      pages.push(p);
      prev = p;
    }
  }
  return pages;
}

// ─── main component ──────────────────────────────────────────────────────────
export const Inbox = () => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<ItemStatus | null>(null);
  const [typeFilter, setTypeFilter] = useState<ItemType | null>(null);
  const [importantOnly, setImportantOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [limit, setLimit] = useState(20);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  // Folder = CONTEXT của trang — DERIVE thẳng từ URL (không state+effect,
  // tránh render frame đầu bị null → header nháy "Tất cả mục" rồi mới hiện tên folder).
  const selectedFolderId = searchParams.get('folder');

  // Multi-selection state
  const [selectedItemIds, setSelectedItemIds] = useState<Set<string>>(new Set());

  // Đổi context → về trang 1
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPage(1);
  }, [selectedFolderId]);

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleSearchChange = useCallback((val: string) => {
    setSearchInput(val);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setSearch(val.trim());
      setPage(1);
    }, 350);
  }, []);

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  const params = {
    status: statusFilter ?? undefined,
    type: typeFilter ?? undefined,
    isImportant: importantOnly || undefined,
    search: search || undefined,
    folderId: selectedFolderId || undefined,
    page,
    limit,
  };

  const queryKey = ['items', { status: params.status, type: params.type, isImportant: params.isImportant, search: params.search, folderId: params.folderId, page, limit }];

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey,
    queryFn: () => itemsApi.getItems(params),
    placeholderData: (prev) => prev,
  });

  const { mutate: toggleImportant } = useMutation({
    mutationFn: ({ id, isImportant }: { id: string; isImportant: boolean }) =>
      itemsApi.updateItemImportant(id, isImportant),
    onMutate: async ({ id, isImportant }) => {
      await queryClient.cancelQueries({ queryKey: ['items'] });
      const previous = queryClient.getQueryData<PagedResult<ItemResponse>>(queryKey);
      queryClient.setQueryData(queryKey, (old: PagedResult<ItemResponse> | undefined) => {
        if (!old) return old;
        return { ...old, items: old.items.map((it: ItemResponse) => it.id === id ? { ...it, isImportant } : it) };
      });
      return { previous };
    },
    onError: (err, _vars, ctx: { previous?: PagedResult<ItemResponse> } | undefined) => {
      if (ctx?.previous) queryClient.setQueryData(queryKey, ctx.previous);
      handleApiError(err, 'Lỗi đánh dấu quan trọng', { navigate });
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['items'] }),
  });

  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / limit));
  const rangeStart = total === 0 ? 0 : (page - 1) * limit + 1;
  const rangeEnd = Math.min(page * limit, total);

  // Clear selection when page or filters change
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setSelectedItemIds(new Set());
  }, [page, limit, statusFilter, typeFilter, importantOnly, search, selectedFolderId]);

  const toggleSelection = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    const newSet = new Set(selectedItemIds);
    if (newSet.has(id)) {
      newSet.delete(id);
    } else {
      newSet.add(id);
    }
    setSelectedItemIds(newSet);
  };

  const toggleAllSelection = () => {
    if (selectedItemIds.size === items.length) {
      setSelectedItemIds(new Set());
    } else {
      setSelectedItemIds(new Set(items.map((i: ItemResponse) => i.id)));
    }
  };

  const handleDragStart = (e: React.DragEvent, id: string) => {
    // Pass either the single dragged item, or a JSON array if dragging a selection
    if (selectedItemIds.has(id) && selectedItemIds.size > 1) {
      e.dataTransfer.setData('itemIds', JSON.stringify(Array.from(selectedItemIds)));
    } else {
      e.dataTransfer.setData('itemId', id);
    }
  };

  const clearFilters = () => {
    setStatusFilter(null);
    setTypeFilter(null);
    setImportantOnly(false);
    setSearchInput('');
    setSearch('');
    setPage(1);
  };

  const currentFolder = selectedFolderId
    ? folders.find(f => f.id === selectedFolderId) ?? null
    : null;
  const hasActiveFilters = Boolean(statusFilter || typeFilter || importantOnly || search);

  const isEmpty = !isLoading && !isError && items.length === 0;
  const showList = !isLoading && !isError && items.length > 0;

  const pageNumbers = buildPageNumbers(page, totalPages);

  return (
    <div className="flex-1 min-h-0 bg-slate-50 overflow-y-auto">
      <div className="max-w-[1400px] mx-auto px-6 py-5">

        {/* ── Toolbar dùng chung với view Bảng — layout GIỐNG HỆT khi đổi view ── */}
        <WorkspaceToolbar
          view="list"
          folder={currentFolder}
          folderId={selectedFolderId}
          subtitle={isLoading ? 'Đang tải…' : `${total} mục`}
          statusFilter={statusFilter}
          onStatusFilter={(s) => { setStatusFilter(s); setPage(1); }}
          typeFilter={typeFilter}
          onTypeFilter={(t) => { setTypeFilter(t); setPage(1); }}
          importantOnly={importantOnly}
          onImportantToggle={() => { setImportantOnly(v => !v); setPage(1); }}
          searchInput={searchInput}
          onSearchChange={handleSearchChange}
        />

        {/* ── Active filter summary (KHÔNG gồm folder — folder là context, hiển thị ở header) ── */}
        {hasActiveFilters && (
          <div className="flex items-center gap-2 mb-3 text-[12.5px] text-slate-500 flex-wrap">
            <span>Đang lọc:</span>
            {statusFilter && <span className="px-2 py-0.5 rounded-full bg-slate-100">{statusLabel(statusFilter)}</span>}
            {typeFilter && <span className="px-2 py-0.5 rounded-full bg-slate-100">{typeLabel(typeFilter)}</span>}
            {importantOnly && <span className="px-2 py-0.5 rounded-full bg-amber-50 text-amber-700">⭐ Quan trọng</span>}
            {search && <span className="px-2 py-0.5 rounded-full bg-slate-100">"{search}"</span>}
            <button onClick={clearFilters} className="text-indigo-600 hover:underline ml-1">Xoá bộ lọc</button>
          </div>
        )}

        {/* ── Content ── */}
        <div className="bg-white rounded-xl border border-slate-200 overflow-hidden">

          {isLoading && Array.from({ length: 8 }).map((_, i) => <SkeletonRow key={i} />)}

          {isError && (
            <div className="flex flex-col items-center justify-center py-16 text-center gap-3">
              <AlertCircle className="w-8 h-8 text-rose-400" />
              <p className="text-[13.5px] text-slate-500">Không thể tải dữ liệu.</p>
              <button onClick={() => refetch()} className="text-[13px] text-indigo-600 hover:underline">Thử lại</button>
            </div>
          )}

          {isEmpty && (
            <div className="flex flex-col items-center justify-center py-16 gap-3 px-6 text-center">
              <InboxIcon className="w-10 h-10 text-slate-300" />
              {hasActiveFilters ? (
                <>
                  <p className="text-[13.5px] text-slate-400">Không có mục nào khớp bộ lọc.</p>
                  <button onClick={clearFilters} className="text-[13px] text-indigo-600 hover:underline">Xoá bộ lọc</button>
                </>
              ) : selectedFolderId ? (
                <>
                  <p className="text-[13.5px] text-slate-500 font-medium">Thư mục này chưa có mục nào.</p>
                  <p className="text-[12.5px] text-slate-400 max-w-[360px]">
                    Mở <span className="font-medium text-slate-500">Tất cả mục</span> rồi kéo-thả item vào thư mục ở sidebar, hoặc dùng nút gán thư mục trên từng item.
                  </p>
                </>
              ) : (
                <>
                  <p className="text-[13.5px] text-slate-500 font-medium">Chưa có mục nào.</p>
                  <p className="text-[12.5px] text-slate-400 max-w-[360px]">
                    Kết nối Gmail / Calendar / Drive / Jira rồi bấm <span className="font-medium text-slate-500">Đồng bộ</span> để kéo dữ liệu về.
                  </p>
                </>
              )}
            </div>
          )}

          {showList && (
            <div className="flex items-center gap-3 px-4 py-2 border-b border-slate-100 bg-slate-50">
              <input
                type="checkbox"
                checked={items.length > 0 && selectedItemIds.size === items.length}
                onChange={toggleAllSelection}
                className="w-4 h-4 rounded border-slate-300 text-indigo-600 focus:ring-indigo-600"
              />
              <span className="text-[12.5px] font-medium text-slate-500">
                Chọn tất cả trang này
              </span>
            </div>
          )}

          {showList && items.map((item: ItemResponse) => {
            const v = rowVisual(item, selectedId === item.id, selectedItemIds.has(item.id));
            return (
            <div
              key={item.id}
              draggable
              onDragStart={(e) => handleDragStart(e, item.id)}
              onClick={() => setSelectedId(item.id)}
              className={`flex items-center gap-3 px-4 py-[13px] border-b border-slate-100 last:border-b-0 cursor-pointer transition-colors ${v.row}`}
            >
              <input
                type="checkbox"
                checked={selectedItemIds.has(item.id)}
                onClick={(e) => toggleSelection(item.id, e)}
                onChange={() => {}} // handled by onClick
                className="w-4 h-4 rounded border-slate-300 text-indigo-600 focus:ring-indigo-600 mr-1"
              />
              <div className={`w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 ${typeTileClass(item.type)}`}>
                {typeIcon(item.type)}
              </div>

              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-1.5 min-w-0">
                  {v.unread && (
                    <span className="w-2 h-2 rounded-full bg-blue-500 flex-shrink-0" aria-label="Chưa đọc" />
                  )}
                  <div className={`text-[13.5px] truncate leading-snug ${v.title}`}>
                    {item.title}
                  </div>
                </div>
                <div className={`text-[12.5px] truncate mt-0.5 leading-snug ${v.snippet}`}>
                  {item.snippet}
                </div>
                {item.folderIds && item.folderIds.length > 0 && (
                  <div className="flex items-center gap-1 mt-1.5 flex-wrap">
                    {item.folderIds.filter(fId => fId !== selectedFolderId).map((fId: string) => {
                      const f = folders.find(fol => fol.id === fId);
                      if (!f) return null;
                      return (
                        <span key={f.id} className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-medium border border-slate-100" style={{ backgroundColor: f.color ? `${f.color}15` : '#f1f5f9', color: f.color || '#475569' }}>
                          {f.name}
                        </span>
                      );
                    })}
                  </div>
                )}
              </div>

              <div className="flex flex-col items-end gap-1.5 flex-shrink-0">
                <span className={`text-[11.5px] ${v.time}`}>{formatTime(item.occurredAt)}</span>
                <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${statusChipClass(item)}`}>
                  <span className={`w-1.5 h-1.5 rounded-full ${statusDotClass(item)}`} />
                  {getItemStatusLabel(item)}
                </span>
              </div>

              <button
                onClick={e => {
                  e.stopPropagation();
                  toggleImportant({ id: item.id, isImportant: !item.isImportant });
                }}
                aria-label="Đánh dấu quan trọng"
                className="flex-shrink-0 p-1.5 rounded-md text-slate-300 hover:text-amber-400 hover:bg-amber-50 transition-colors"
              >
                <Star className={`w-4 h-4 ${item.isImportant ? 'fill-amber-400 text-amber-400' : ''}`} />
              </button>
            </div>
            );
          })}
        </div>

        {/* ── Pagination ── */}
        {showList && (
          <div className="flex items-center justify-between mt-4 flex-wrap gap-3">
            <div className="flex items-center gap-3">
              <PageSizeSelect value={limit} onChange={(n) => { setLimit(n); setPage(1); }} />
              <span className="text-[12.5px] text-slate-400">
                {rangeStart}–{rangeEnd} trong {total} mục
              </span>
            </div>

            {totalPages > 1 && (
            <div className="flex items-center gap-1">
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page === 1}
                aria-label="Trang trước"
                className="h-8 w-8 flex items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>

              {pageNumbers.map((p, i) =>
                p === '…' ? (
                  <span key={`ellipsis-${i}`} className="h-8 w-8 flex items-center justify-center text-[13px] text-slate-400">…</span>
                ) : (
                  <button
                    key={p}
                    onClick={() => setPage(p)}
                    className={`h-8 w-8 flex items-center justify-center rounded-lg text-[13px] font-medium border transition-colors
                      ${p === page
                        ? 'bg-indigo-600 text-white border-indigo-600'
                        : 'bg-white text-slate-700 border-slate-200 hover:bg-slate-50'
                      }`}
                  >
                    {p}
                  </button>
                )
              )}

              <button
                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                disabled={page === totalPages}
                aria-label="Trang sau"
                className="h-8 w-8 flex items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
            )}
          </div>
        )}
      </div>

      {/* ── Item Detail Drawer ── */}
      {selectedId && (
        <ItemDetail
          itemId={selectedId}
          onClose={() => setSelectedId(null)}
          onDeleted={() => setSelectedId(null)}
        />
      )}

      {/* ── Bulk Action Bar ── */}
      <BulkActionBar
        selectedItemIds={selectedItemIds}
        onClearSelection={() => setSelectedItemIds(new Set())}
      />
    </div>
  );
};
