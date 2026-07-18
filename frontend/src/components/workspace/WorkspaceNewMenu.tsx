import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  ChevronDown, Plus, StickyNote, CalendarPlus, Ticket,
  FolderPlus, Upload, FolderUp,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { connectionsApi, type ConnectionDto } from '../../lib/connectionsApi';
import { buildDriveFolderEntries } from '../../lib/driveApi';
import { enqueueFiles, enqueueFolder } from '../../lib/driveUploadStore';
import { itemsApi } from '../../lib/itemsApi';
import { handleApiError } from '../../lib/errorUtils';
import { markSeen } from '../../lib/seenStore';
import { emptyCalendarForm, formToRange, calendarRangeToApiTimes } from '../../lib/calendarFormUtils';
import { useI18n } from '../../hooks/useI18n';
import type { FolderResponse, ItemType } from '../../types/items';
import { CreateNoteModal } from './CreateNoteModal';
import { CalendarEventEditorModal, type CalendarEventFormValue } from '../calendar/CalendarEventEditorModal';
import { CreateTicketModal } from '../jira/CreateTicketModal';
import { CreateDriveFolderModal } from '../drive/CreateDriveFolderModal';

interface Props {
  /** Folder context hiện tại — truyền cho ghi chú mới. */
  folder: FolderResponse | null;
  /** Folder Drive đang mở (drill-down) — upload/ tạo folder vào đây nếu có. */
  currentDriveFolderId?: string;
  /** Tab nguồn đang xem (sidebar). null = "Tất cả mục" → full option; tab cụ thể → chỉ option hợp loại đó. */
  sourceType?: ItemType | null;
  /** Tài khoản Drive đang được lọc (multi-account) — ưu tiên làm đích upload/tạo folder nếu hợp lệ. */
  preferredDriveConnectionId?: string;
}

/** 1 dòng trong menu "Mới". */
function MenuItem({
  icon, label, onClick,
}: { icon: React.ReactNode; label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="w-full flex items-center gap-2.5 px-3 py-2 text-left text-[13px] text-slate-700 hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-700/60 transition-colors"
    >
      <span className="text-slate-500 dark:text-slate-400 shrink-0">{icon}</span>
      {label}
    </button>
  );
}

/**
 * Menu "Mới" hợp nhất — gộp mọi hành động tạo vào 1 dropdown.
 * Option hiện theo integration đang Active: Ghi chú (nội bộ, luôn có) · Sự kiện (Google
 * Calendar) · Ticket (Jira) · Thư mục mới / Tải tệp / Tải thư mục (Google Drive).
 */
