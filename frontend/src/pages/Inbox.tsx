import { useState, useCallback, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { itemsApi } from '../lib/itemsApi';
import type { ItemType, ItemStatus } from '../types/items';
import {
  Mail, Calendar, FileText, StickyNote,
  Star, AlertCircle, Inbox as InboxIcon,
  ChevronLeft, ChevronRight, Search, LayoutGrid, List,
} from 'lucide-react';

const LIMIT = 20;

// ─── helpers ────────────────────────────────────────────────────────────────

function formatTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffDays = Math.floor(diffMs / 86400000);
  if (diffDays === 0) return d.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
  if (diffDays === 1) return 'Hôm qua';
  if (diffDays < 7) return d.toLocaleDateString('vi-VN', { weekday: 'short' });
  return d.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' });
}

function typeLabel(t: ItemType): string {
  const map: Record<ItemType, string> = {
    Email: 'Email', Event: 'Sự kiện', File: 'Tệp', Note: 'Ghi chú',
  };
  return map[t] ?? t;
}

function typeIcon(t: ItemType) {
  const cls = 'w-4 h-4';
  switch (t) {
    case 'Email': return <Mail className={cls} />;
    case 'Event': return <Calendar className={cls} />;
    case 'File': return <FileText className={cls} />;
    case 'Note': return <StickyNote className={cls} />;
  }
}

function typeTileClass(t: ItemType): string {
  const map: Record<ItemType, string> = {
    Email: 'bg-blue-50 text-blue-600',
    Event: 'bg-amber-50 text-amber-600',
    File: 'bg-emerald-50 text-emerald-600',
    Note: 'bg-slate-100 text-slate-500',
  };
  return map[t] ?? 'bg-slate-100 text-slate-500';
}

function statusChipClass(s: ItemStatus): string {
  const map: Record<ItemStatus, string> = {
    Inbox: 'bg-slate-100 text-slate-600',
    Doing: 'bg-blue-50 text-blue-700',
    Done: 'bg-emerald-50 text-emerald-700',
  };
  return map[s] ?? 'bg-slate-100 text-slate-500';
}

