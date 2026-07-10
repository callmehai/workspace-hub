import { useState, useMemo, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueries, useQueryClient } from '@tanstack/react-query';
import {
  Star, Search, RefreshCw, Plus, Tag, Settings2,
  FolderPlus, Briefcase, UserRound, Loader2, ChevronDown, Check,
} from 'lucide-react';
import { Select } from '../Select';
import toast from 'react-hot-toast';
import { connectionsApi, type ConnectionDto } from '../../lib/connectionsApi';
import { jiraApi, type JiraProject } from '../../lib/jiraApi';
import { itemsApi } from '../../lib/itemsApi';
import { tagsApi } from '../../lib/tagsApi';
import { useI18n } from '../../hooks/useI18n';
import { handleApiError } from '../../lib/errorUtils';
import { TYPE_FILTERS, STATUS_FILTERS, typeIcon, integrationLabelKey } from '../../lib/itemVisuals';
import type { ItemType, ItemStatus, FolderResponse, TagResponse } from '../../types/items';
import { CreateNoteModal } from './CreateNoteModal';
import { CreateEventModal } from './CreateEventModal';
import { CreateTicketModal } from '../jira/CreateTicketModal';
import { TagManagerModal } from '../tags/TagManagerModal';
import { CreateDriveFolderModal } from '../drive/CreateDriveFolderModal';
import { WorkspaceViewSwitcher, type WorkspaceView } from './WorkspaceViewSwitcher';

