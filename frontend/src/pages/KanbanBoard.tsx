import React, { useState, useRef, useCallback, useEffect } from 'react';
import { useQuery, useInfiniteQuery, useMutation, useQueryClient, type InfiniteData } from '@tanstack/react-query';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { itemsApi, foldersApi } from '../lib/itemsApi';
import { connectionsApi } from '../lib/connectionsApi';
import { ItemDetail } from '../components/ItemDetail';
import { BulkActionBar } from '../components/BulkActionBar';
import { WorkspaceToolbar } from '../components/workspace/WorkspaceToolbar';
import { typeIcon, typeLabelKey, parseSourceType } from '../lib/itemVisuals';
import { TagChip, FolderChip } from '../components/tags/TagChip';
import { isItemUnread, isDraftEmail, isDriveFolder } from '../lib/itemMeta';
import { useSeenSet } from '../lib/seenStore';
import type { ItemStatus, ItemType, FolderResponse, ItemResponse, PagedResult } from '../types/items';
import { Plus, Star, GripVertical, AlertCircle, Plug } from 'lucide-react';
import toast from 'react-hot-toast';
import { handleApiError } from '../lib/errorUtils';
import { useI18n } from '../hooks/useI18n';
import type { TranslationKey } from '../i18n/translations';
import { timeAgo } from '../lib/datetime';
import { usePollingInterval } from '../hooks/usePollingInterval';


function typeTileClass(t: ItemType): string {
  const map: Record<ItemType, string> = {
    Email: 'bg-blue-50 text-blue-700 dark:bg-blue-500/15 dark:text-blue-300',
    Event: 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
    File: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
    Note: 'bg-slate-100 text-slate-600 dark:bg-slate-700 dark:text-slate-300',
    Ticket: 'bg-violet-50 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300',
  };
  return map[t] ?? 'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300';
}

const COLUMNS: { titleKey: TranslationKey, status: ItemStatus, dotColor: string }[] = [
  { titleKey: 'kanban.colInbox', status: 'Inbox', dotColor: 'bg-amber-500' },
  { titleKey: 'kanban.colDoing', status: 'Doing', dotColor: 'bg-blue-500' },
  { titleKey: 'kanban.colDone', status: 'Done', dotColor: 'bg-emerald-500' },
];

/** Số thẻ load mỗi lần cho 1 cột — bấm "Tải thêm" ở đáy cột để lấy tiếp (không còn cap 100). */
const COL_PAGE_SIZE = 30;

/** Nguồn (tab sidebar) → service cần Active để có dữ liệu (empty-state "chưa kết nối"). */
const SOURCE_SERVICE: Record<string, { key: string; label: string }> = {
  Email: { key: 'gmail', label: 'Gmail' },
  Event: { key: 'gcal', label: 'Google Calendar' },
  File: { key: 'drive', label: 'Google Drive' },
  Ticket: { key: 'jira', label: 'Jira' },
};

