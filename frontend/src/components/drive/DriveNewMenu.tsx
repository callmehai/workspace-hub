import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ChevronDown, FolderPlus, Loader2, Plus, Upload, FolderUp } from 'lucide-react';
import toast from 'react-hot-toast';
import { connectionsApi } from '../../lib/connectionsApi';
import {
  buildDriveFolderEntries,
  driveApi,
  validateDriveUploadFiles,
} from '../../lib/driveApi';
import { handleApiError } from '../../lib/errorUtils';
import { useI18n } from '../../hooks/useI18n';
import { CreateDriveFolderModal } from './CreateDriveFolderModal';

interface Props {
  /** Folder Drive đang xem (drill-down) — upload vào đây nếu có */
  defaultParentItemId?: string | null;
  /** Gợi ý connection (từ ItemDetail) */
  defaultConnectionId?: string;
}

/**
 * Menu "Mới" cho Google Drive — tạo folder / upload file / upload folder.
 * Giống menu New trên Google Drive gốc.
 */
export function DriveNewMenu({ defaultParentItemId, defaultConnectionId }: Props) {
  const { t } = useI18n();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const menuRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const folderInputRef = useRef<HTMLInputElement>(null);

  const [open, setOpen] = useState(false);
  const [isFolderModalOpen, setIsFolderModalOpen] = useState(false);
  const [isUploading, setIsUploading] = useState(false);

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });

  const driveConnections = useMemo(
    () => connections.filter((c) => c.serviceType === 'Drive' && c.status === 'Active'),
    [connections],
  );

  const connectionId = useMemo(() => {
    if (defaultConnectionId && driveConnections.some((c) => c.id === defaultConnectionId)) {
      return defaultConnectionId;
    }
    return driveConnections[0]?.id ?? '';
  }, [defaultConnectionId, driveConnections]);

  // Đóng menu khi click ra ngoài
  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [open]);

  /** Upload nhiều file đơn — gọi API từng file một */
  const handleFilesSelected = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    if (!connectionId) {
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
    try {
      for (let i = 0; i < list.length; i++) {
        toast.loading(t('drive.upload.progress', { current: i + 1, total: list.length }), { id: toastId });
        await driveApi.uploadFile({
          connectionId,
          file: list[i],
          parentItemId: defaultParentItemId || null,
        });
      }
      toast.success(
        list.length === 1 ? t('drive.upload.fileDone') : t('drive.upload.filesDone', { n: list.length }),
        { id: toastId },
      );
      queryClient.invalidateQueries({ queryKey: ['items'] });
    } catch (err) {
      toast.dismiss(toastId);
      handleApiError(err, t('drive.upload.fail'), { navigate });
    } finally {
      setIsUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  /** Upload cả folder — một request batch lên BE */
  const handleFolderSelected = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    if (!connectionId) {
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
        connectionId,
        entries: buildDriveFolderEntries(list),
        parentItemId: defaultParentItemId || null,
      });
      toast.success(
        t('drive.upload.folderDone', { files: result.filesUploaded, folders: result.foldersCreated }),
        { id: toastId },
      );
      queryClient.invalidateQueries({ queryKey: ['items'] });
    } catch (err) {
      toast.dismiss(toastId);
      handleApiError(err, t('drive.upload.fail'), { navigate });
    } finally {
      setIsUploading(false);
      if (folderInputRef.current) folderInputRef.current.value = '';
    }
  };

  if (driveConnections.length === 0) return null;

  return (
    <>
      <div className="relative" ref={menuRef}>
        <button
          type="button"
          disabled={isUploading}
          onClick={() => setOpen((o) => !o)}
          className="inline-flex items-center justify-center gap-1.5 h-9 px-3 text-[13px] font-semibold text-slate-700 bg-white border border-slate-200 rounded-[9px] shadow-sm hover:bg-slate-50 dark:text-slate-200 dark:bg-slate-800 dark:border-slate-700 dark:hover:bg-slate-700 transition-colors disabled:opacity-50"
        >
          {isUploading ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            <Plus className="w-4 h-4" />
          )}
          <span>{t('toolbar.driveNew')}</span>
          <ChevronDown className={`w-3.5 h-3.5 opacity-60 transition-transform ${open ? 'rotate-180' : ''}`} />
        </button>

        {open && (
          <div className="absolute right-0 top-full mt-2 w-56 z-40 rounded-xl border border-slate-200 bg-white shadow-lg dark:border-slate-700 dark:bg-slate-800 overflow-hidden py-1">
            <button
              type="button"
              onClick={() => { setOpen(false); setIsFolderModalOpen(true); }}
              className="w-full flex items-center gap-2.5 px-3 py-2 text-left text-[13px] text-slate-700 hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-700/60 transition-colors"
            >
              <FolderPlus className="w-4 h-4 text-slate-500" />
              {t('drive.upload.newFolder')}
            </button>

            <div className="my-1 border-t border-slate-100 dark:border-slate-700" />

            <button
              type="button"
              onClick={() => { setOpen(false); fileInputRef.current?.click(); }}
              className="w-full flex items-center gap-2.5 px-3 py-2 text-left text-[13px] text-slate-700 hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-700/60 transition-colors"
            >
              <Upload className="w-4 h-4 text-slate-500" />
              {t('drive.upload.uploadFile')}
            </button>

            <button
              type="button"
              onClick={() => { setOpen(false); folderInputRef.current?.click(); }}
              className="w-full flex items-center gap-2.5 px-3 py-2 text-left text-[13px] text-slate-700 hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-700/60 transition-colors"
            >
              <FolderUp className="w-4 h-4 text-slate-500" />
              {t('drive.upload.uploadFolder')}
            </button>
          </div>
        )}

        {/* Input ẩn — chọn file từ máy */}
        <input
          ref={fileInputRef}
          type="file"
          multiple
          className="hidden"
          onChange={(e) => void handleFilesSelected(e.target.files)}
        />
        {/* Input ẩn — chọn folder từ máy (Chrome/Edge) */}
        <input
          ref={folderInputRef}
          type="file"
          className="hidden"
          // @ts-expect-error webkitdirectory không có trong type DOM chuẩn
          webkitdirectory=""
          onChange={(e) => void handleFolderSelected(e.target.files)}
        />
      </div>

      <CreateDriveFolderModal
        isOpen={isFolderModalOpen}
        onClose={() => setIsFolderModalOpen(false)}
        defaultConnectionId={connectionId}
        defaultParentItemId={defaultParentItemId}
      />
    </>
  );
}