/*
 * Toolbar dùng chung cho các view chính của workspace.
 * MỤC TIÊU: đổi view KHÔNG làm mất context folder/source:
 *   Hàng 1: context + actions · Hàng 2: chips (trạng thái + loại + quan trọng)
 *   Hàng 3: search full-width.
 * Ở Bảng, chip Trạng thái = lọc CỘT hiển thị (chọn "Đang xử lý" → chỉ hiện cột đó).
 */

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
          ? 'bg-brand-50 text-brand-700 border-brand-200 dark:bg-brand-500/15 dark:text-brand-300 dark:border-brand-500/30'
          : 'bg-white text-slate-600 border-slate-200 hover:border-slate-300 dark:bg-slate-800 dark:text-slate-300 dark:border-slate-700 dark:hover:border-slate-600'
        }`}
    >
      {children}
    </button>
  );
}

/** Nhóm filter có nhãn nhỏ ở trước (chia tầng cho gọn). */
function FilterGroup({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-2">
      <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500 shrink-0 select-none">
        {label}
      </span>
      <div className="flex flex-wrap items-center gap-1.5">{children}</div>
    </div>
  );
}

/** Lọc theo tag dạng dropdown checklist ĐA CHỌN — gom mọi tag vào 1 nút, không rải chip tràn hàng.
 *  Chọn nhiều tag = lọc OR (item khớp nếu mang bất kỳ tag nào đã chọn). */
function TagFilterDropdown({
  tags, tagFilters, onToggleTagFilter, onClearTagFilters, onManage,
}: {
  tags: TagResponse[];
  tagFilters: string[];
  onToggleTagFilter: (id: string) => void;
  onClearTagFilters: () => void;
  onManage: () => void;
}) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [open]);

  const selectedTags = tags.filter(x => tagFilters.includes(x.id));
  const hasSelection = selectedTags.length > 0;

  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen(o => !o)}
        title={t('tag.filterTitle')}
        className={`inline-flex items-center gap-1.5 pl-3 pr-2 py-1.5 rounded-full text-[13px] font-medium border transition-colors whitespace-nowrap
          ${hasSelection
            ? 'bg-brand-50 text-brand-700 border-brand-200 dark:bg-brand-500/15 dark:text-brand-300 dark:border-brand-500/30'
            : 'bg-white text-slate-600 border-slate-200 hover:border-slate-300 dark:bg-slate-800 dark:text-slate-300 dark:border-slate-700 dark:hover:border-slate-600'
          }`}
      >
        {selectedTags.length === 1
          ? <span className="w-2 h-2 rounded-full shrink-0" style={{ backgroundColor: selectedTags[0].color }} />
          : <Tag className={`w-3.5 h-3.5 shrink-0 ${hasSelection ? '' : 'text-slate-400'}`} />}
        <span className="max-w-[140px] truncate">
          {selectedTags.length === 1
            ? selectedTags[0].name
            : hasSelection
              ? t('tag.filterCount', { n: selectedTags.length })
              : t('tag.filterAll')}
        </span>
        <ChevronDown className={`w-3.5 h-3.5 opacity-60 transition-transform ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && (
        <div className="absolute left-0 top-full mt-2 w-64 z-40 rounded-xl border border-slate-200 bg-white shadow-lg dark:border-slate-700 dark:bg-slate-800 overflow-hidden">
          <div className="flex items-center justify-between px-3 py-2 border-b border-slate-100 dark:border-slate-700">
            <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">
              {t('tag.filterTitle')}
            </span>
            {hasSelection && (
              <button
                onClick={onClearTagFilters}
                className="text-[12px] font-medium text-brand-600 dark:text-brand-400 hover:underline"
              >
                {t('tag.clearSelection')}
              </button>
            )}
          </div>

          {tags.length === 0 ? (
            <div className="px-3 py-4 text-center text-[12.5px] text-slate-400 dark:text-slate-500">
              {t('tag.empty')}
            </div>
          ) : (
            <div className="max-h-60 overflow-y-auto py-1">
              {tags.map(tg => {
                const active = tagFilters.includes(tg.id);
                return (
                  <button
                    key={tg.id}
                    onClick={() => onToggleTagFilter(tg.id)}
                    className="w-full flex items-center gap-2.5 px-3 py-1.5 text-left hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors"
                  >
                    <span className={`flex items-center justify-center w-4 h-4 rounded-[5px] border shrink-0 transition-colors
                      ${active ? 'bg-brand-500 border-brand-500' : 'border-slate-300 dark:border-slate-600'}`}>
                      {active && <Check className="w-3 h-3 text-white" strokeWidth={3} />}
                    </span>
                    <span className="w-2.5 h-2.5 rounded-full shrink-0" style={{ backgroundColor: tg.color }} />
                    <span className="flex-1 truncate text-[13px] text-slate-700 dark:text-slate-200">{tg.name}</span>
                    <span className="text-[11px] text-slate-400 dark:text-slate-500 shrink-0">{tg.itemCount}</span>
                  </button>
                );
              })}
            </div>
          )}

          <button
            onClick={() => { setOpen(false); onManage(); }}
            className="w-full flex items-center gap-2 px-3 py-2 text-[12.5px] font-medium text-slate-500 hover:text-slate-700 hover:bg-slate-50 dark:text-slate-400 dark:hover:text-slate-200 dark:hover:bg-slate-700/60 border-t border-slate-100 dark:border-slate-700 transition-colors"
          >
            <Settings2 className="w-3.5 h-3.5" /> {t('tag.manage')}
          </button>
        </div>
      )}
    </div>
  );
}

interface WorkspaceToolbarProps {
  view: WorkspaceView;
  folder: FolderResponse | null;
  /** Context không tìm thấy trong list folders (share/ẩn) nhưng vẫn đang chọn */
  folderId: string | null;
  subtitle: string;
  /** Poll/refetch nền (TanStack isFetching && !isLoading) — hiện "Đang cập nhật…" cạnh tiêu đề */
  isBackgroundFetching?: boolean;

  statusFilter: ItemStatus[];
  onToggleStatusFilter: (s: ItemStatus) => void;
  typeFilter: ItemType[];
  onToggleTypeFilter: (t: ItemType) => void;
  /** Scope integration đang chọn (sidebar). Set ⟹ ẩn chip loại (loại cố định), tiêu đề = tên nguồn. */
  sourceType?: ItemType | null;
  importantOnly: boolean;
  onImportantToggle: () => void;
  tagFilters: string[];
  onToggleTagFilter: (id: string) => void;
  onClearTagFilters: () => void;
  projectKeyFilter?: string;
  onProjectKeyChange?: (v: string) => void;
  assigneeFilter?: string;
  onAssigneeChange?: (v: string) => void;
  searchInput: string;
  onSearchChange: (v: string) => void;
}