export const KanbanBoard = () => {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { t, lang } = useI18n();
  const seenSet = useSeenSet();
  const [searchParams] = useSearchParams();
  const pollMs = usePollingInterval(45_000);

  // Folder = CONTEXT của trang — DERIVE thẳng từ URL (không state+effect, hết nháy header khi đổi view)
  const selectedFolderId = searchParams.get('folder');
  // Nguồn (integration) chọn ở sidebar — scope bảng theo 1 loại. null = tab "Tất cả mục".
  const sourceType = parseSourceType(searchParams.get('type'));
  const [selectedItemId, setSelectedItemId] = useState<string | null>(null);

  const [search, setSearch] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [typeFilter, setTypeFilter] = useState<ItemType[]>([]);
  // Ở Bảng, statusFilter = lọc CỘT hiển thị (đa chọn — không chọn gì = hiện đủ 3 cột)
  const [statusFilter, setStatusFilter] = useState<ItemStatus[]>([]);
  const [importantOnly, setImportantOnly] = useState(false);
  const [tagFilters, setTagFilters] = useState<string[]>([]);
  const [driveKind, setDriveKind] = useState<'all' | 'folder' | 'file'>('all');
  const [projectKeyFilter, setProjectKeyFilter] = useState<string>('');
  const [debouncedProjectKey, setDebouncedProjectKey] = useState<string>('');
  const [assigneeFilter, setAssigneeFilter] = useState<string>('');
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const [selectedItemIds, setSelectedItemIds] = useState<Set<string>>(new Set());
  const [addingFolderItemId, setAddingFolderItemId] = useState<string | null>(null);

  // Đóng dropdown "Thêm vào thư mục" trên card khi click ra ngoài / nhấn Esc.
  useEffect(() => {
    if (!addingFolderItemId) return;
    const onDocClick = (e: MouseEvent) => {
      if (!(e.target as HTMLElement).closest('[data-folder-menu]')) setAddingFolderItemId(null);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setAddingFolderItemId(null); };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [addingFolderItemId]);

  const [dragOverCol, setDragOverCol] = useState<ItemStatus | null>(null);
  const [draggingId, setDraggingId] = useState<string | null>(null);

  const toggleStatusFilter = (s: ItemStatus) => {
    setStatusFilter(prev => prev.includes(s) ? prev.filter(v => v !== s) : [...prev, s]);
  };
  const toggleTypeFilter = (ty: ItemType) => {
    setTypeFilter(prev => prev.includes(ty) ? prev.filter(v => v !== ty) : [...prev, ty]);
  };

  const handleSearchChange = useCallback((val: string) => {
    setSearchInput(val);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setSearch(val.trim());
    }, 350);
  }, []);

  const projectKeyDebounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const handleProjectKeyChange = useCallback((val: string) => {
    setProjectKeyFilter(val);
    if (projectKeyDebounceRef.current) clearTimeout(projectKeyDebounceRef.current);
    projectKeyDebounceRef.current = setTimeout(() => {
      setDebouncedProjectKey(val.trim());
    }, 350);
  }, []);

  const { data: folders = [] } = useQuery({
    queryKey: ['folders'],
    queryFn: () => foldersApi.getFolders()
  });

  /*
   * Bỏ giới hạn "100 item đầu tiên": mỗi cột là MỘT infinite query riêng theo status
   * (kiểu Jira/Trello) — load COL_PAGE_SIZE thẻ đầu, bấm "Tải thêm" ở đáy cột để lấy tiếp.
   * Số trên header cột = TỔNG THẬT từ server (total của envelope), không phải số đã load.
   */
  // Scope theo nguồn: có tab ⟹ khoá 1 loại (bỏ qua chip loại); tab Jira mới áp project.
  const effectiveTypes = sourceType ? [sourceType] : (typeFilter.length > 0 ? typeFilter : undefined);
  const effectiveProjectKey = sourceType === 'Ticket' ? (debouncedProjectKey || undefined) : undefined;
  const effectiveAssignee = sourceType === 'Ticket' ? (assigneeFilter || undefined) : undefined;
  // Lọc folder/file chỉ áp ở tab Drive (Bảng không có drill-down folder nên chỉ cần sourceType File).
  const effectiveDriveKind = sourceType === 'File' && driveKind !== 'all' ? driveKind : undefined;

  const boardKey = (status: ItemStatus) =>
    ['items', 'board', { status, folderId: selectedFolderId, source: sourceType, type: typeFilter, isImportant: importantOnly, tagIds: tagFilters, projectKey: effectiveProjectKey, assignee: effectiveAssignee, search, driveKind: effectiveDriveKind }];

  const makeColQuery = (status: ItemStatus) => ({
    queryKey: boardKey(status),
    queryFn: ({ pageParam }: { pageParam: number }) => itemsApi.getItems({
      statuses: [status],
      folderId: selectedFolderId || undefined,
      types: effectiveTypes,
      isImportant: importantOnly || undefined,
      tagIds: tagFilters.length > 0 ? tagFilters : undefined,
      projectKey: effectiveProjectKey,
      assignee: effectiveAssignee,
      search: search || undefined,
      driveKind: effectiveDriveKind,
      page: pageParam,
      limit: COL_PAGE_SIZE,
    }),
    initialPageParam: 1,
    getNextPageParam: (lastPage: PagedResult<ItemResponse>, allPages: PagedResult<ItemResponse>[]) => {
      const loaded = allPages.reduce((n, p) => n + p.items.length, 0);
      return loaded < lastPage.total ? allPages.length + 1 : undefined;
    },
    staleTime: 0,
    refetchInterval: pollMs,
    refetchIntervalInBackground: true,
    refetchOnWindowFocus: true,
  });

  // 3 cột cố định → gọi hook tường minh (không được gọi hook trong vòng lặp)
  const inboxQ = useInfiniteQuery(makeColQuery('Inbox'));
  const doingQ = useInfiniteQuery(makeColQuery('Doing'));
  const doneQ = useInfiniteQuery(makeColQuery('Done'));
  const colQueries: Record<ItemStatus, typeof inboxQ> = { Inbox: inboxQ, Doing: doingQ, Done: doneQ };

  const colItemsOf = (q: typeof inboxQ): ItemResponse[] => q.data?.pages.flatMap(p => p.items) ?? [];
  const colTotalOf = (q: typeof inboxQ): number => q.data?.pages.at(-1)?.total ?? 0;

  const items: ItemResponse[] = COLUMNS.flatMap(c => colItemsOf(colQueries[c.status]));
  const isColLoading = inboxQ.isLoading || doingQ.isLoading || doneQ.isLoading;
  const isBackgroundFetching =
    (inboxQ.isFetching || doingQ.isFetching || doneQ.isFetching) && !isColLoading;
  const isError = inboxQ.isError || doingQ.isError || doneQ.isError;
  const refetchAll = () => { inboxQ.refetch(); doingQ.refetch(); doneQ.refetch(); };

  // Empty-state thông minh (giống Danh sách): board rỗng + chưa kết nối service của nguồn đang xem
  // → hiện panel dẫn sang trang Kết nối thay vì 3 cột trống khó hiểu.
  const { data: connectionsList = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
    staleTime: 60_000,
  });
  const activeServices = new Set(
    connectionsList.filter(c => c.status.toLowerCase() === 'active').map(c => c.serviceType.toLowerCase()),
  );
  const requiredService = sourceType ? SOURCE_SERVICE[sourceType] : undefined;
  const missingConnection = requiredService ? !activeServices.has(requiredService.key) : activeServices.size === 0;
  const boardTotal = colTotalOf(inboxQ) + colTotalOf(doingQ) + colTotalOf(doneQ);
  const boardEmpty = !isColLoading && !isError && boardTotal === 0;

  type BoardCache = InfiniteData<PagedResult<ItemResponse>>;

  const updateStatus = useMutation({
    mutationFn: ({ id, status, type }: { id: string, status: ItemStatus, type: ItemType }) => {
      if (type === 'Ticket') {
        const transitionMap: Record<ItemStatus, string> = {
          Inbox: 'To Do',
          Doing: 'In Progress',
          Done: 'Done'
        };
        return itemsApi.patchItem(id, { statusTransition: transitionMap[status] });
      }
      return itemsApi.updateItemStatus(id, { status });
    },
    // Optimistic move giữa 2 cache cột: gỡ khỏi cột nguồn, chèn đầu cột đích; lỗi → khôi phục snapshot
    onMutate: async ({ id, status: toStatus }) => {
      const statuses: ItemStatus[] = ['Inbox', 'Doing', 'Done'];
      await Promise.all(statuses.map(st => queryClient.cancelQueries({ queryKey: boardKey(st) })));

      const snapshots = new Map<ItemStatus, BoardCache | undefined>();
      let moved: ItemResponse | undefined;

      for (const st of statuses) {
        const data = queryClient.getQueryData<BoardCache>(boardKey(st));
        snapshots.set(st, data);
        if (!data || st === toStatus) continue;
        const found = data.pages.flatMap(p => p.items).find(it => it.id === id);
        if (found) {
          moved = found;
          queryClient.setQueryData<BoardCache>(boardKey(st), {
            ...data,
            pages: data.pages.map(p => ({
              ...p,
              total: Math.max(0, p.total - 1),
              items: p.items.filter(it => it.id !== id),
            })),
          });
        }
      }

      if (moved) {
        const target = queryClient.getQueryData<BoardCache>(boardKey(toStatus));
        if (target && target.pages.length > 0) {
          queryClient.setQueryData<BoardCache>(boardKey(toStatus), {
            ...target,
            pages: target.pages.map((p, i) => ({
              ...p,
              total: p.total + 1,
              items: i === 0 ? [{ ...moved!, status: toStatus }, ...p.items] : p.items,
            })),
          });
        }
      }

      return { snapshots };
    },
    onError: (err, _vars, ctx) => {
      ctx?.snapshots.forEach((data, st) => {
        if (data) queryClient.setQueryData(boardKey(st), data);
      });
      handleApiError(err, 'Lỗi cập nhật trạng thái', { navigate });
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
    }
  });

  const addToFolderMutation = useMutation({
    mutationFn: ({ folderId, itemId }: { folderId: string, itemId: string }) => foldersApi.addItemToFolder(folderId, { itemId }),
    onSuccess: (_, variables) => {
      toast.success(t('item.addedToFolder'));
      queryClient.invalidateQueries({ queryKey: ['items'] });
      queryClient.invalidateQueries({ queryKey: ['item', variables.itemId] });
    },
    onError: (err) => handleApiError(err, 'Lỗi thêm vào thư mục', { navigate })
  });

  // Đánh dấu quan trọng ngay trên thẻ (như view Danh sách). Optimistic trên cache từng cột board.
  const toggleImportant = useMutation({
    mutationFn: ({ id, isImportant }: { id: string; isImportant: boolean }) =>
      itemsApi.updateItemImportant(id, isImportant),
    onMutate: async ({ id, isImportant }) => {
      await queryClient.cancelQueries({ queryKey: ['items'] });
      const snapshots = queryClient.getQueriesData<BoardCache>({ queryKey: ['items', 'board'] });
      snapshots.forEach(([key, data]) => {
        if (!data) return;
        queryClient.setQueryData<BoardCache>(key, {
          ...data,
          pages: data.pages.map(p => ({
            ...p,
            items: p.items.map(it => it.id === id ? { ...it, isImportant } : it),
          })),
        });
      });
      return { snapshots };
    },
    onError: (err, _vars, ctx) => {
      ctx?.snapshots.forEach(([key, data]) => queryClient.setQueryData(key, data));
      handleApiError(err, t('item.saveFail'), { navigate });
    },
  });

  const handleDragStart = (e: React.DragEvent, id: string) => {
    setDraggingId(id);
    if (selectedItemIds.has(id) && selectedItemIds.size > 1) {
      e.dataTransfer.setData('itemIds', JSON.stringify(Array.from(selectedItemIds)));
    } else {
      e.dataTransfer.setData('itemId', id);
    }
  };

  const handleDragEnd = () => {
    setDraggingId(null);
    setDragOverCol(null);
  };

  const handleDragOver = (e: React.DragEvent, status: ItemStatus) => {
    e.preventDefault();
    if (dragOverCol !== status) setDragOverCol(status);
  };

  const handleDragLeave = () => {
    setDragOverCol(null);
  };

  const handleDrop = (e: React.DragEvent, status: ItemStatus) => {
    e.preventDefault();
    setDragOverCol(null);
    setDraggingId(null);
    const itemId = e.dataTransfer.getData('itemId');
    const itemIds = e.dataTransfer.getData('itemIds');

    if (itemIds) {
      const ids = JSON.parse(itemIds);
      ids.forEach((id: string) => {
        const item = items.find((i: ItemResponse) => i.id === id);
        if (item) updateStatus.mutate({ id, status, type: item.type });
      });
    } else if (itemId) {
      const draggedItem = items.find((i: ItemResponse) => i.id === itemId);
      if (draggedItem) {
        updateStatus.mutate({ id: itemId, status, type: draggedItem.type });
      }
    }
  };

  const currentFolder = selectedFolderId
    ? folders.find((f: FolderResponse) => f.id === selectedFolderId) ?? null
    : null;

  return (
    <div className="h-full flex flex-col min-w-0 overflow-hidden bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-100">
      <div className="max-w-[1400px] mx-auto px-6 py-5 w-full flex flex-col flex-1 min-h-0">

        {/* ── Toolbar dùng chung với view Danh sách — layout GIỐNG HỆT khi đổi view ── */}
        <WorkspaceToolbar
          view="board"
          folder={currentFolder}
          folderId={selectedFolderId}
          subtitle={t('kanban.subtitle')}
          isBackgroundFetching={isBackgroundFetching}
          statusFilter={statusFilter}
          onToggleStatusFilter={toggleStatusFilter}
          typeFilter={typeFilter}
          onToggleTypeFilter={toggleTypeFilter}
          sourceType={sourceType}
          importantOnly={importantOnly}
          onImportantToggle={() => setImportantOnly(v => !v)}
          tagFilters={tagFilters}
          onToggleTagFilter={(id) =>
            setTagFilters(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id])}
          onClearTagFilters={() => setTagFilters([])}
          projectKeyFilter={projectKeyFilter}
          onProjectKeyChange={handleProjectKeyChange}
          assigneeFilter={assigneeFilter}
          onAssigneeChange={setAssigneeFilter}
          searchInput={searchInput}
          onSearchChange={handleSearchChange}
          driveKind={driveKind}
          onDriveKindChange={setDriveKind}
        />

        {/* Board content */}
        <div className="flex-1 overflow-x-auto overflow-y-hidden min-h-0">
          {isError ? (
            <div className="h-full flex flex-col items-center justify-center border-2 border-dashed border-slate-200 dark:border-slate-700 rounded-xl bg-white dark:bg-slate-900 p-8 text-center max-w-md mx-auto">
              <div className="w-12 h-12 rounded-xl bg-red-50 dark:bg-red-500/10 text-red-500 flex items-center justify-center mb-4">
                <AlertCircle className="w-6 h-6" />
              </div>
              <span className="text-slate-900 dark:text-slate-100 text-sm font-semibold mb-1">{t('kanban.loadError')}</span>
              <p className="text-[13px] text-slate-500 dark:text-slate-400 mb-4">{t('kanban.loadErrorHint')}</p>
              <button onClick={refetchAll} className="px-4 py-2 bg-brand-600 hover:bg-brand-700 text-white text-[13px] font-medium rounded-lg">
                {t('common.retry')}
              </button>
            </div>
          ) : boardEmpty && missingConnection ? (
            <div className="h-full flex flex-col items-center justify-center border-2 border-dashed border-slate-200 dark:border-slate-700 rounded-xl bg-white dark:bg-slate-900 p-8 text-center max-w-md mx-auto">
              <div className="w-12 h-12 rounded-xl bg-brand-50 dark:bg-brand-500/10 text-brand-600 dark:text-brand-400 flex items-center justify-center mb-4">
                <Plug className="w-6 h-6" />
              </div>
              <span className="text-slate-900 dark:text-slate-100 text-sm font-semibold mb-1">
                {requiredService
                  ? t('inbox.emptyNoConnectionSource', { service: requiredService.label })
                  : t('inbox.emptyNoConnectionAny')}
              </span>
              <button
                onClick={() => navigate('/integrations')}
                className="mt-4 inline-flex items-center gap-1.5 px-4 py-2 rounded-lg bg-brand-600 text-[13px] font-semibold text-white hover:bg-brand-700 transition-colors"
              >
                <Plug className="w-4 h-4" />
                {t('nav.integrations')}
              </button>
            </div>
          ) : (
            <div className={`flex h-full gap-5 ${statusFilter.length > 0 ? '' : 'min-w-[900px]'}`}>
              {COLUMNS.filter(col => statusFilter.length === 0 || statusFilter.includes(col.status)).map(col => {
                const q = colQueries[col.status];
                const colItems = colItemsOf(q);
                const colTotal = colTotalOf(q);
                const isColLoading = q.isLoading;
                const isOver = dragOverCol === col.status;
                return (
                  <div key={col.status} className="flex-1 w-80 flex flex-col min-h-0">
                    <div className="flex items-center gap-2 px-1 mb-2">
                      <span className={`w-2 h-2 rounded-full ${col.dotColor}`}></span>
                      <span className="text-[14px] font-semibold text-slate-900 dark:text-slate-100">{t(col.titleKey)}</span>
                      <span className="text-[12px] font-semibold text-slate-500 bg-slate-200 dark:bg-slate-700 dark:text-slate-300 px-2 py-0.5 rounded-full">
                        {isColLoading ? '…' : colTotal}
                      </span>
                    </div>

                    <div
                      onDragOver={(e) => handleDragOver(e, col.status)}
                      onDragLeave={handleDragLeave}
                      onDrop={(e) => handleDrop(e, col.status)}
                      className={`flex-1 flex flex-col gap-2.5 p-2.5 rounded-xl min-h-[160px] overflow-y-auto transition-colors border-2 ${
                        isOver ? 'bg-brand-50 border-brand-400 border-dashed dark:bg-brand-500/10 dark:border-brand-500' : 'bg-slate-100/80 border-transparent dark:bg-slate-900/60'
                      }`}
                    >
                      {isColLoading ? (
                        Array.from({ length: 3 }).map((_, i) => (
                          <div key={i} className="bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl p-3 animate-pulse">
                            <div className="h-4 bg-slate-200 dark:bg-slate-700 rounded w-1/4 mb-3"></div>
                            <div className="h-3 bg-slate-200 dark:bg-slate-700 rounded w-3/4 mb-2"></div>
                            <div className="h-3 bg-slate-200 dark:bg-slate-700 rounded w-1/2"></div>
                          </div>
                        ))
                      ) : (
                        colItems.map(item => {
                          // Đã xem / chưa xem kiểu Gmail — đồng bộ view Danh sách. Email = Gmail;
                          // Event/File/Note/Ticket = seenStore (mở detail = đã xem).
                          const unread = isItemUnread(item, seenSet);
                          const seenDim = !unread;
                          const cardIsFolder = isDriveFolder(item);

                          return (
                          <div
                            key={item.id}
                            draggable={!(updateStatus.isPending && updateStatus.variables?.id === item.id)}
                            onDragStart={(e) => handleDragStart(e, item.id)}
                            onDragEnd={handleDragEnd}
                            onClick={() => {
                               if (isDraftEmail(item)) {
                                 navigate(`/send-email?draftItemId=${item.id}`);
                               } else {
                                 setSelectedItemId(item.id);
                               }
                             }}
                            className={`shrink-0 border rounded-xl p-3 cursor-pointer group hover:shadow-md hover:border-slate-300 dark:hover:border-slate-600 transition-all relative overflow-hidden ${
                              draggingId === item.id ? 'opacity-40 shadow-none' : 'opacity-100 shadow-sm'
                            } ${
                              selectedItemIds.has(item.id)
                                ? 'bg-brand-50/40 border-brand-200 dark:bg-brand-500/10 dark:border-brand-500/40'
                                : seenDim
                                  ? 'bg-slate-50 border-slate-200 dark:bg-slate-800/50 dark:border-slate-700'
                                  : 'bg-white border-slate-200 dark:bg-slate-800 dark:border-slate-700'
                            }`}
                          >
                            <div className="flex justify-between items-start gap-2 mb-2">
                              <div className="flex items-center gap-1.5 flex-wrap min-w-0">
                                <input
                                  type="checkbox"
                                  checked={selectedItemIds.has(item.id)}
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    const newSet = new Set(selectedItemIds);
                                    if (newSet.has(item.id)) newSet.delete(item.id);
                                    else newSet.add(item.id);
                                    setSelectedItemIds(newSet);
                                  }}
                                  onChange={() => {}}
                                  className={`w-3.5 h-3.5 shrink-0 rounded border-slate-300 dark:border-slate-600 text-brand-600 focus:ring-brand-600 ${selectedItemIds.has(item.id) ? 'inline-block' : 'hidden group-hover:inline-block'}`}
                                />
                                <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-[11px] font-medium border border-transparent ${typeTileClass(item.type)}`}>
                                  {typeIcon(item.type, 'w-3.5 h-3.5', undefined, cardIsFolder)}
                                  {cardIsFolder ? t('type.folder') : t(typeLabelKey(item.type))}
                                </span>
                                {item.folderIds?.map(fId => {
                                  const f = folders.find(fol => fol.id === fId);
                                  if (!f) return null;
                                  return <FolderChip key={f.id} name={f.name} color={f.color || '#94a3b8'} size="sm" />;
                                })}

                                <div className="relative" data-folder-menu onClick={e => e.stopPropagation()}>
                                  <button
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      setAddingFolderItemId(addingFolderItemId === item.id ? null : item.id);
                                    }}
                                    className="inline-flex items-center justify-center gap-0.5 px-1.5 py-0.5 rounded text-[10px] font-medium border border-slate-200 bg-white text-slate-500 hover:text-slate-700 hover:bg-slate-50 dark:border-slate-600 dark:bg-slate-700 dark:text-slate-300 dark:hover:bg-slate-600 transition-colors opacity-0 group-hover:opacity-100"
                                    title={t('item.addToFolder')}
                                  >
                                    <Plus className="w-2.5 h-2.5" />
                                    <span>{t('item.add')}</span>
                                  </button>

                                  {addingFolderItemId === item.id && (
                                    <div className="absolute top-full left-0 mt-1 w-44 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 shadow-xl rounded-md py-1 z-[60] animate-in fade-in zoom-in-95 duration-100">
                                      {folders.filter((f: FolderResponse) => !item.folderIds?.includes(f.id)).length === 0 ? (
                                        <div className="px-3 py-1.5 text-[11px] text-slate-500 dark:text-slate-400 text-center">{t('item.noMoreFolders')}</div>
                                      ) : (
                                        folders.filter((f: FolderResponse) => !item.folderIds?.includes(f.id)).map((f: FolderResponse) => (
                                          <button
                                            key={f.id}
                                            onClick={(e) => {
                                              e.stopPropagation();
                                              addToFolderMutation.mutate({ folderId: f.id, itemId: item.id });
                                              setAddingFolderItemId(null);
                                            }}
                                            disabled={addToFolderMutation.isPending}
                                            className="w-full text-left px-3 py-1.5 text-[11.5px] font-medium text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 flex items-center gap-2 transition-colors"
                                          >
                                            <span className="w-1.5 h-1.5 rounded-full shrink-0" style={{ backgroundColor: f.color || '#f59e0b' }}></span>
                                            <span className="truncate">{f.name}</span>
                                          </button>
                                        ))
                                      )}
                                    </div>
                                  )}
                                </div>
                              </div>
                              <div className="flex items-center gap-1.5 shrink-0">
                                {unread && (
                                  <span className="inline-flex items-center gap-1 text-[10.5px] font-semibold text-blue-600 dark:text-blue-400" aria-label={t('status.unread')}>
                                    <span className="w-2 h-2 rounded-full bg-blue-500" />
                                    {t('status.unread')}
                                  </span>
                                )}
                                <span className="text-slate-300 cursor-grab active:cursor-grabbing hover:text-slate-400">
                                  <GripVertical className="w-4 h-4" />
                                </span>
                              </div>
                            </div>
                            <h4 className={`text-[13.5px] leading-snug mb-2 line-clamp-2 ${
                              unread ? 'font-bold text-slate-900 dark:text-slate-100' : 'font-medium text-slate-600 dark:text-slate-400'
                            }`}>
                              {item.title}
                            </h4>
                            {item.tags?.length > 0 && (
                              <div className="flex items-center gap-1 flex-wrap mb-2">
                                {item.tags.map((tg) => (
                                  <TagChip key={tg.id} name={tg.name} color={tg.color} size="sm" />
                                ))}
                              </div>
                            )}
                            <div className="flex items-center justify-end gap-1.5 mt-auto pt-1">
                              <button
                                type="button"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  toggleImportant.mutate({ id: item.id, isImportant: !item.isImportant });
                                }}
                                aria-label={t('inbox.markImportant')}
                                className="shrink-0 p-0.5 rounded hover:bg-amber-100/70 dark:hover:bg-amber-500/15 transition-colors"
                              >
                                <Star className={`w-3.5 h-3.5 transition-colors ${
                                  item.isImportant
                                    ? 'fill-amber-400 text-amber-400'
                                    : 'text-slate-300 dark:text-slate-600 hover:text-amber-400'
                                }`} />
                              </button>
                              <span className="text-[12px] text-slate-400 dark:text-slate-500 shrink-0">
                                {timeAgo(item.occurredAt, lang)}
                              </span>
                            </div>
                          </div>
                          );
                        })
                      )}

                      {!isColLoading && colItems.length === 0 && (
                        <div className="flex items-center justify-center p-4 border-[1.5px] border-dashed border-slate-300 dark:border-slate-700 rounded-xl text-[12.5px] text-slate-400 dark:text-slate-500 text-center h-20">
                          {t('kanban.dropHere')}
                        </div>
                      )}

                      {/* Tải thêm — thay cho cap 100 item cũ */}
                      {q.hasNextPage && (
                        <button
                          onClick={() => q.fetchNextPage()}
                          disabled={q.isFetchingNextPage}
                          className="shrink-0 w-full py-2 rounded-lg border border-slate-200 bg-white text-[12.5px] font-medium text-slate-600 hover:bg-slate-50 hover:text-slate-900 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700 transition-colors disabled:opacity-50"
                        >
                          {q.isFetchingNextPage
                            ? t('kanban.loadingMore')
                            : `${t('kanban.loadMore')} (${colItems.length}/${colTotal})`}
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Item Detail — render TRỰC TIẾP như Inbox (ItemDetail tự có overlay fixed);
          bọc thêm wrapper trắng 600px sẽ tạo panel trắng thừa sau drawer */}
      {selectedItemId && (
        <ItemDetail
          itemId={selectedItemId}
          onClose={() => setSelectedItemId(null)}
          onDeleted={() => setSelectedItemId(null)}
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
