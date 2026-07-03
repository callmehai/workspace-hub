import { useState, useCallback, useRef, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useLocation } from 'react-router-dom';
import { itemsApi, foldersApi } from '../lib/itemsApi';
import { handleApiError } from '../lib/errorUtils';
import type { ItemType, ItemStatus } from '../types/items';
import {
  Mail, Calendar, FileText, StickyNote, Briefcase,
  Star, AlertCircle, Inbox as InboxIcon,
  ChevronLeft, ChevronRight, Search, LayoutGrid, List,
} from 'lucide-react';
import { ItemDetail } from '../components/ItemDetail';
import { formatDistanceToNow } from 'date-fns';
import { vi } from 'date-fns/locale';

const LIMIT = 20;

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

function typeIcon(t: ItemType) {
  const cls = 'w-4 h-4';
  switch (t) {
    case 'Email':  return <Mail className={cls} />;
    case 'Event':  return <Calendar className={cls} />;
    case 'File':   return <FileText className={cls} />;
    case 'Note':   return <StickyNote className={cls} />;
    case 'Ticket': return <Briefcase className={cls} />;
  }
}

function typeTileClass(t: ItemType): string {
  const map: Record<ItemType, string> = {
    Email:  'bg-blue-50 text-blue-600',
    Event:  'bg-amber-50 text-amber-600',
    File:   'bg-emerald-50 text-emerald-600',
    Note:   'bg-slate-100 text-slate-500',
    Ticket: 'bg-purple-50 text-purple-600',
  };
  return map[t] ?? 'bg-slate-100 text-slate-500';
}

function statusChipClass(s: ItemStatus): string {
  const map: Record<ItemStatus, string> = {
    Inbox: 'bg-slate-100 text-slate-600',
    Doing: 'bg-blue-50 text-blue-700',
    Done:  'bg-emerald-50 text-emerald-700',
  };
  return map[s] ?? 'bg-slate-100 text-slate-500';
}

function statusDotClass(s: ItemStatus): string {
  const map: Record<ItemStatus, string> = {
    Inbox: 'bg-slate-400',
    Doing: 'bg-blue-500',
    Done:  'bg-emerald-500',
  };
  return map[s] ?? 'bg-slate-400';
}