function statusDotClass(s: ItemStatus): string {
  const map: Record<ItemStatus, string> = {
    Inbox: 'bg-slate-400',
    Doing: 'bg-blue-500',
    Done: 'bg-emerald-500',
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
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<ItemStatus | null>(null);
  const [typeFilter, setTypeFilter] = useState<ItemType | null>(null);
  const [importantOnly, setImportantOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleSearchChange = useCallback((val: string) => {
    setSearchInput(val);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setSearch(val.trim());
      setPage(1);
    }, 350);
  }, []);

  const params = {
    status: statusFilter ?? undefined,
    type: typeFilter ?? undefined,
    isImportant: importantOnly || undefined,
    search: search || undefined,
    page,
    limit: LIMIT,
  };

  const queryKey = ['items', { status: params.status, type: params.type, isImportant: params.isImportant, search: params.search, page, limit: LIMIT }];

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey,
    queryFn: () => itemsApi.getItems(params),
    placeholderData: (prev) => prev,
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
            <button className="flex items-center gap-1.5 px-[11px] py-1.5 rounded-[7px] text-slate-500 text-[13px] font-medium hover:bg-slate-50">
              <LayoutGrid className="w-4 h-4" />
              <span>Bảng</span>
            </button>
          </div>
        </div>

        {/* ── Filter bar ── */}
        <div className="flex flex-wrap gap-2 items-center mb-4">
          {/* Status chips */}
          {STATUS_FILTERS.map(f => (
            <Chip key={String(f.value)} active={statusFilter === f.value} onClick={() => { setStatusFilter(f.value); setPage(1); }}>
              {f.label}
            </Chip>
          ))}

          <div className="w-px h-[22px] bg-slate-200 mx-0.5" />

          {/* Type chips */}
          {TYPE_FILTERS.map(f => (
            <Chip key={String(f.value)} active={typeFilter === f.value} onClick={() => { setTypeFilter(f.value); setPage(1); }}>
              {f.value ? (
                <span className={`inline-flex items-center gap-1 ${typeTileClass(f.value)} px-0 bg-transparent`}>
                  {typeIcon(f.value)}{f.label}
                </span>
              ) : f.label}
            </Chip>
          ))}

          {/* Important */}
          <Chip active={importantOnly} onClick={() => { setImportantOnly(v => !v); setPage(1); }}>
            <Star className={`w-3.5 h-3.5 ${importantOnly ? 'fill-indigo-600 text-indigo-600' : 'text-slate-400'}`} />
            <span>Quan trọng</span>
          </Chip>

          {/* Search */}
          <div className="relative ml-auto min-w-[200px] max-w-xs">
            <span className="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-400 flex pointer-events-none">
              <Search className="w-4 h-4" />
            </span>
            <input
              id="inbox-search"
              type="text"
              value={searchInput}
              onChange={e => handleSearchChange(e.target.value)}
              placeholder="Tìm kiếm…"
              className="w-full h-9 pl-8 pr-3 text-[13px] border border-slate-200 rounded-lg bg-white text-slate-900 placeholder:text-slate-400 outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-500/20 transition"
            />
          </div>
        </div>

        {/* ── List card ── */}
        <div className="bg-white border border-slate-200 rounded-xl overflow-hidden shadow-[0_1px_3px_0_rgb(0,0,0,.05)]">

          {/* Loading skeleton */}
          {isLoading && (
            <>
              {Array.from({ length: 6 }).map((_, i) => <SkeletonRow key={i} />)}
            </>
          )}

          {/* Error */}
          {isError && (
            <div className="py-[52px] px-6 text-center">
              <div className="w-12 h-12 rounded-xl bg-red-50 text-red-500 flex items-center justify-center mx-auto mb-3.5">
                <AlertCircle className="w-6 h-6" />
              </div>
              <div className="text-[15px] font-semibold text-slate-900 mb-1">Không tải được dữ liệu</div>
              <div className="text-[13px] text-slate-500 mb-[18px]">Mất kết nối tới máy chủ. Vui lòng thử lại.</div>
              <button
                onClick={() => refetch()}
                className="h-[38px] px-[18px] border-none rounded-lg bg-indigo-600 text-white text-[13px] font-semibold cursor-pointer hover:bg-indigo-700 transition-colors"
              >
                Thử lại
              </button>
            </div>
          )}

          {/* Empty */}
          {isEmpty && (
            <div className="py-[52px] px-6 text-center">
              <div className="w-12 h-12 rounded-xl bg-slate-100 text-slate-400 flex items-center justify-center mx-auto mb-3.5">
                <InboxIcon className="w-6 h-6" />
              </div>
              <div className="text-[15px] font-semibold text-slate-900 mb-1">Không có mục nào</div>
              <div className="text-[13px] text-slate-500 mb-[18px]">Thử bỏ bớt bộ lọc, hoặc kết nối thêm dịch vụ để kéo dữ liệu về.</div>
              <button
                onClick={clearFilters}
                className="h-[38px] px-[18px] border border-slate-300 rounded-lg bg-white text-slate-800 text-[13px] font-semibold cursor-pointer hover:bg-slate-50 transition-colors"
              >
                Xoá bộ lọc
              </button>
            </div>
          )}

          {/* Item rows */}
          {showList && items.map((item, idx) => {
            const isActive = item.id === selectedId;
            return (
              <div
                key={item.id}
                onClick={() => setSelectedId(prev => prev === item.id ? null : item.id)}
                className={`flex items-center gap-[13px] px-4 py-[13px] border-b border-slate-100 cursor-pointer transition-colors duration-100 last:border-b-0
                  ${isActive ? 'bg-indigo-50/60' : 'hover:bg-slate-50'}
                  ${idx === items.length - 1 ? '' : ''}`}
              >
                {/* Type icon tile */}
                <div className={`w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 ${typeTileClass(item.type)}`}>
                  {typeIcon(item.type)}
                </div>

                {/* Main content */}
                <div className="min-w-0 flex-1">
                  {/* Title row */}
                  <div className="flex items-center gap-1.5">
                    {item.isImportant && (
                      <span className="w-1.5 h-1.5 rounded-full bg-indigo-500 flex-shrink-0" />
                    )}
                    <span className={`text-[14px] truncate ${item.isImportant ? 'font-semibold text-slate-900' : 'font-medium text-slate-800'}`}>
                      {item.title}
                    </span>
                  </div>
                  {/* from · snippet */}
                  <div className="flex items-center gap-1.5 mt-[3px] min-w-0">
                    <span className="text-[13px] text-slate-500 whitespace-nowrap flex-shrink-0 max-w-[42%] overflow-hidden text-ellipsis">
                      {typeLabel(item.type)}
                    </span>
                    <span className="text-slate-300 flex-shrink-0">·</span>
                    <span className="text-[13px] text-slate-400 whitespace-nowrap overflow-hidden text-ellipsis min-w-0">
                      {item.snippet}
                    </span>
                  </div>
                </div>

                {/* Right meta */}
                <div className="flex flex-col items-end gap-1.5 flex-shrink-0">
                  <span className="text-[12px] text-slate-400 whitespace-nowrap">
                    {formatTime(item.occurredAt)}
                  </span>
                  {/* Status chip */}
                  <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${statusChipClass(item.status)}`}>
                    <span className={`w-1.5 h-1.5 rounded-full flex-shrink-0 ${statusDotClass(item.status)}`} />
                    {statusLabel(item.status)}
                  </span>
                </div>

                {/* Star button */}
                <button
                  onClick={e => { e.stopPropagation(); }}
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
          <div className="flex items-center justify-between mt-3.5 flex-wrap gap-2.5">
            <span className="text-[13px] text-slate-500">
              Hiển thị {rangeStart}–{rangeEnd} trong {total} mục
            </span>

            <div className="flex items-center gap-1">
              {/* Prev */}
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page === 1}
                aria-label="Trang trước"
                className="h-8 w-8 flex items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>

              {/* Page numbers */}
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

              {/* Next */}
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
    </div>
  );
};
