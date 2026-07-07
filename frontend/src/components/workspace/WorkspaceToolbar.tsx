import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Star, Search, LayoutGrid, List, RefreshCw, Plus, Tag, Settings2,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { connectionsApi } from '../../lib/connectionsApi';
import { tagsApi } from '../../lib/tagsApi';
import { useI18n } from '../../hooks/useI18n';
import { handleApiError } from '../../lib/errorUtils';
import { TYPE_FILTERS, STATUS_FILTERS, typeIcon } from '../../lib/itemVisuals';
import type { ItemType, ItemStatus, FolderResponse } from '../../types/items';
import { CreateNoteModal } from './CreateNoteModal';
import { CreateEventModal } from './CreateEventModal';
import { TagManagerModal } from '../tags/TagManagerModal';

/*
 * Toolbar dùng chung cho 2 view của workspace (Danh sách "/" + Bảng "/kanban").
 * MỤC TIÊU: đổi view KHÔNG thay đổi layout — mọi hàng GIỐNG HỆT nhau ở 2 view:
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

interface WorkspaceToolbarProps {
  view: 'list' | 'board';
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
  importantOnly: boolean;
  onImportantToggle: () => void;
  tagFilter: string | null;
  onTagFilter: (id: string | null) => void;
  searchInput: string;
  onSearchChange: (v: string) => void;
}

export const WorkspaceToolbar = ({
  view, folder, folderId, subtitle, isBackgroundFetching = false,
  statusFilter, onToggleStatusFilter,
  typeFilter, onToggleTypeFilter,
  importantOnly, onImportantToggle,
  tagFilter, onTagFilter,
  searchInput, onSearchChange,
}: WorkspaceToolbarProps) => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { t } = useI18n();
  const [isSyncing, setIsSyncing] = useState(false);
  const [isNoteOpen, setIsNoteOpen] = useState(false);
  const [isEventOpen, setIsEventOpen] = useState(false);
  const [isTagManagerOpen, setIsTagManagerOpen] = useState(false);

  const { data: tags = [] } = useQuery({ queryKey: ['tags'], queryFn: tagsApi.getTags });

  const q = folderId ? `?folder=${folderId}` : '';

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

  return (
    <>
      {/* ── Hàng 1: context + actions (vị trí cố định ở cả 2 view) ── */}
      <div className="flex items-end justify-between gap-3 mb-4 flex-wrap">
        <div>
          <div className="flex items-center gap-2.5">
            {folder && (
              <span
                className="w-3 h-3 rounded-full shrink-0"
                style={{ backgroundColor: folder.color || '#94a3b8' }}
              />
            )}
            <h1 className="text-[22px] font-semibold text-slate-900 dark:text-slate-100 leading-tight m-0">
              {folderId ? (folder?.name ?? t('toolbar.folder')) : t('nav.allItems')}
            </h1>
            {isBackgroundFetching && (
              <span className="text-xs text-slate-400 dark:text-slate-500">{t('common.updating')}</span>
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

          {/* View switcher — luôn giữ ?folder= khi đổi view */}
          <div className="flex items-center gap-1 p-[3px] bg-white border border-slate-200 rounded-[9px] dark:bg-slate-800 dark:border-slate-700">
            <button
              onClick={() => view !== 'list' && navigate(`/${q}`)}
              className={`flex items-center gap-1.5 px-[11px] py-1.5 rounded-[7px] text-[13px] transition-colors ${
                view === 'list'
                  ? 'bg-brand-50 text-brand-700 font-semibold dark:bg-brand-500/15 dark:text-brand-300'
                  : 'text-slate-500 font-medium hover:bg-slate-50 dark:text-slate-400 dark:hover:bg-slate-700'
              }`}
            >
              <List className="w-4 h-4" />
              <span>{t('toolbar.list')}</span>
            </button>
            <button
              onClick={() => view !== 'board' && navigate(`/kanban${q}`)}
              className={`flex items-center gap-1.5 px-[11px] py-1.5 rounded-[7px] text-[13px] transition-colors ${
                view === 'board'
                  ? 'bg-brand-50 text-brand-700 font-semibold dark:bg-brand-500/15 dark:text-brand-300'
                  : 'text-slate-500 font-medium hover:bg-slate-50 dark:text-slate-400 dark:hover:bg-slate-700'
              }`}
            >
              <LayoutGrid className="w-4 h-4" />
              <span>{t('toolbar.board')}</span>
            </button>
          </div>

          {/* Tạo nội dung — có ở CẢ 2 view */}
          <button
            onClick={() => setIsNoteOpen(true)}
            className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-slate-700 bg-white border border-slate-200 rounded-[9px] shadow-sm hover:bg-slate-50 dark:text-slate-200 dark:bg-slate-800 dark:border-slate-700 dark:hover:bg-slate-700 transition-colors"
          >
            <Plus className="w-4 h-4" /> {t('toolbar.note')}
          </button>
          <button
            onClick={() => setIsEventOpen(true)}
            className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-slate-700 bg-white border border-slate-200 rounded-[9px] shadow-sm hover:bg-slate-50 dark:text-slate-200 dark:bg-slate-800 dark:border-slate-700 dark:hover:bg-slate-700 transition-colors"
          >
            <Plus className="w-4 h-4" /> {t('toolbar.event')}
          </button>
        </div>
      </div>

      {/* ── Hàng 2: filter chips — GIỐNG HỆT 2 view (Bảng: chip trạng thái lọc cột hiển thị) ── */}
      <div className="flex flex-wrap gap-2 items-center mb-3">
        {STATUS_FILTERS.map(f => (
          <Chip key={f.value} active={statusFilter.includes(f.value)} onClick={() => onToggleStatusFilter(f.value)}>
            {t(f.labelKey)}
          </Chip>
        ))}

        <div className="w-px h-[22px] bg-slate-200 dark:bg-slate-700 mx-0.5" />

        {TYPE_FILTERS.map(f => (
          <Chip key={f.value} active={typeFilter.includes(f.value)} onClick={() => onToggleTypeFilter(f.value)}>
            <span className="inline-flex items-center gap-1">
              {typeIcon(f.value, 'w-3.5 h-3.5')}{t(f.labelKey)}
            </span>
          </Chip>
        ))}

        <div className="w-px h-[22px] bg-slate-200 dark:bg-slate-700 mx-0.5" />

        <Chip active={importantOnly} onClick={onImportantToggle}>
          <Star className={`w-3.5 h-3.5 ${importantOnly ? 'fill-amber-400 text-amber-400' : 'text-slate-400'}`} />
          {t('toolbar.important')}
        </Chip>

        <div className="w-px h-[22px] bg-slate-200 dark:bg-slate-700 mx-0.5" />

        {/* Lọc theo tag — bấm tag đang chọn để bỏ lọc */}
        {tags.map(tg => (
          <Chip key={tg.id} active={tagFilter === tg.id} onClick={() => onTagFilter(tagFilter === tg.id ? null : tg.id)}>
            <span className="w-2 h-2 rounded-full" style={{ backgroundColor: tg.color }} />
            {tg.name}
          </Chip>
        ))}
        <button
          onClick={() => setIsTagManagerOpen(true)}
          className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-[13px] font-medium border border-dashed border-slate-300 text-slate-500 hover:border-slate-400 hover:text-slate-700 dark:border-slate-600 dark:text-slate-400 dark:hover:text-slate-200 transition-colors whitespace-nowrap"
          title={t('tag.manageTitle')}
        >
          {tags.length === 0 ? <Tag className="w-3.5 h-3.5" /> : <Settings2 className="w-3.5 h-3.5" />}
          {tags.length === 0 ? t('tag.addTag') : t('tag.manage')}
        </button>
      </div>

      {/* ── Hàng 3: search full-width — vị trí + kích thước GIỐNG HỆT 2 view ── */}
      <div className="relative mb-4">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
        <input
          type="text"
          value={searchInput}
          onChange={e => onSearchChange(e.target.value)}
          placeholder={t('toolbar.search')}
          className="w-full h-9 pl-9 pr-4 rounded-lg border border-slate-200 bg-white text-[13px] text-slate-900 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-brand-500/30 focus:border-brand-400 transition dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 dark:placeholder:text-slate-500"
        />
      </div>

      <CreateNoteModal isOpen={isNoteOpen} onClose={() => setIsNoteOpen(false)} folder={folder} />
      <CreateEventModal isOpen={isEventOpen} onClose={() => setIsEventOpen(false)} />
      <TagManagerModal isOpen={isTagManagerOpen} onClose={() => setIsTagManagerOpen(false)} />
    </>
  );
};