export const WorkspaceToolbar = ({
  view, folder, folderId, subtitle, isBackgroundFetching = false,
  statusFilter, onToggleStatusFilter,
  typeFilter, onToggleTypeFilter,
  sourceType = null,
  importantOnly, onImportantToggle,
  tagFilters, onToggleTagFilter, onClearTagFilters,
  projectKeyFilter, onProjectKeyChange,
  assigneeFilter, onAssigneeChange,
  searchInput, onSearchChange,
}: WorkspaceToolbarProps) => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const [isSyncing, setIsSyncing] = useState(false);
  const [isNoteOpen, setIsNoteOpen] = useState(false);
  const [isEventOpen, setIsEventOpen] = useState(false);
  const [isTicketOpen, setIsTicketOpen] = useState(false);
  const [isTagManagerOpen, setIsTagManagerOpen] = useState(false);
  const [isDriveFolderOpen, setIsDriveFolderOpen] = useState(false);

  const { data: tags = [] } = useQuery({ queryKey: ['tags'], queryFn: tagsApi.getTags });
  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });
  const hasActiveDrive = connections.some(
    (c) => c.serviceType === 'Drive' && c.status === 'Active',
  );

  const jiraConns = connections.filter(
    (c: ConnectionDto) => c.serviceType.toLowerCase() === 'jira' && c.status.toLowerCase() === 'active'
  );

  const projectQueries = useQueries({
    queries: jiraConns.map((c: ConnectionDto) => ({
      queryKey: ['jira', 'projects', c.id],
      queryFn: () => jiraApi.getProjects(c.id),
      staleTime: 5 * 60_000,
    }))
  });

  // Danh sách người phụ trách (assignee) cho filter tab Jira — suy từ ticket đã sync.
  const { data: assignees = [] } = useQuery({
    queryKey: ['jira', 'assignees'],
    queryFn: itemsApi.getAssignees,
    enabled: sourceType === 'Ticket',
    staleTime: 60_000,
  });

  const availableProjects = useMemo(() => {
    const map = new Map<string, string>();
    projectQueries.forEach(q => {
      if (q.data) {
        q.data.forEach((p: JiraProject) => map.set(p.key, p.name));
      }
    });
    return Array.from(map.entries())
      .map(([key, name]) => ({ key, name }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [projectQueries]);

  const handleSyncAll = async () => {
    try {
      setIsSyncing(true);
      const connections = await connectionsApi.getConnections();
      const activeConns = connections.filter(c => c.status === 'Active');
      if (activeConns.length === 0) {
        toast.error(t('toolbar.noActiveConn'));
        return;
      }
      const toastId = toast.loading(t('toolbar.syncing'));
      try {
        await Promise.all(activeConns.map(c => connectionsApi.syncConnection(c.id)));
        toast.success(t('toolbar.syncDone'), { id: toastId });
        queryClient.invalidateQueries({ queryKey: ['items'] });
        queryClient.invalidateQueries({ queryKey: ['connections'] });
      } catch (err) {
        toast.error(t('integrations.syncErrorToast'), { id: toastId });
        handleApiError(err, t('integrations.syncErrorToast'), { navigate });
      }
    } catch (err) {
      handleApiError(err, t('integrations.connectionsError'), { navigate });
    } finally {
      setIsSyncing(false);
    }
  };

  // Nút tạo nhanh — ở tab lẻ chỉ hiện nút hợp loại đó; "Tất cả mục" hiện đủ.
  // Ghi chú là loại nội bộ (không phải integration) → chỉ hiện ở "Tất cả mục".
  const showNote = !sourceType;
  const showEvent = !sourceType || sourceType === 'Event';
  const showTicket = !sourceType || sourceType === 'Ticket';
  const showDriveFolder = hasActiveDrive && (!sourceType || sourceType === 'File');
  const showCreateRow = showNote || showEvent || showTicket || showDriveFolder;

  return (
    <>
      {/* ── Hàng 1: context + Đồng bộ + đổi view — cố định ở góc trên phải ── */}
      <div className="flex items-end justify-between gap-3 mb-3 flex-wrap">
        <div>
          <div className="flex items-center gap-2.5">
            {folder && (
              <span
                className="w-3 h-3 rounded-full shrink-0"
                style={{ backgroundColor: folder.color || '#94a3b8' }}
              />
            )}
            <h1 className="text-[22px] font-semibold text-slate-900 dark:text-slate-100 leading-tight m-0">
              {folderId ? (folder?.name ?? t('toolbar.folder')) : sourceType ? t(integrationLabelKey(sourceType)) : t('nav.allItems')}
            </h1>
            {isBackgroundFetching && (
              <span className="inline-flex items-center gap-1.5 text-xs font-medium text-brand-600 dark:text-brand-400">
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
                {t('common.updating')}
              </span>
            )}
          </div>
          <p className="text-[13px] text-slate-500 dark:text-slate-400 mt-0.5">
            {folderId ? t('toolbar.folderPrefix') : ''}{subtitle}
          </p>
        </div>

        <div className="flex items-center gap-2.5 flex-wrap">
          <button
            onClick={handleSyncAll}
            disabled={isSyncing}
            className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-slate-700 bg-white border border-slate-200 rounded-[9px] shadow-sm hover:bg-slate-50 dark:text-slate-200 dark:bg-slate-800 dark:border-slate-700 dark:hover:bg-slate-700 transition-colors disabled:opacity-50"
          >
            <RefreshCw className={`w-4 h-4 text-slate-500 dark:text-slate-400 ${isSyncing ? 'animate-spin' : ''}`} />
            <span>{t('toolbar.sync')}</span>
          </button>

          <WorkspaceViewSwitcher view={view} folderId={folderId} sourceType={sourceType} />
        </div>
      </div>

      {/* ── Hàng 2: filter CHIA TẦNG — Tier 1: Trạng thái · Loại · | Tier 2: Lọc thêm (Quan trọng + Tag) ── */}
      <div className="mb-4 space-y-2.5">
        {/* Tier 1 — facet chính: trạng thái & loại item */}
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <FilterGroup label={t('toolbar.groupStatus')}>
            {STATUS_FILTERS.map(f => (
              <Chip key={f.value} active={statusFilter.includes(f.value)} onClick={() => onToggleStatusFilter(f.value)}>
                {t(f.labelKey)}
              </Chip>
            ))}
          </FilterGroup>

          {/* Nhóm Loại — CHỈ hiện ở tab "Tất cả mục". Vào 1 nguồn (Email/Jira/…) loại đã cố định. */}
          {!sourceType && (
            <>
              <div className="hidden sm:block w-px h-6 bg-slate-200 dark:bg-slate-700" />
              <FilterGroup label={t('toolbar.groupType')}>
                {TYPE_FILTERS.map(f => (
                  <Chip key={f.value} active={typeFilter.includes(f.value)} onClick={() => onToggleTypeFilter(f.value)}>
                    <span className="inline-flex items-center gap-1.5">
                      {typeIcon(f.value, 'w-4 h-4')}{t(f.labelKey)}
                    </span>
                  </Chip>
                ))}
              </FilterGroup>
            </>
          )}
        </div>

        {/* Tier 2 — lọc thêm (trái): Quan trọng + Tag · nút tạo nhanh (phải), ngay trên thanh search */}
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
          <FilterGroup label={t('toolbar.groupRefine')}>
            <Chip active={importantOnly} onClick={onImportantToggle}>
              <Star className={`w-3.5 h-3.5 ${importantOnly ? 'fill-amber-400 text-amber-400' : 'text-slate-400'}`} />
              {t('toolbar.important')}
            </Chip>
            <TagFilterDropdown
              tags={tags}
              tagFilters={tagFilters}
              onToggleTagFilter={onToggleTagFilter}
              onClearTagFilters={onClearTagFilters}
              onManage={() => setIsTagManagerOpen(true)}
            />
          </FilterGroup>

          {/* Nút tạo nhanh — dồn về mép phải, cùng dòng lọc thêm. Ẩn cả cụm nếu không có nút hợp tab. */}
          {showCreateRow && (
            <div className="flex items-center gap-2.5 flex-wrap justify-end ml-auto">
              {showNote && (
                <button
                  onClick={() => setIsNoteOpen(true)}
                  className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-slate-700 bg-white border border-slate-200 rounded-[9px] shadow-sm hover:bg-slate-50 dark:text-slate-200 dark:bg-slate-800 dark:border-slate-700 dark:hover:bg-slate-700 transition-colors"
                  title={t('toolbar.noteTooltip')}
                >
                  <Plus className="w-4 h-4" /> {t('toolbar.note')}
                </button>
              )}
              {showEvent && (
                <button
                  onClick={() => setIsEventOpen(true)}
                  className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-slate-700 bg-white border border-slate-200 rounded-[9px] shadow-sm hover:bg-slate-50 dark:text-slate-200 dark:bg-slate-800 dark:border-slate-700 dark:hover:bg-slate-700 transition-colors"
                  title={t('toolbar.eventTooltip')}
                >
                  <Plus className="w-4 h-4" /> {t('toolbar.event')}
                </button>
              )}
              {showTicket && (
                <button
                  onClick={() => setIsTicketOpen(true)}
                  className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-slate-700 bg-white border border-slate-200 rounded-[9px] shadow-sm hover:bg-slate-50 dark:text-slate-200 dark:bg-slate-800 dark:border-slate-700 dark:hover:bg-slate-700 transition-colors"
                  title={t('toolbar.ticketTooltip')}
                >
                  <Plus className="w-4 h-4" /> {t('type.ticket')}
                </button>
              )}
              {showDriveFolder && (
                <button
                  type="button"
                  onClick={() => setIsDriveFolderOpen(true)}
                  className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-slate-700 bg-white border border-slate-200 rounded-[9px] shadow-sm hover:bg-slate-50 dark:text-slate-200 dark:bg-slate-800 dark:border-slate-700 dark:hover:bg-slate-700 transition-colors"
                >
                  <FolderPlus className="w-4 h-4" />
                  <span>{t('toolbar.driveFolder')}</span>
                </button>
              )}
            </div>
          )}
        </div>
      </div>

      {/* ── Hàng 3: search full-width — vị trí + kích thước GIỐNG HỆT 2 view ── */}
      <div className="flex gap-2 mb-4">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
          <input
            type="text"
            value={searchInput}
            onChange={e => onSearchChange(e.target.value)}
            placeholder={t('toolbar.search')}
            className="w-full h-9 pl-9 pr-4 rounded-lg border border-slate-200 bg-white text-[13px] text-slate-900 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-brand-500/30 focus:border-brand-400 transition dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 dark:placeholder:text-slate-500"
          />
        </div>
        {/* Lọc theo space (project) Jira — CHỈ hiện khi đang ở tab Jira. */}
        {onProjectKeyChange && sourceType === 'Ticket' && jiraConns.length > 0 && (
          <div className="w-56 shrink-0">
            <Select
              value={projectKeyFilter ?? ''}
              onChange={onProjectKeyChange}
              className="h-9 text-[13px]"
              icon={<Briefcase className="w-4 h-4" />}
              placeholder={`${t('createTicket.selectProject')}...`}
              options={[
                { value: '', label: t('toolbar.allProjects') },
                ...availableProjects.map(p => ({ value: p.key, label: `${p.name} (${p.key})` })),
              ]}
            />
          </div>
        )}
        {/* Lọc theo người phụ trách (assignee) — CHỈ hiện khi đang ở tab Jira. */}
        {onAssigneeChange && sourceType === 'Ticket' && jiraConns.length > 0 && (
          <div className="w-52 shrink-0">
            <Select
              value={assigneeFilter ?? ''}
              onChange={onAssigneeChange}
              className="h-9 text-[13px]"
              icon={<UserRound className="w-4 h-4" />}
              placeholder={t('toolbar.allAssignees')}
              options={[
                { value: '', label: t('toolbar.allAssignees') },
                ...assignees.map(a => ({ value: a.accountId, label: a.displayName })),
              ]}
            />
          </div>
        )}
      </div>

      <CreateNoteModal isOpen={isNoteOpen} onClose={() => setIsNoteOpen(false)} folder={folder} />
      <CreateEventModal isOpen={isEventOpen} onClose={() => setIsEventOpen(false)} />
      <CreateTicketModal isOpen={isTicketOpen} onClose={() => setIsTicketOpen(false)} />
      <TagManagerModal isOpen={isTagManagerOpen} onClose={() => setIsTagManagerOpen(false)} />
      <CreateDriveFolderModal isOpen={isDriveFolderOpen} onClose={() => setIsDriveFolderOpen(false)} />
    </>
  );
};
