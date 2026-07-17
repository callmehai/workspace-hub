import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  ChevronDown, Plus, Loader2, StickyNote, CalendarPlus, Ticket,
  FolderPlus, Upload, FolderUp,
} from 'lucide-react';
import toast from 'react-hot-toast';
import { connectionsApi, type ConnectionDto } from '../../lib/connectionsApi';
import { buildDriveFolderEntries, driveApi, validateDriveUploadFiles } from '../../lib/driveApi';
import { handleApiError } from '../../lib/errorUtils';
import { useI18n } from '../../hooks/useI18n';
import type { FolderResponse, ItemType } from '../../types/items';
import { CreateNoteModal } from './CreateNoteModal';
import { CreateEventModal } from './CreateEventModal';
import { CreateTicketModal } from '../jira/CreateTicketModal';
import { CreateDriveFolderModal } from '../drive/CreateDriveFolderModal';

interface Props {
  /** Folder context hiện tại — truyền cho ghi chú mới. */
  folder: FolderResponse | null;
  /** Folder Drive đang mở (drill-down) — upload/ tạo folder vào đây nếu có. */
  currentDriveFolderId?: string;
  /** Tab nguồn đang xem (sidebar). null = "Tất cả mục" → full option; tab cụ thể → chỉ option hợp loại đó. */
  sourceType?: ItemType | null;
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
export function WorkspaceNewMenu({ folder, currentDriveFolderId, sourceType = null }: Props) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const menuRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const folderInputRef = useRef<HTMLInputElement>(null);

  const [open, setOpen] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
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
  const driveConnections = useMemo(
    () => connections.filter(isActive('drive')),
    [connections],
  );
  const hasDrive = driveConnections.length > 0;
  const driveConnectionId = driveConnections[0]?.id ?? '';

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

  // Đóng menu khi click ra ngoài.
  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [open]);

  /** Upload nhiều file đơn — mỗi file 1 request, gom lỗi báo "đã lên X/Y". */
  const handleFilesSelected = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    if (!driveConnectionId) {
      toast.error(t('drive.createFolder.noConnection'));
      return;
    }

    const list = Array.from(files);
    const validationError = validateDriveUploadFiles(list);
    if (validationError) {
      toast.error(t(validationError));
      return;
    }

    setIsUploading(true);
    const toastId = toast.loading(t('drive.upload.uploading'));
    let ok = 0;
    let lastError: unknown = null;
    for (let i = 0; i < list.length; i++) {
      toast.loading(t('drive.upload.progress', { current: i + 1, total: list.length }), { id: toastId });
      try {
        await driveApi.uploadFile({
          connectionId: driveConnectionId,
          file: list[i],
          parentItemId: currentDriveFolderId || null,
        });
        ok += 1;
      } catch (err) {
        lastError = err;
      }
    }
    const failedCount = list.length - ok;

    if (ok > 0) queryClient.invalidateQueries({ queryKey: ['items'] });

    if (failedCount === 0) {
      toast.success(
        list.length === 1 ? t('drive.upload.fileDone') : t('drive.upload.filesDone', { n: list.length }),
        { id: toastId },
      );
    } else if (ok > 0) {
      toast(t('drive.upload.filesPartial', { ok, total: list.length, failed: failedCount }), {
        id: toastId,
        icon: '⚠️',
      });
    } else {
      toast.dismiss(toastId);
      handleApiError(lastError, t('drive.upload.fail'), { navigate });
    }

    setIsUploading(false);
    if (fileInputRef.current) fileInputRef.current.value = '';
  };

  /** Upload cả folder — một request batch lên BE. */
  const handleFolderSelected = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    if (!driveConnectionId) {
      toast.error(t('drive.createFolder.noConnection'));
      return;
    }

    const list = Array.from(files);
    const validationError = validateDriveUploadFiles(list);
    if (validationError) {
      toast.error(t(validationError));
      return;
    }

    setIsUploading(true);
    const toastId = toast.loading(t('drive.upload.uploadingFolder'));
    try {
      const result = await driveApi.uploadFolder({
        connectionId: driveConnectionId,
        entries: buildDriveFolderEntries(list),
        parentItemId: currentDriveFolderId || null,
      });
      const failedCount = result.failed?.length ?? 0;
      if (failedCount > 0) {
        toast(
          t('drive.upload.folderPartial', {
            ok: result.filesUploaded,
            total: result.filesUploaded + failedCount,
            folders: result.foldersCreated,
            failed: failedCount,
          }),
          { id: toastId, icon: '⚠️' },
        );
      } else {
        toast.success(
          t('drive.upload.folderDone', { files: result.filesUploaded, folders: result.foldersCreated }),
          { id: toastId },
        );
      }
      queryClient.invalidateQueries({ queryKey: ['items'] });
    } catch (err) {
      toast.dismiss(toastId);
      handleApiError(err, t('drive.upload.fail'), { navigate });
    } finally {
      setIsUploading(false);
      if (folderInputRef.current) folderInputRef.current.value = '';
    }
  };

  // Không có option nào hợp tab hiện tại → ẩn hẳn nút "Mới" (vd tab Gmail không có hành động tạo).
  if (!hasAnyOption) return null;

  return (
    <>
      <div className="relative" ref={menuRef}>
        <button
          type="button"
          disabled={isUploading}
          onClick={() => setOpen((o) => !o)}
          className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-white bg-brand-600 rounded-[9px] shadow-sm hover:bg-brand-700 transition-colors disabled:opacity-50"
        >
          {isUploading ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />}
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
      <CreateEventModal isOpen={isEventOpen} onClose={() => setIsEventOpen(false)} />
      <CreateTicketModal isOpen={isTicketOpen} onClose={() => setIsTicketOpen(false)} />
      <CreateDriveFolderModal
        isOpen={isFolderModalOpen}
        onClose={() => setIsFolderModalOpen(false)}
        defaultConnectionId={driveConnectionId}
        defaultParentItemId={currentDriveFolderId}
      />
    </>
  );
}