export function WorkspaceNewMenu({ folder, currentDriveFolderId, sourceType = null, preferredDriveConnectionId }: Props) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const menuRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const folderInputRef = useRef<HTMLInputElement>(null);

  const [open, setOpen] = useState(false);
  const [isNoteOpen, setIsNoteOpen] = useState(false);
  const [isEventOpen, setIsEventOpen] = useState(false);
  const [isTicketOpen, setIsTicketOpen] = useState(false);
  const [isFolderModalOpen, setIsFolderModalOpen] = useState(false);

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });

  // Option nào hiện là do integration đang Active quyết định.
  const isActive = (svc: string) => (c: ConnectionDto) =>
    c.serviceType.toLowerCase() === svc && c.status.toLowerCase() === 'active';
  const hasGcal = connections.some(isActive('gcal'));
  const hasJira = connections.some(isActive('jira'));
  const gcalConnections = useMemo(
    () => connections.filter(isActive('gcal')),
    [connections],
  );
  const driveConnections = useMemo(
    () => connections.filter(isActive('drive')),
    [connections],
  );
  const hasDrive = driveConnections.length > 0;
  // Ưu tiên account Drive đang lọc (nếu còn Active); else Drive đầu tiên.
  const driveConnectionId =
    (preferredDriveConnectionId && driveConnections.some((c) => c.id === preferredDriveConnectionId)
      ? preferredDriveConnectionId
      : driveConnections[0]?.id) ?? '';

  // Đang drill trong 1 folder Drive → chỉ có ý nghĩa tạo/tải nội dung Drive (tạo Ticket/Note/Event
  // vào folder Drive là vô nghĩa). currentDriveFolderId set = đang trong folder Drive.
  const inDriveFolder = !!currentDriveFolderId;

  // Option hiện = (đang ở "Tất cả mục" HOẶC đúng tab của loại đó) VÀ integration tương ứng Active.
  // Ghi chú là nội bộ (không integration) → chỉ ở "Tất cả mục". Trong folder Drive → ẩn hết trừ Drive.
  const showNote = !sourceType && !inDriveFolder;
  const showEvent = !inDriveFolder && (!sourceType || sourceType === 'Event') && hasGcal;
  const showTicket = !inDriveFolder && (!sourceType || sourceType === 'Ticket') && hasJira;
  const showDrive = (inDriveFolder || !sourceType || sourceType === 'File') && hasDrive;
  const hasAnyOption = showNote || showEvent || showTicket || showDrive;

  const createEventMutation = useMutation({
    mutationFn: (form: CalendarEventFormValue) => {
      const { start, end } = formToRange(form);
      const times = calendarRangeToApiTimes(start, end, form.allDay);
      return itemsApi.createEvent({
        connectionId: form.connectionId,
        title: form.title,
        start: times.start,
        end: times.end,
        allDay: form.allDay,
        location: form.location.trim() || undefined,
        attendees: form.attendees,
        description: form.description.trim() || undefined,
        driveItemIds: form.driveItemIds.length > 0 ? form.driveItemIds : undefined,
      });
    },
    onSuccess: (created) => {
      markSeen(created.id);
      queryClient.invalidateQueries({ queryKey: ['items'] });
      queryClient.invalidateQueries({ queryKey: ['calendar-items'] });
      setIsEventOpen(false);
      toast.success(t('createEvent.created'));
    },
    onError: (err) => handleApiError(err, t('createEvent.createFail'), { navigate }),
  });

  // Đóng menu khi click ra ngoài.
  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [open]);

  /** Chọn nhiều file → đẩy vào hàng đợi upload (song song + progress ở panel). File lỗi tự bị bỏ qua. */
  const handleFilesSelected = (files: FileList | null) => {
    if (!files || files.length === 0) return;
    if (!driveConnectionId) {
      toast.error(t('drive.createFolder.noConnection'));
      return;
    }
    enqueueFiles(Array.from(files), {
      connectionId: driveConnectionId,
      parentItemId: currentDriveFolderId || null,
      folderId: folder?.id ?? null,
    });
    if (fileInputRef.current) fileInputRef.current.value = '';
  };

  /** Chọn cả folder → 1 task upload batch trong panel (progress tổng). */
  const handleFolderSelected = (files: FileList | null) => {
    if (!files || files.length === 0) return;
    if (!driveConnectionId) {
      toast.error(t('drive.createFolder.noConnection'));
      return;
    }
    const list = Array.from(files);
    const folderName =
      (list[0] as File & { webkitRelativePath?: string }).webkitRelativePath?.split('/')[0] ||
      t('drive.upload.uploadFolder');
    enqueueFolder(folderName, buildDriveFolderEntries(list), {
      connectionId: driveConnectionId,
      parentItemId: currentDriveFolderId || null,
      folderId: folder?.id ?? null,
    });
    if (folderInputRef.current) folderInputRef.current.value = '';
  };

  // Không có option nào hợp tab hiện tại → ẩn hẳn nút "Mới" (vd tab Gmail không có hành động tạo).
  if (!hasAnyOption) return null;

  return (
    <>
      <div className="relative" ref={menuRef}>
        <button
          type="button"
          onClick={() => setOpen((o) => !o)}
          className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-white bg-brand-600 rounded-[9px] shadow-sm hover:bg-brand-700 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>{t('toolbar.driveNew')}</span>
          <ChevronDown className={`w-3.5 h-3.5 opacity-80 transition-transform ${open ? 'rotate-180' : ''}`} />
        </button>

        {open && (
          <div className="absolute right-0 top-full mt-2 w-56 z-40 rounded-xl border border-slate-200 bg-white shadow-lg dark:border-slate-700 dark:bg-slate-800 overflow-hidden py-1">
            {/* Ghi chú — nội bộ app, chỉ ở "Tất cả mục" */}
            {showNote && (
              <MenuItem
                icon={<StickyNote className="w-4 h-4" />}
                label={t('toolbar.note')}
                onClick={() => { setOpen(false); setIsNoteOpen(true); }}
              />
            )}
            {/* Sự kiện — cần Google Calendar */}
            {showEvent && (
              <MenuItem
                icon={<CalendarPlus className="w-4 h-4" />}
                label={t('toolbar.event')}
                onClick={() => { setOpen(false); setIsEventOpen(true); }}
              />
            )}
            {/* Ticket — cần Jira */}
            {showTicket && (
              <MenuItem
                icon={<Ticket className="w-4 h-4" />}
                label={t('type.ticket')}
                onClick={() => { setOpen(false); setIsTicketOpen(true); }}
              />
            )}

            {/* Nhóm Drive — cần Google Drive */}
            {showDrive && (
              <>
                {/* Đường kẻ chỉ khi có option phía trên (tránh divider mồ côi ở đầu menu). */}
                {(showNote || showEvent || showTicket) && (
                  <div className="my-1 border-t border-slate-100 dark:border-slate-700" />
                )}
                <MenuItem
                  icon={<FolderPlus className="w-4 h-4" />}
                  label={t('drive.upload.newFolder')}
                  onClick={() => { setOpen(false); setIsFolderModalOpen(true); }}
                />
                <MenuItem
                  icon={<Upload className="w-4 h-4" />}
                  label={t('drive.upload.uploadFile')}
                  onClick={() => { setOpen(false); fileInputRef.current?.click(); }}
                />
                <MenuItem
                  icon={<FolderUp className="w-4 h-4" />}
                  label={t('drive.upload.uploadFolder')}
                  onClick={() => { setOpen(false); folderInputRef.current?.click(); }}
                />
              </>
            )}
          </div>
        )}

        {/* Input ẩn — chọn file/folder từ máy */}
        <input
          ref={fileInputRef}
          type="file"
          multiple
          className="hidden"
          onChange={(e) => void handleFilesSelected(e.target.files)}
        />
        <input
          ref={folderInputRef}
          type="file"
          className="hidden"
          // @ts-expect-error webkitdirectory không có trong type DOM chuẩn
          webkitdirectory=""
          onChange={(e) => void handleFolderSelected(e.target.files)}
        />
      </div>

      <CreateNoteModal isOpen={isNoteOpen} onClose={() => setIsNoteOpen(false)} folder={folder} />
      <CalendarEventEditorModal
        key={isEventOpen ? (gcalConnections[0]?.id ?? 'new') : 'closed'}
        open={isEventOpen}
        mode="create"
        initialValue={emptyCalendarForm(new Date(), gcalConnections[0]?.id ?? '')}
        connections={gcalConnections}
        allConnections={connections}
        saving={createEventMutation.isPending}
        onClose={() => setIsEventOpen(false)}
        onSubmit={form => createEventMutation.mutate(form)}
      />
      <CreateTicketModal isOpen={isTicketOpen} onClose={() => setIsTicketOpen(false)} />
      <CreateDriveFolderModal
        isOpen={isFolderModalOpen}
        onClose={() => setIsFolderModalOpen(false)}
        defaultConnectionId={driveConnectionId}
        defaultParentItemId={currentDriveFolderId}
        folderContextId={folder?.id ?? null}
      />
    </>
  );
}