function statusLabel(s: ItemStatus): string {
  const map: Record<ItemStatus, string> = {
    Inbox: 'Cần xem', Doing: 'Đang xử lý', Done: 'Done',
  };
  return map[s] ?? s;
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

// ─── filter chip ─────────────────────────────────────────────────────────────
interface ChipProps {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}
function Chip({ active, onClick, children }: ChipProps) {
  return (
    <button
      onClick={onClick}
      className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-[13px] font-medium border transition-colors whitespace-nowrap
        ${active
          ? 'bg-indigo-50 text-indigo-700 border-indigo-200'
          : 'bg-white text-slate-600 border-slate-200 hover:border-slate-300'
        }`}
    >
      {children}
    </button>
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
  const navigate = useNavigate();
  const location = useLocation();

  const getInitialType = (): ItemType | null => {
    if (location.pathname === '/files') return 'File';
    if (location.pathname === '/calendar') return 'Event';
    if (location.pathname === '/tasks') return 'Ticket';
    return null;
  };

  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<ItemStatus | null>(null);
  const [typeFilter, setTypeFilter] = useState<ItemType | null>(getInitialType);
  const [importantOnly, setImportantOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const [selectedFolderId, setSelectedFolderId] = useState<string | null>(null);

  useEffect(() => {
    const params = new URLSearchParams(location.search);
    const folder = params.get('folder');
    setSelectedFolderId(folder || null);

    if (location.pathname === '/files') {
      setTypeFilter('File');
    } else if (location.pathname === '/calendar') {
      setTypeFilter('Event');
    } else if (location.pathname === '/tasks') {
      setTypeFilter('Ticket');
    } else {
      setTypeFilter(null);
    }
    setPage(1);
  }, [location.pathname, location.search]);

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
    limit: LIMIT,
  };

  const queryKey = ['items', { status: params.status, type: params.type, isImportant: params.isImportant, search: params.search, folderId: params.folderId, page, limit: LIMIT }];

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey,
    queryFn: () => itemsApi.getItems(params),
    placeholderData: (prev) => prev,
  });

  const queryClient = useQueryClient();
  const { mutate: toggleImportant } = useMutation({
    mutationFn: ({ id, isImportant }: { id: string; isImportant: boolean }) =>
      itemsApi.updateItemImportant(id, isImportant),
    onMutate: async ({ id, isImportant }) => {
      await queryClient.cancelQueries({ queryKey: ['items'] });
      const previous = queryClient.getQueryData(queryKey);
      queryClient.setQueryData(queryKey, (old: any) => {
        if (!old) return old;
        return { ...old, items: old.items.map((it: any) => it.id === id ? { ...it, isImportant } : it) };
      });
      return { previous };
    },
    onError: (err, _vars, ctx: any) => {
      if (ctx?.previous) queryClient.setQueryData(queryKey, ctx.previous);
      handleApiError(err, 'Lỗi đánh dấu quan trọng', { navigate });
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['items'] }),
  });

  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / LIMIT));
  const rangeStart = total === 0 ? 0 : (page - 1) * LIMIT + 1;
  const rangeEnd = Math.min(page * LIMIT, total);

  const clearFilters = () => {
    setStatusFilter(null);
    setTypeFilter(null);
    setImportantOnly(false);
    setSearchInput('');
    setSearch('');
    setPage(1);
  };

  const handleRemoveFolderFilter = () => {
    const params = new URLSearchParams(location.search);
    params.delete('folder');
    navigate(`${location.pathname}?${params.toString()}`);
  };

  const isEmpty = !isLoading && !isError && items.length === 0;
  const showList = !isLoading && !isError && items.length > 0;

  const STATUS_FILTERS: { label: string; value: ItemStatus | null }[] = [
    { label: 'Tất cả', value: null },
    { label: 'Cần xem', value: 'Inbox' },
    { label: 'Đang xử lý', value: 'Doing' },
    { label: 'Done', value: 'Done' },
  ];
  const TYPE_FILTERS: { label: string; value: ItemType | null }[] = [
    { label: 'Tất cả', value: null },
    { label: 'Email', value: 'Email' },
    { label: 'Sự kiện', value: 'Event' },
    { label: 'Tệp', value: 'File' },
    { label: 'Ghi chú', value: 'Note' },
    { label: 'Ticket', value: 'Ticket' },
  ];

  const pageNumbers = buildPageNumbers(page, totalPages);

  return (
    <div className="flex-1 min-h-0 bg-slate-50 overflow-y-auto">
      <div className="max-w-[1120px] mx-auto px-6 py-5">

        {/* ── Page header ── */}
        <div className="flex items-end justify-between gap-3 mb-4 flex-wrap">
          <div>
            <h1 className="text-[22px] font-semibold text-slate-900 leading-tight m-0">Inbox</h1>
            <p className="text-[13px] text-slate-500 mt-0.5">
              {isLoading ? 'Đang tải…' : `${total} mục`}
            </p>
          </div>

          {/* View switcher */}
          <div className="flex items-center gap-1 p-[3px] bg-white border border-slate-200 rounded-[9px]">
            <button className="flex items-center gap-1.5 px-[11px] py-1.5 rounded-[7px] bg-indigo-50 text-indigo-700 text-[13px] font-semibold">
              <List className="w-4 h-4" />
              <span>Danh sách</span>
            </button>
            <button 
              onClick={() => navigate('/kanban')}
              className="flex items-center gap-1.5 px-[11px] py-1.5 rounded-[7px] text-slate-500 text-[13px] font-medium hover:bg-slate-50"
            >
              <LayoutGrid className="w-4 h-4" />
              <span>Bảng</span>
            </button>
          </div>
        </div>

        {/* ── Filter bar ── */}
        <div className="flex flex-wrap gap-2 items-center mb-4">
          {STATUS_FILTERS.map(f => (
            <Chip key={String(f.value)} active={statusFilter === f.value} onClick={() => { setStatusFilter(f.value); setPage(1); }}>
              {f.label}
            </Chip>
          ))}

          <div className="w-px h-[22px] bg-slate-200 mx-0.5" />

          {TYPE_FILTERS.map(f => (
            <Chip key={String(f.value)} active={typeFilter === f.value} onClick={() => { setTypeFilter(f.value); setPage(1); }}>
              {f.value ? (
                <span className={`inline-flex items-center gap-1 ${typeTileClass(f.value)} px-0 bg-transparent`}>
                  {typeIcon(f.value)}{f.label}
                </span>
              ) : f.label}
            </Chip>
          ))}

          <div className="w-px h-[22px] bg-slate-200 mx-0.5" />

          <Chip active={importantOnly} onClick={() => { setImportantOnly(v => !v); setPage(1); }}>
            <Star className={`w-3.5 h-3.5 ${importantOnly ? 'fill-amber-400 text-amber-400' : 'text-slate-400'}`} />
            Quan trọng
          </Chip>
        </div>

        {/* ── Search ── */}
        <div className="relative mb-4">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
          <input
            type="text"
            value={searchInput}
            onChange={e => handleSearchChange(e.target.value)}
            placeholder="Tìm kiếm tiêu đề, nội dung…"
            className="w-full h-9 pl-9 pr-4 rounded-lg border border-slate-200 bg-white text-[13.5px] text-slate-900 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500/30 focus:border-indigo-400 transition"
          />
        </div>

        {/* ── Active filter summary ── */}
        {(statusFilter || typeFilter || importantOnly || search || selectedFolderId) && (
          <div className="flex items-center gap-2 mb-3 text-[12.5px] text-slate-500 flex-wrap">
            <span>Đang lọc:</span>
            {selectedFolderId && (
              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-indigo-50 text-indigo-700">
                Thư mục: {folders.find(f => f.id === selectedFolderId)?.name || 'Ẩn'}
                <button onClick={handleRemoveFolderFilter} className="hover:text-indigo-900 font-bold ml-1">×</button>
              </span>
            )}
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
            <div className="flex flex-col items-center justify-center py-16 gap-3">
              <InboxIcon className="w-10 h-10 text-slate-300" />
              <p className="text-[13.5px] text-slate-400">Không có mục nào.</p>
              {(statusFilter || typeFilter || importantOnly || search) && (
                <button onClick={clearFilters} className="text-[13px] text-indigo-600 hover:underline">Xoá bộ lọc</button>
              )}
            </div>
          )}

          {showList && items.map((item) => (
            <div
              key={item.id}
              onClick={() => setSelectedId(item.id)}
              className={`flex items-center gap-3 px-4 py-[13px] border-b border-slate-100 last:border-b-0 cursor-pointer transition-colors hover:bg-slate-50 ${selectedId === item.id ? 'bg-indigo-50/50' : ''}`}
            >
              <div className={`w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 ${typeTileClass(item.type)}`}>
                {typeIcon(item.type)}
              </div>

              <div className="flex-1 min-w-0">
                <div className="text-[13.5px] font-semibold text-slate-900 truncate leading-snug">
                  {item.title}
                </div>
                <div className="text-[12.5px] text-slate-500 truncate mt-0.5 leading-snug">
                  {item.snippet}
                </div>
                {item.folderIds && item.folderIds.length > 0 && (
                  <div className="flex items-center gap-1 mt-1.5 flex-wrap">
                    {item.folderIds.map(fId => {
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
                <span className="text-[11.5px] text-slate-400">{formatTime(item.occurredAt)}</span>
                <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${statusChipClass(item.status)}`}>
                  <span className={`w-1.5 h-1.5 rounded-full ${statusDotClass(item.status)}`} />
                  {statusLabel(item.status)}
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
          ))}
        </div>

        {/* ── Pagination ── */}
        {showList && totalPages > 1 && (
          <div className="flex items-center justify-between mt-4">
            <span className="text-[12.5px] text-slate-400">
              Hiển thị {rangeStart}–{rangeEnd} trong {total} mục
            </span>

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
    </div>
  );
};
