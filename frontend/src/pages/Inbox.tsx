import { useState, useCallback, useRef, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { itemsApi, foldersApi } from '../lib/itemsApi';
import { useI18n } from '../hooks/useI18n';
import { handleApiError } from '../lib/errorUtils';
import { isItemUnread, getStatusLabel, isDraftEmail } from '../lib/itemMeta';
import { useSeenSet } from '../lib/seenStore';
import type { ItemType, ItemStatus, ItemResponse, PagedResult } from '../types/items';
import {
  Star, AlertCircle, Inbox as InboxIcon,
  ChevronLeft, ChevronRight, ChevronRight as BreadcrumbSeparator, Home,
  Send, FileEdit, Megaphone, Users, Bell, Mails, Loader2, ShieldAlert, Trash2,
  type LucideIcon,
} from 'lucide-react';
import { ItemDetail } from '../components/ItemDetail';
import { BulkActionBar } from '../components/BulkActionBar';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { WorkspaceToolbar } from '../components/workspace/WorkspaceToolbar';
import { typeIcon, typeLabelKey, typeSolidTileClass, parseSourceType } from '../lib/itemVisuals';
import type { TranslationKey } from '../i18n/translations';
import { PageSizeSelect } from '../components/PageSizeSelect';
import { TagChip, FolderChip } from '../components/tags/TagChip';
import { timeAgo } from '../lib/datetime';
import { usePollingInterval } from '../hooks/usePollingInterval';
import toast from 'react-hot-toast';
import { sendEmailApi } from '../lib/sendEmailApi';

// ─── helpers ────────────────────────────────────────────────────────────────

// Key i18n cho nhãn status (chip "đang lọc") — tái dùng nhãn cột Kanban.
const STATUS_LABEL_KEY: Record<ItemStatus, TranslationKey> = {
  Inbox: 'kanban.colInbox', Doing: 'kanban.colDoing', Done: 'kanban.colDone',
};

// ── Hộp thư kiểu Gmail (lọc theo Gmail label — dữ liệu đã có trong metadata.labels) ──
//  mailbox = null ⟺ KHÔNG ở chế độ email (ẩn nav, xem mọi loại item). Các value dưới đều là email.
//  'ALL' = tất cả thư (type=Email, trừ Spam/Trash) · còn lại = 1 Gmail label.
//  Giao dịch/Hoá đơn Gmail không expose qua API; 'Quan trọng' đã là filter sao (⭐).
type MailboxValue = string | null;
const MAILBOXES: { value: string; labelKey: TranslationKey; Icon: LucideIcon }[] = [
  { value: 'INBOX', labelKey: 'mailbox.inbox', Icon: InboxIcon },
  { value: 'STARRED', labelKey: 'mailbox.starred', Icon: Star },
  { value: 'SENT', labelKey: 'mailbox.sent', Icon: Send },
  { value: 'DRAFT', labelKey: 'mailbox.drafts', Icon: FileEdit },
  { value: 'ALL', labelKey: 'mailbox.allMail', Icon: Mails },
  { value: 'SPAM', labelKey: 'mailbox.spam', Icon: ShieldAlert },
  { value: 'TRASH', labelKey: 'mailbox.trash', Icon: Trash2 },
  { value: 'CATEGORY_PROMOTIONS', labelKey: 'mailbox.promotions', Icon: Megaphone },
  { value: 'CATEGORY_SOCIAL', labelKey: 'mailbox.social', Icon: Users },
  { value: 'CATEGORY_UPDATES', labelKey: 'mailbox.updates', Icon: Bell },
];

// Màu theo category Kanban (dùng cho Ticket & các loại khác ở Doing/Done): xám / xanh dương / xanh lá.
const CHIP_BY_STATUS: Record<string, string> = {
  Inbox: 'bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300',
  Doing: 'bg-blue-50 text-blue-700 dark:bg-blue-500/15 dark:text-blue-300',
  Done: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
};
const DOT_BY_STATUS: Record<string, string> = {
  Inbox: 'bg-slate-400',
  Doing: 'bg-blue-500',
  Done: 'bg-emerald-500',
};

// Cam = "chưa xem" (Email chưa đọc theo Gmail; Event/File/Note chưa mở trong app). `unread` do caller tính.
function statusChipClass(item: ItemResponse, unread: boolean): string {
  // Ticket (Jira): trung tính theo category — KHÔNG dùng cam "chưa xem" (status Jira tuỳ biến).
  if (item.type === 'Ticket') return CHIP_BY_STATUS[item.status] ?? 'bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300';
  if (item.status === 'Inbox' && unread) return 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300';
  return CHIP_BY_STATUS[item.status] ?? 'bg-slate-100 text-slate-500 dark:bg-slate-700 dark:text-slate-400';
}

function statusDotClass(item: ItemResponse, unread: boolean): string {
  if (item.type === 'Ticket') return DOT_BY_STATUS[item.status] ?? 'bg-slate-400';
  if (item.status === 'Inbox' && unread) return 'bg-amber-500';
  return DOT_BY_STATUS[item.status] ?? 'bg-slate-400';
}


/*
 * Style đã xem / chưa xem kiểu Gmail — áp cho MỌI loại (Email/Event/File/Note/Ticket):
 *  - Chưa xem: nền TRẮNG + tiêu đề đậm + chấm xanh + thời gian xanh đậm.
 *  - Đã xem:   nền xám nhạt + chữ thường, màu dịu.
 * `unread` = isItemUnread(item, seen) do caller truyền (Email = Gmail; còn lại = seenStore).
 * Riêng Ticket vẫn giữ status chip THÔ của Jira; highlight này là trục độc lập.
 */
function rowVisual(selected: boolean, checked: boolean, unread: boolean) {
  return {
    unread,
    row: selected
      ? 'bg-brand-50/60 dark:bg-brand-500/10'
      : checked
        ? 'bg-brand-50/40 dark:bg-brand-500/5'
        : unread
          ? 'bg-white hover:bg-slate-50 dark:bg-slate-900 dark:hover:bg-slate-800/60'
          : 'bg-slate-100/70 hover:bg-slate-100 dark:bg-slate-800/40 dark:hover:bg-slate-800/70',
    title: unread
      ? 'font-bold text-slate-900 dark:text-slate-100'
      : 'font-medium text-slate-600 dark:text-slate-400',
    snippet: unread ? 'text-slate-600 dark:text-slate-400' : 'text-slate-400 dark:text-slate-500',
    time: unread ? 'text-brand-600 dark:text-brand-400 font-semibold' : 'text-slate-400 dark:text-slate-500',
  };
}

// ─── skeleton row ────────────────────────────────────────────────────────────
function SkeletonRow() {
  return (
    <div className="flex items-center gap-3 px-4 py-[13px] border-b border-slate-100 dark:border-slate-800 animate-pulse">
      <div className="w-9 h-9 rounded-lg bg-slate-100 dark:bg-slate-800 flex-shrink-0" />
      <div className="flex-1 space-y-2">
        <div className="h-3.5 bg-slate-100 dark:bg-slate-800 rounded w-2/3" />
        <div className="h-3 bg-slate-100 dark:bg-slate-800 rounded w-full" />
      </div>
      <div className="flex-shrink-0 space-y-1.5 flex flex-col items-end">
        <div className="h-3 bg-slate-100 dark:bg-slate-800 rounded w-10" />
        <div className="h-5 bg-slate-100 dark:bg-slate-800 rounded-full w-20" />
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
  const location = useLocation();
  const { t, lang } = useI18n();
  const seenSet = useSeenSet();
  const [searchParams] = useSearchParams();
  const pollMs = usePollingInterval(45_000);

  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<ItemStatus[]>([]);
  const [typeFilter, setTypeFilter] = useState<ItemType[]>([]);
  const [mailbox, setMailbox] = useState<MailboxValue>('INBOX'); // default = Hộp thư đến (như Gmail)
  const [importantOnly, setImportantOnly] = useState(false);
  const [tagFilters, setTagFilters] = useState<string[]>([]);
  const [projectKeyFilter, setProjectKeyFilter] = useState<string>('');
  const [debouncedProjectKey, setDebouncedProjectKey] = useState<string>('');
  const [assigneeFilter, setAssigneeFilter] = useState<string>('');
  const [page, setPage] = useState(1);
  const [limit, setLimit] = useState(20);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  // Drive folder drill-down state
  const [driveFolderStack, setDriveFolderStack] = useState<{id: string, name: string, internalId: string}[]>([]);

  // Folder = CONTEXT của trang — DERIVE thẳng từ URL (không state+effect,
  // tránh render frame đầu bị null → header nháy "Tất cả mục" rồi mới hiện tên folder).
  const selectedFolderId = searchParams.get('folder');
  // Nguồn (integration) chọn ở sidebar — scope trang theo 1 loại. null = tab "Tất cả mục".
  const sourceType = parseSourceType(searchParams.get('type'));
  const itemFromUrl = searchParams.get('item');
  const activeItemId = itemFromUrl ?? selectedId;

  const closeItemDetail = () => {
    setSelectedId(null);
    if (itemFromUrl) {
      const next = new URLSearchParams(searchParams);
      next.delete('item');
      const q = next.toString();
      navigate({ pathname: location.pathname, search: q ? `?${q}` : '' }, { replace: true });
    }
  };

  const clickTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleItemClick = (item: ItemResponse, e: React.MouseEvent) => {
    let isFolder = false;
    if (item.type === 'File' && item.metadataJson) {
      try {
        isFolder = JSON.parse(item.metadataJson).isFolder === true;
      } catch { /* ignore */ }
    }

    if (isFolder) {
      if (e.detail === 1) {
        clickTimeoutRef.current = setTimeout(() => {
          setSelectedId(item.id);
          clickTimeoutRef.current = null;
        }, 200); // 200ms delay to distinguish double-click
      } else if (e.detail === 2) {
        if (clickTimeoutRef.current) {
          clearTimeout(clickTimeoutRef.current);
          clickTimeoutRef.current = null;
        }
        handleItemDoubleClick(item);
      }
    } else {
      // Normal items don't have double-click behavior, open instantly
      setSelectedId(item.id);
    }
  };

  const handleItemDoubleClick = (item: ItemResponse) => {
    if (item.type === 'File' && item.metadataJson) {
      try {
        const meta = JSON.parse(item.metadataJson);
        if (meta.isFolder && item.externalId) {
          setDriveFolderStack(prev => [...prev, { id: item.externalId!, name: item.title, internalId: item.id }]);
          setPage(1);
          setSelectedId(null);
          return;
        }
      } catch { /* ignore */ }
    }
  };

  // Multi-selection state
  const [selectedItemIds, setSelectedItemIds] = useState<Set<string>>(new Set());

  const discardDraftMutation = useMutation({
    mutationFn: (itemId: string) => sendEmailApi.discardDraft(itemId),
    onSuccess: () => {
      toast.success(t('sendEmail.draftDiscarded'));
      queryClient.invalidateQueries({ queryKey: ['items'] });
    },
    onError: (err) => handleApiError(err, t('sendEmail.discardFail')),
  });

  const [isEmptyMailboxPending, setIsEmptyMailboxPending] = useState(false);
  const [emptyMailboxConfirmOpen, setEmptyMailboxConfirmOpen] = useState(false);
  // Nháp đang chờ xác nhận hủy (null = đóng dialog).
  const [discardDraftTargetId, setDiscardDraftTargetId] = useState<string | null>(null);

  const handleEmptyMailbox = async () => {
    const isTrash = mailbox === 'TRASH';
    setEmptyMailboxConfirmOpen(false);
    setIsEmptyMailboxPending(true);
    try {
      // Dọn TOÀN BỘ mailbox (không chỉ trang hiện tại): xoá theo lô 100 (giới hạn limit BE)
      // cho tới khi hết. Guard: 1 lô không xoá được mục nào (toàn lỗi) → dừng, tránh lặp vô hạn.
      let deleted = 0;
      let failedTotal = 0;
      for (let guard = 0; guard < 200; guard++) {
        const batch = await itemsApi.getItems({ ...params, page: 1, limit: 100 });
        if (batch.items.length === 0) break;
        const results = await Promise.allSettled(batch.items.map((i) => itemsApi.deleteItem(i.id)));
        const failed = results.filter((r) => r.status === 'rejected').length;
        const ok = results.length - failed;
        deleted += ok;
        failedTotal += failed;
        if (ok === 0) break; // không tiến triển → dừng
      }

      if (deleted === 0 && failedTotal > 0) {
        toast.error(t('bulk.deleteFail'));
      } else if (failedTotal > 0) {
        toast.success(t('bulk.partialDelete'));
      } else {
        toast.success(isTrash ? t('bulk.emptiedTrash') : t('bulk.emptiedSpam'));
      }

      setSelectedItemIds(new Set());
      refetch();
    } catch (err) {
      console.error(err);
      toast.error(t('bulk.deleteFail'));
    } finally {
      setIsEmptyMailboxPending(false);
    }
  };

  // Đổi context (folder HOẶC nguồn) → về trang 1.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setPage(1);
    setDriveFolderStack([]); // Reset drive drill-down khi đổi tab
  }, [selectedFolderId, sourceType]);

  const toggleStatusFilter = (s: ItemStatus) => {
    setStatusFilter(prev => prev.includes(s) ? prev.filter(v => v !== s) : [...prev, s]);
    setPage(1);
  };
  // Chip loại — CHỈ dùng ở tab "Tất cả mục" (đa chọn như cũ). Trong 1 nguồn, chip loại bị ẩn.
  const toggleTypeFilter = (ty: ItemType) => {
    setTypeFilter(prev => prev.includes(ty) ? prev.filter(v => v !== ty) : [...prev, ty]);
    setPage(1);
  };

  // Chọn 1 hộp thư trong nav (chỉ hiện ở tab Email) → đổi Gmail label đang lọc.
  const selectMailbox = (value: MailboxValue) => {
    setMailbox(value);
    setPage(1);
  };

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleSearchChange = useCallback((val: string) => {
    setSearchInput(val);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setSearch(val.trim());
      setPage(1);
    }, 350);
  }, []);

  const projectKeyDebounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const handleProjectKeyChange = useCallback((val: string) => {
    setProjectKeyFilter(val);
    if (projectKeyDebounceRef.current) clearTimeout(projectKeyDebounceRef.current);
    projectKeyDebounceRef.current = setTimeout(() => {
      setDebouncedProjectKey(val.trim());
      setPage(1);
    }, 350);
  }, []);

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  // Scope theo nguồn (sidebar):
  //  · Email  → luôn types=['Email'] + lọc theo Gmail label ('ALL' = mọi thư, khác = 1 label).
  //  · Jira/Event/File → types=[loại đó].
  //  · null (tab Tất cả mục) → dùng chip loại đa chọn; KHÔNG label, KHÔNG project.
  const isEmailScope = sourceType === 'Email';
  const gmailLabel = isEmailScope && mailbox && mailbox !== 'ALL' ? mailbox : undefined;
  const effectiveTypes = sourceType ? [sourceType] : (typeFilter.length > 0 ? typeFilter : undefined);
  const effectiveProjectKey = sourceType === 'Ticket' ? (debouncedProjectKey || undefined) : undefined;
  const effectiveAssignee = sourceType === 'Ticket' ? (assigneeFilter || undefined) : undefined;

  const params = {
    statuses: statusFilter.length > 0 ? statusFilter : undefined,
    types: effectiveTypes,
    isImportant: importantOnly || undefined,
    search: search || undefined,
    folderId: selectedFolderId || undefined,
    tagIds: tagFilters.length > 0 ? tagFilters : undefined,
    projectKey: effectiveProjectKey,
    assignee: effectiveAssignee,
    gmailLabel,
    driveParentId: driveFolderStack.length > 0 ? driveFolderStack[driveFolderStack.length - 1].id : undefined,
    page,
    limit,
  };

  const queryKey = ['items', { statuses: params.statuses, types: params.types, isImportant: params.isImportant, search: params.search, folderId: params.folderId, tagIds: params.tagIds, projectKey: params.projectKey, assignee: params.assignee, gmailLabel: params.gmailLabel, driveParentId: params.driveParentId, page, limit }];

  // Khóa bộ lọc (không gồm page/limit) — so sánh total chỉ trong cùng context lọc, tránh invalidate
  // nhầm khi đổi chip Tất cả ↔ Email (total khác nhau vì lọc, không phải cron sync).
  const filterKey = JSON.stringify({
    statuses: params.statuses,
    types: params.types,
    isImportant: params.isImportant,
    search: params.search,
    folderId: params.folderId,
    tagIds: params.tagIds,
    projectKey: params.projectKey,
    assignee: params.assignee,
    gmailLabel: params.gmailLabel,
    driveParentId: params.driveParentId,
  });

  const { data, isLoading, isError, refetch, isFetching, isPlaceholderData } = useQuery({
    queryKey,
    queryFn: () => itemsApi.getItems(params),
    placeholderData: (prev) => prev,
    // Ghi đè staleTime global 5 phút — mỗi bộ lọc (Tất cả / Email / …) là queryKey riêng;
    // nếu không, quay lại "Tất cả" sẽ hiện cache cũ trong khi tab lọc Email vẫn poll được mail mới.
    staleTime: 0,
    refetchInterval: pollMs,
    refetchIntervalInBackground: true,
    refetchOnWindowFocus: true,
  });

  // Khi poll (cùng bộ lọc) phát hiện total đổi → refresh mọi query items (tab/bộ lọc khác).
  const prevTotalRef = useRef<{ filterKey: string; total: number } | null>(null);
  useEffect(() => {
    if (data?.total == null) return;
    const prev = prevTotalRef.current;
    if (prev?.filterKey === filterKey && prev.total !== data.total) {
      queryClient.invalidateQueries({ queryKey: ['items'] });
    }
    prevTotalRef.current = { filterKey, total: data.total };
  }, [data?.total, filterKey, queryClient]);

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
  }, [page, limit, statusFilter, typeFilter, importantOnly, tagFilters, search, selectedFolderId, mailbox]);

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

  const clearFilters = () => {
    setStatusFilter([]);
    setTypeFilter([]);
    setImportantOnly(false);
    setSearchInput('');
    setSearch('');
    setProjectKeyFilter('');
    setDebouncedProjectKey('');
    setAssigneeFilter('');
    setPage(1);
  };

  const currentFolder = selectedFolderId
    ? folders.find(f => f.id === selectedFolderId) ?? null
    : null;
  const hasActiveFilters = Boolean(statusFilter.length > 0 || (!sourceType && typeFilter.length > 0) || importantOnly || tagFilters.length > 0 || search || effectiveProjectKey);

  const isEmpty = !isLoading && !isError && items.length === 0;
  const showList = !isLoading && !isError && items.length > 0;

  const pageNumbers = buildPageNumbers(page, totalPages);

  return (
    <div className="flex-1 min-h-0 bg-slate-50 dark:bg-slate-950 overflow-y-auto">
      <div className="max-w-[1400px] mx-auto px-6 py-5">

        {/* ── Toolbar dùng chung với view Bảng — layout GIỐNG HỆT khi đổi view ── */}
        <WorkspaceToolbar
          view="list"
          folder={currentFolder}
          folderId={selectedFolderId}
          subtitle={isLoading ? t('common.loading') : t('inbox.count', { n: total })}
          isBackgroundFetching={isFetching && !isLoading}
          statusFilter={statusFilter}
          onToggleStatusFilter={toggleStatusFilter}
          typeFilter={typeFilter}
          onToggleTypeFilter={toggleTypeFilter}
          sourceType={sourceType}
          importantOnly={importantOnly}
          onImportantToggle={() => { setImportantOnly(v => !v); setPage(1); }}
          tagFilters={tagFilters}
          onToggleTagFilter={(id) => {
            setTagFilters(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]);
            setPage(1);
          }}
          onClearTagFilters={() => { setTagFilters([]); setPage(1); }}
          projectKeyFilter={projectKeyFilter}
          onProjectKeyChange={handleProjectKeyChange}
          assigneeFilter={assigneeFilter}
          onAssigneeChange={(v) => { setAssigneeFilter(v); setPage(1); }}
          searchInput={searchInput}
          onSearchChange={handleSearchChange}
          currentDriveFolderId={driveFolderStack.length > 0 ? driveFolderStack[driveFolderStack.length - 1].internalId : undefined}
        />

        {/* ── Hộp thư kiểu Gmail — CHỈ hiện khi đang ở tab Email (sidebar) ── */}
        {isEmailScope && (
        <div className="flex items-center gap-1.5 mb-4 overflow-x-auto pb-1 -mx-1 px-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
          {MAILBOXES.map(mb => {
            const active = mailbox === mb.value;
            const Icon = mb.Icon;
            return (
              <button
                key={mb.value}
                onClick={() => selectMailbox(mb.value)}
                className={`shrink-0 inline-flex items-center gap-1.5 h-8 px-3 rounded-full text-[12.5px] font-medium border transition-colors ${
                  active
                    ? 'bg-brand-600 text-white border-brand-600 shadow-sm'
                    : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50 hover:border-slate-300 dark:bg-slate-800 dark:text-slate-300 dark:border-slate-700 dark:hover:bg-slate-700'
                }`}
              >
                <Icon className={`w-3.5 h-3.5 ${active ? '' : 'text-slate-400 dark:text-slate-500'}`} strokeWidth={2.25} />
                {t(mb.labelKey)}
              </button>
            );
          })}
        </div>
        )}

        {isEmailScope && (mailbox === 'TRASH' || mailbox === 'SPAM') && items.length > 0 && (
          <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 px-4 py-3 mb-4 rounded-xl bg-rose-50 border border-rose-100 dark:bg-rose-950/20 dark:border-rose-900/30 text-rose-800 dark:text-rose-300 text-[13px] animate-in fade-in slide-in-from-top-2 duration-300">
            <div className="flex items-center gap-2 font-medium">
              <AlertCircle className="w-4 h-4 shrink-0 text-rose-500 dark:text-rose-400" />
              <span>{mailbox === 'TRASH' ? t('bulk.trashWarning') : t('bulk.spamWarning')}</span>
            </div>
            <button
              onClick={() => setEmptyMailboxConfirmOpen(true)}
              disabled={isEmptyMailboxPending}
              className="shrink-0 font-bold hover:underline text-rose-700 dark:text-rose-400 flex items-center gap-1 disabled:opacity-50"
            >
              {isEmptyMailboxPending ? t('common.updating') : (mailbox === 'TRASH' ? t('bulk.emptyTrashBtn') : t('bulk.emptySpamBtn'))}
            </button>
          </div>
        )}

        {/* ── Active filter summary (KHÔNG gồm folder — folder là context, hiển thị ở header) ── */}
        {hasActiveFilters && (
          <div className="flex items-center gap-2 mb-3 text-[12.5px] text-slate-500 dark:text-slate-400 flex-wrap">
            <span>{t('inbox.filtering')}</span>
            {statusFilter.map(s => (
              <span key={s} className="px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800">{t(STATUS_LABEL_KEY[s])}</span>
            ))}
            {!sourceType && typeFilter.map(ty => (
              <span key={ty} className="px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800">{t(typeLabelKey(ty))}</span>
            ))}
            {importantOnly && <span className="px-2 py-0.5 rounded-full bg-amber-50 text-amber-700 dark:bg-amber-500/10 dark:text-amber-400">⭐ {t('toolbar.important')}</span>}
            {search && <span className="px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-800">"{search}"</span>}
            {effectiveProjectKey && <span className="px-2 py-0.5 rounded-full bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-400">Project: {effectiveProjectKey}</span>}
            <button onClick={clearFilters} className="text-brand-600 dark:text-brand-400 hover:underline ml-1">{t('inbox.clearFilters')}</button>
          </div>
        )}

        {/* ── Drive Folder Breadcrumb ── */}
        {driveFolderStack.length > 0 && (
          <div className="flex items-center gap-1.5 mb-3 text-[13px] font-medium overflow-x-auto pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
            <button 
              onClick={() => { setDriveFolderStack([]); setPage(1); }}
              className="flex items-center gap-1.5 text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200 transition-colors"
            >
              <Home className="w-4 h-4" />
              {t('nav.allItems')}
            </button>
            {driveFolderStack.map((folder, index) => {
              const isLast = index === driveFolderStack.length - 1;
              return (
                <div key={folder.id} className="flex items-center gap-1.5 whitespace-nowrap">
                  <BreadcrumbSeparator className="w-4 h-4 text-slate-300 dark:text-slate-600 shrink-0" />
                  <button
                    onClick={() => {
                      if (!isLast) {
                        setDriveFolderStack(prev => prev.slice(0, index + 1));
                        setPage(1);
                      }
                    }}
                    className={`transition-colors ${isLast ? 'text-slate-900 dark:text-slate-100' : 'text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200'}`}
                    disabled={isLast}
                  >
                    {folder.name}
                  </button>
                </div>
              );
            })}
          </div>
        )}

        {/* ── Content ── */}
        <div className="relative">
        <div className={`bg-white dark:bg-slate-900 rounded-xl border border-slate-200 dark:border-slate-800 overflow-hidden transition-opacity ${isPlaceholderData ? 'opacity-50 pointer-events-none' : ''}`}>

          {isLoading && Array.from({ length: 8 }).map((_, i) => <SkeletonRow key={i} />)}

          {isError && (
            <div className="flex flex-col items-center justify-center py-16 text-center gap-3">
              <AlertCircle className="w-8 h-8 text-rose-400" />
              <p className="text-[13.5px] text-slate-500 dark:text-slate-400">{t('inbox.loadError')}</p>
              <button onClick={() => refetch()} className="text-[13px] text-brand-600 dark:text-brand-400 hover:underline">{t('common.retry')}</button>
            </div>
          )}

          {isEmpty && (
            <div className="flex flex-col items-center justify-center py-16 gap-3 px-6 text-center">
              <InboxIcon className="w-10 h-10 text-slate-300 dark:text-slate-600" />
              {hasActiveFilters ? (
                <>
                  <p className="text-[13.5px] text-slate-400 dark:text-slate-500">{t('inbox.emptyFiltered')}</p>
                  <button onClick={clearFilters} className="text-[13px] text-brand-600 dark:text-brand-400 hover:underline">{t('inbox.clearFilters')}</button>
                </>
              ) : selectedFolderId ? (
                <>
                  <p className="text-[13.5px] text-slate-500 dark:text-slate-300 font-medium">{t('inbox.emptyFolder')}</p>
                  <p className="text-[12.5px] text-slate-400 dark:text-slate-500 max-w-[360px]">
                    Mở <span className="font-medium text-slate-500 dark:text-slate-300">{t('nav.allItems')}</span> rồi kéo-thả item vào thư mục ở sidebar, hoặc dùng nút gán thư mục trên từng item.
                  </p>
                </>
              ) : (
                <>
                  <p className="text-[13.5px] text-slate-500 dark:text-slate-300 font-medium">{t('inbox.empty')}</p>
                  <p className="text-[12.5px] text-slate-400 dark:text-slate-500 max-w-[360px]">
                    Kết nối Gmail / Calendar / Drive / Jira rồi bấm <span className="font-medium text-slate-500 dark:text-slate-300">Đồng bộ</span> để kéo dữ liệu về.
                  </p>
                </>
              )}
            </div>
          )}

          {showList && (
            <div className="flex items-center gap-3 px-4 py-2 border-b border-slate-100 dark:border-slate-800 bg-slate-50 dark:bg-slate-800/50">
              <input
                type="checkbox"
                checked={items.length > 0 && selectedItemIds.size === items.length}
                onChange={toggleAllSelection}
                className="w-4 h-4 rounded border-slate-300 dark:border-slate-600 text-brand-600 focus:ring-brand-600"
              />
              <span className="text-[12.5px] font-medium text-slate-500 dark:text-slate-400">
                {t('inbox.selectAll')}
              </span>
            </div>
          )}

          {showList && items.map((item: ItemResponse) => {
            const unread = isItemUnread(item, seenSet);
            const v = rowVisual(activeItemId === item.id, selectedItemIds.has(item.id), unread);
            let isDriveFolder = false;
            if (item.type === 'File' && item.metadataJson) {
              try {
                isDriveFolder = JSON.parse(item.metadataJson).isFolder === true;
              } catch { /* ignore */ }
            }
            return (
            <div
              key={item.id}
              onClick={(e) => handleItemClick(item, e)}
              className={`group flex items-center gap-2.5 px-3 sm:px-4 py-2.5 border-b border-slate-100 dark:border-slate-800 last:border-b-0 cursor-pointer transition-colors ${v.row} select-none`}
            >
              {/* checkbox */}
              <input
                type="checkbox"
                checked={selectedItemIds.has(item.id)}
                onClick={(e) => toggleSelection(item.id, e)}
                onChange={() => {}} // handled by onClick
                className="w-4 h-4 shrink-0 rounded border-slate-300 dark:border-slate-600 text-brand-600 focus:ring-brand-600"
              />

              {/* sao (quan trọng) — đưa RA TRƯỚC như Gmail */}
              <button
                onClick={e => {
                  e.stopPropagation();
                  toggleImportant({ id: item.id, isImportant: !item.isImportant });
                }}
                aria-label={t('inbox.markImportant')}
                className="shrink-0 p-1 rounded-md hover:bg-amber-100/70 dark:hover:bg-amber-500/15 transition-colors"
              >
                <Star
                  className={`w-[18px] h-[18px] transition-colors ${
                    item.isImportant
                      ? 'fill-amber-400 text-amber-400'
                      : 'text-slate-300 dark:text-slate-600 group-hover:text-slate-400 dark:group-hover:text-slate-500'
                  }`}
                />
              </button>

              {/* avatar loại — logo brand thật trên nền trắng (Note = notepad vàng) */}
              <div className={`w-9 h-9 rounded-full flex items-center justify-center flex-shrink-0 shadow-sm ${typeSolidTileClass(item.type, isDriveFolder)}`}>
                {typeIcon(item.type, 'w-[22px] h-[22px]', undefined, isDriveFolder)}
              </div>

              <div className="flex-1 min-w-0">
                {/* 1 dòng: chấm chưa đọc · tiêu đề (đậm) · badge thread — em-dash · snippet (mờ, ngắn) */}
                <div className="flex items-center gap-1.5 min-w-0">
                  {v.unread && (
                    <span className="w-2 h-2 rounded-full bg-blue-500 flex-shrink-0" aria-label={t('inbox.unreadAria')} />
                  )}
                  <span className={`text-[13.5px] truncate max-w-[60%] shrink-0 leading-snug ${v.title}`}>
                    {item.title}
                  </span>
                  {(item.threadCount ?? 1) > 1 && (
                    <span
                      className="shrink-0 inline-flex items-center justify-center min-w-[18px] h-[18px] px-1 rounded-full bg-slate-200 dark:bg-slate-700 text-slate-600 dark:text-slate-300 text-[11px] font-semibold tabular-nums"
                      title={t('inbox.threadCount', { n: item.threadCount ?? 1 })}
                    >
                      {item.threadCount}
                    </span>
                  )}
                  {item.snippet && (
                    <span className={`text-[12.5px] truncate min-w-0 flex-1 leading-snug ${v.snippet}`}>
                      <span className="text-slate-300 dark:text-slate-600 mr-1">—</span>{item.snippet}
                    </span>
                  )}
                </div>
                {((item.folderIds && item.folderIds.length > 0) || (item.tags && item.tags.length > 0)) && (
                  <div className="flex items-center gap-1 mt-1.5 flex-wrap">
                    {item.folderIds.map((fId: string) => {
                      const f = folders.find(fol => fol.id === fId);
                      if (!f) return null;
                      return <FolderChip key={f.id} name={f.name} color={f.color || '#94a3b8'} size="sm" />;
                    })}
                    {item.tags?.map((tg) => (
                      <TagChip key={tg.id} name={tg.name} color={tg.color} size="sm" />
                    ))}
                  </div>
                )}
              </div>

              <div className="flex flex-col items-end gap-1 flex-shrink-0">
                <span className={`text-[11.5px] whitespace-nowrap ${v.time}`}>{timeAgo(item.occurredAt, lang)}</span>
                <div className="flex items-center gap-2">
                  {isDraftEmail(item) && (
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        setDiscardDraftTargetId(item.id);
                      }}
                      disabled={discardDraftMutation.isPending}
                      className="opacity-0 group-hover:opacity-100 transition-opacity p-1 rounded-md text-red-500 hover:bg-red-50 dark:hover:bg-red-950/20"
                      title={t('sendEmail.discardDraft')}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  )}
                  <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${statusChipClass(item, unread)}`}>
                    <span className={`w-1.5 h-1.5 rounded-full ${statusDotClass(item, unread)}`} />
                    {getStatusLabel(item, t, unread)}
                  </span>
                </div>
              </div>
            </div>
            );
          })}
        </div>

          {/* Overlay khi đang tải bộ lọc/trang mới (data cũ vẫn hiện mờ để đỡ nháy) */}
          {isPlaceholderData && (
            <div className="absolute inset-0 flex items-start justify-center pt-16 pointer-events-none">
              <span className="inline-flex items-center gap-2 px-3.5 py-2 rounded-full bg-white/95 dark:bg-slate-800/95 shadow-lg ring-1 ring-slate-200 dark:ring-slate-700 text-[12.5px] font-medium text-slate-600 dark:text-slate-300">
                <Loader2 className="w-4 h-4 animate-spin text-brand-600 dark:text-brand-400" />
                {t('common.loading')}
              </span>
            </div>
          )}
        </div>

        {/* ── Pagination ── */}
        {showList && (
          <div className="flex items-center justify-between mt-4 flex-wrap gap-3">
            <div className="flex items-center gap-3">
              <PageSizeSelect value={limit} onChange={(n) => { setLimit(n); setPage(1); }} />
              <span className="text-[12.5px] text-slate-400 dark:text-slate-500">
                {t('inbox.range', { start: rangeStart, end: rangeEnd, total })}
              </span>
            </div>

            {totalPages > 1 && (
            <div className="flex items-center gap-1">
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page === 1}
                aria-label={t('common.prevPage')}
                className="h-8 w-8 flex items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>

              {pageNumbers.map((p, i) =>
                p === '…' ? (
                  <span key={`ellipsis-${i}`} className="h-8 w-8 flex items-center justify-center text-[13px] text-slate-400 dark:text-slate-500">…</span>
                ) : (
                  <button
                    key={p}
                    onClick={() => setPage(p)}
                    className={`h-8 w-8 flex items-center justify-center rounded-lg text-[13px] font-medium border transition-colors
                      ${p === page
                        ? 'bg-brand-600 text-white border-brand-600'
                        : 'bg-white text-slate-700 border-slate-200 hover:bg-slate-50 dark:bg-slate-800 dark:text-slate-300 dark:border-slate-700 dark:hover:bg-slate-700'
                      }`}
                  >
                    {p}
                  </button>
                )
              )}

              <button
                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                disabled={page === totalPages}
                aria-label={t('common.nextPage')}
                className="h-8 w-8 flex items-center justify-center rounded-lg border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
            )}
          </div>
        )}
      </div>

      {/* ── Item Detail Drawer ── */}
      {activeItemId && (
        <ItemDetail
          itemId={activeItemId}
          onClose={closeItemDetail}
          onDeleted={closeItemDetail}
        />
      )}

      {/* ── Bulk Action Bar ── */}
      <BulkActionBar
        selectedItemIds={selectedItemIds}
        onClearSelection={() => setSelectedItemIds(new Set())}
        mailbox={mailbox || undefined}
      />

      {/* Xác nhận dọn sạch Thùng rác / Thư rác */}
      <ConfirmDialog
        open={emptyMailboxConfirmOpen}
        tone="danger"
        message={mailbox === 'TRASH' ? t('bulk.emptyTrashConfirm') : t('bulk.emptySpamConfirm')}
        confirmLabel={mailbox === 'TRASH' ? t('bulk.emptyTrashBtn') : t('bulk.emptySpamBtn')}
        loading={isEmptyMailboxPending}
        onConfirm={handleEmptyMailbox}
        onCancel={() => setEmptyMailboxConfirmOpen(false)}
      />

      {/* Xác nhận hủy thư nháp (nút thùng rác trên dòng nháp) */}
      <ConfirmDialog
        open={discardDraftTargetId !== null}
        tone="danger"
        message={t('sendEmail.discardConfirm')}
        confirmLabel={t('sendEmail.discardDraft')}
        loading={discardDraftMutation.isPending}
        onConfirm={() => {
          if (discardDraftTargetId) {
            discardDraftMutation.mutate(discardDraftTargetId, {
              onSettled: () => setDiscardDraftTargetId(null),
            });
          }
        }}
        onCancel={() => setDiscardDraftTargetId(null)}
      />
    </div>
  );
};
