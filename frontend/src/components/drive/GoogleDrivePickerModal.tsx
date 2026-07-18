import { useEffect, useState, useMemo, useRef, type ChangeEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import { X, Search, Grid, List, FileText, Check, AlertCircle, Loader2, CloudUpload } from 'lucide-react';
import toast from 'react-hot-toast';
import { DriveIcon } from '../../lib/brandIcons';
import { itemsApi } from '../../lib/itemsApi';
import { driveApi, validateDriveUploadFiles } from '../../lib/driveApi';
import { useI18n } from '../../hooks/useI18n';
import { handleApiError } from '../../lib/errorUtils';
import type { ItemResponse } from '../../types/items';

export interface DrivePickerSelectMeta {
  uploadedThisSessionIds: string[];
}

interface GoogleDrivePickerModalProps {
  open: boolean;
  connectionId: string;
  initialSelectedIds: string[];
  onClose: () => void;
  onSelect: (selectedIds: string[], meta: DrivePickerSelectMeta) => void;
}

type TabType = 'drive' | 'computer';

function formatFileSize(bytes?: number): string {
  if (!bytes || bytes <= 0) return '';
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(bytes < 10 * 1024 * 1024 ? 1 : 0)} MB`;
}

export function GoogleDrivePickerModal({
  open,
  connectionId,
  initialSelectedIds,
  onClose,
  onSelect,
}: GoogleDrivePickerModalProps) {
  const { t, lang } = useI18n();
  const [activeTab, setActiveTab] = useState<TabType>('drive');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>(initialSelectedIds);
  const [viewMode, setViewMode] = useState<'grid' | 'list'>('grid');

  const [uploading, setUploading] = useState(false);
  const [uploadedFile, setUploadedFile] = useState<{ name: string; size: number } | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const { data: driveFilesData, isLoading, isError } = useQuery({
    queryKey: ['items', 'drive-files', connectionId],
    queryFn: () => itemsApi.getItems({ types: ['File'], connectionId, limit: 100 }),
    enabled: open && !!connectionId,
  });

  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- hydrate selection khi mở modal
    setSelectedIds(initialSelectedIds);
    setUploading(false);
    setUploadedFile(null);
    setActiveTab('drive');
    setSearchQuery('');
  }, [open, initialSelectedIds]);

  const driveFiles = useMemo(() => {
    const merged = driveFilesData?.items || [];
    const seen = new Set<string>();
    return merged.filter(file => {
      if (seen.has(file.id)) return false;
      seen.add(file.id);
      return true;
    });
  }, [driveFilesData]);

  const handleToggleSelect = (fileId: string) => {
    setSelectedIds(current => {
      if (current.includes(fileId)) {
        return current.filter(id => id !== fileId);
      } else {
        return [...current, fileId];
      }
    });
  };

  const handleInsert = () => {
    onSelect(selectedIds, { uploadedThisSessionIds: [] });
    onClose();
  };

  // Helper to parse file metadata
  const getFileMetadata = (file: ItemResponse) => {
    try {
      return file.metadataJson ? JSON.parse(file.metadataJson) : {};
    } catch {
      return {};
    }
  };

  // Filter and sort files based on tab and search
  const filteredFiles = useMemo(() => {
    let result = [...driveFiles];

    // Search query filter
    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase().trim();
      result = result.filter(f => f.title.toLowerCase().includes(q));
    }

    // Tab filter
    if (activeTab === 'drive') {
      // Sort by date key descending
      result.sort((a, b) => new Date(b.occurredAt).getTime() - new Date(a.occurredAt).getTime());
    } else {
      return [];
    }

    return result;
  }, [driveFiles, activeTab, searchQuery]);

  const handleUpload = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    const validationKey = validateDriveUploadFiles([file]);
    if (validationKey) {
      toast.error(t(validationKey));
      e.target.value = '';
      return;
    }
    if (!connectionId) {
      toast.error(lang === 'vi' ? 'Chưa có kết nối Google Drive.' : 'Google Drive connection is missing.');
      e.target.value = '';
      return;
    }

    setUploading(true);
    setUploadedFile({ name: file.name, size: file.size });
    try {
      // Upload file lên Drive trước, rồi trả itemId mới về Calendar modal.
      const uploaded = await driveApi.uploadFile({ connectionId, file });
      toast.success(lang === 'vi' ? 'Đã tải tệp lên Drive.' : 'File uploaded to Drive.');
      onSelect(
        Array.from(new Set([...selectedIds, uploaded.id])),
        // Đánh dấu file mới upload để khi lưu event sẽ hỏi quyền cho guest.
        { uploadedThisSessionIds: [uploaded.id] },
      );
      onClose();
    } catch (error) {
      handleApiError(error, lang === 'vi' ? 'Không tải được tệp lên Drive.' : 'Could not upload the file to Drive.');
    } finally {
      setUploading(false);
      setUploadedFile(null);
      e.target.value = '';
    }
  };

  if (!open) return null;

  // Helper to determine file icon and color
  const getFileIconInfo = (mimeType?: string) => {
    if (!mimeType) return { icon: <FileText className="w-4 h-4 text-slate-400" />, color: 'bg-slate-100', textCls: 'text-slate-500' };
    const mt = mimeType.toLowerCase();

    if (mt.includes('document') || mt.includes('word') || mt.includes('google-apps.document')) {
      return { icon: <FileText className="w-4 h-4 text-blue-500" />, color: 'bg-blue-50 dark:bg-blue-950/20', textCls: 'text-blue-500' };
    }
    if (mt.includes('spreadsheet') || mt.includes('excel') || mt.includes('google-apps.spreadsheet')) {
      return { icon: <FileText className="w-4 h-4 text-emerald-500" />, color: 'bg-emerald-50 dark:bg-emerald-950/20', textCls: 'text-emerald-500' };
    }
    if (mt.includes('presentation') || mt.includes('powerpoint') || mt.includes('google-apps.presentation')) {
      return { icon: <FileText className="w-4 h-4 text-amber-500" />, color: 'bg-amber-50 dark:bg-amber-950/20', textCls: 'text-amber-500' };
    }
    if (mt.includes('pdf')) {
      return { icon: <FileText className="w-4 h-4 text-rose-500" />, color: 'bg-rose-50 dark:bg-rose-950/20', textCls: 'text-rose-500' };
    }
    if (mt.includes('video') || mt.includes('mp4')) {
      return { icon: <FileText className="w-4 h-4 text-red-500" />, color: 'bg-red-50 dark:bg-red-950/20', textCls: 'text-red-500' };
    }
    return { icon: <FileText className="w-4 h-4 text-slate-400" />, color: 'bg-slate-50 dark:bg-slate-800/40', textCls: 'text-slate-400' };
  };

  const tabs = [
    { id: 'drive', label: lang === 'vi' ? 'Tệp từ Drive' : 'Files from Drive' },
    { id: 'computer', label: lang === 'vi' ? 'Từ máy tính' : 'From computer' },
  ] as const;

  return (
    <div 
      className="fixed inset-0 z-[10000] flex items-center justify-center bg-slate-900/45 p-3 backdrop-blur-sm"
      onMouseDown={e => {
        e.stopPropagation();
        onClose();
      }}
    >
      <div 
        className="flex h-[78vh] w-full max-w-5xl flex-col overflow-hidden rounded-xl border border-slate-200 bg-white shadow-2xl animate-in fade-in zoom-in-95 duration-200 dark:border-slate-800 dark:bg-slate-900"
        onMouseDown={e => e.stopPropagation()}
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex shrink-0 flex-col border-b border-slate-200 dark:border-slate-800">
          <div className="flex h-14 items-center justify-between gap-4 px-5">
            <div className="flex min-w-[190px] items-center gap-2.5">
              <DriveIcon className="h-5 w-5 shrink-0" />
              <h2 className="text-[15px] font-semibold text-slate-800 dark:text-slate-100">
                {lang === 'vi' ? 'Chọn từ Google Drive' : 'Pick from Google Drive'}
              </h2>
            </div>
            
            {/* Search Bar */}
            <div className={`relative flex-1 ${activeTab === 'computer' ? 'invisible' : ''}`}>
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <input
                type="text"
                value={searchQuery}
                onChange={e => setSearchQuery(e.target.value)}
                placeholder={lang === 'vi' ? 'Tìm tệp gần đây theo tên' : 'Search recent files by name'}
                className="h-9 w-full rounded-lg border border-slate-200 bg-slate-50 pl-9 pr-3 text-[13px] text-slate-900 outline-none transition placeholder:text-slate-400 focus:border-brand-500 focus:bg-white focus:ring-2 focus:ring-brand-500/15 dark:border-slate-800 dark:bg-slate-800 dark:text-slate-100 dark:focus:bg-slate-950"
              />
            </div>

            <button 
              type="button" 
              onClick={onClose} 
              className="rounded-full p-2 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800 dark:hover:text-slate-200"
            >
              <X className="h-5 w-5" />
            </button>
          </div>

          {/* Navigation Tabs */}
          <div className="flex h-11 items-end justify-between px-5">
            <div className="flex gap-7 overflow-x-auto">
              {tabs.map(tab => (
                <button
                  key={tab.id}
                  type="button"
                  onClick={() => setActiveTab(tab.id)}
                  className={`h-11 border-b-2 text-[13px] font-semibold transition-colors whitespace-nowrap ${
                    activeTab === tab.id
                      ? 'border-brand-600 text-brand-600 dark:border-brand-400 dark:text-brand-400'
                      : 'border-transparent text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'
                  }`}
                >
                  {tab.label}
                </button>
              ))}
            </div>

            {/* Layout switchers */}
            <div className={`flex items-center gap-1 pb-2 text-slate-400 dark:text-slate-500 ${activeTab === 'computer' ? 'invisible' : ''}`}>
              <button 
                type="button"
                onClick={() => setViewMode('grid')}
                className={`p-1.5 rounded hover:bg-slate-100 dark:hover:bg-slate-800 ${viewMode === 'grid' ? 'text-brand-600 dark:text-brand-400' : ''}`}
              >
                <Grid className="w-4 h-4" />
              </button>
              <button 
                type="button"
                onClick={() => setViewMode('list')}
                className={`p-1.5 rounded hover:bg-slate-100 dark:hover:bg-slate-800 ${viewMode === 'list' ? 'text-brand-600 dark:text-brand-400' : ''}`}
              >
                <List className="w-4 h-4" />
              </button>
            </div>
          </div>
        </div>

        {/* Content Panel */}
        <div className="flex-1 overflow-y-auto bg-slate-50 p-4 dark:bg-slate-950/20">
          {activeTab === 'computer' ? (
            /* UPLOAD VIEW */
            <div
              className="flex min-h-full flex-col items-center justify-center rounded-xl border-2 border-dashed border-slate-300 bg-white p-8 text-center transition-colors dark:border-slate-800 dark:bg-slate-900"
              onDragOver={e => e.preventDefault()}
              onDrop={e => {
                e.preventDefault();
                const file = e.dataTransfer.files?.[0];
                if (file) {
                  handleUpload({ target: { files: [file], value: '' } } as unknown as ChangeEvent<HTMLInputElement>);
                }
              }}
            >
              <div 
                className="flex w-full max-w-md flex-col items-center justify-center"
              >
                <input
                  type="file"
                  ref={fileInputRef}
                  onChange={handleUpload}
                  className="hidden"
                />
                
                {uploading ? (
                  <div className="w-full space-y-4">
                    <Loader2 className="w-10 h-10 animate-spin text-brand-600 mx-auto" />
                    <div className="text-[14px] font-medium text-slate-700 dark:text-slate-200">
                      {lang === 'vi' ? `Đang tải lên ${uploadedFile?.name}...` : `Uploading ${uploadedFile?.name}...`}
                    </div>
                    <div className="w-full bg-slate-100 dark:bg-slate-800 rounded-full h-2 max-w-xs mx-auto overflow-hidden">
                      <div 
                        className="bg-brand-600 h-full rounded-full animate-pulse" 
                        style={{ width: '70%' }}
                      />
                    </div>
                    <div className="text-[11.5px] text-slate-400 dark:text-slate-500">
                      {lang === 'vi' ? 'Đang gửi lên Google Drive' : 'Sending to Google Drive'}
                    </div>
                  </div>
                ) : (
                  <>
                    <CloudUpload className="mb-4 h-16 w-16 stroke-[1.1] text-slate-300 dark:text-slate-700" />
                    <h3 className="mb-1 text-[14.5px] font-semibold text-slate-800 dark:text-slate-200">
                      {lang === 'vi' ? 'Kéo tệp vào đây' : 'Drag files here'}
                    </h3>
                    <p className="mb-5 text-[12.5px] text-slate-400 dark:text-slate-500">
                      {lang === 'vi' ? 'Hoặc chọn tệp trực tiếp từ thiết bị của bạn' : 'Or select files from your computer'}
                    </p>
                    <button
                      type="button"
                      onClick={() => fileInputRef.current?.click()}
                      className="h-9 rounded-full bg-brand-600 px-5 text-[13px] font-semibold text-white transition-all hover:bg-brand-700 hover:shadow-md"
                    >
                      {lang === 'vi' ? 'Chọn tệp thiết bị' : 'Select files from device'}
                    </button>
                  </>
                )}
              </div>
            </div>
          ) : isLoading ? (
            <div className="h-full flex flex-col items-center justify-center text-slate-400">
              <Loader2 className="w-8 h-8 animate-spin text-brand-600 mb-2" />
              <span className="text-[13px]">{t('common.loading')}</span>
            </div>
          ) : isError ? (
            <div className="h-full flex flex-col items-center justify-center text-slate-500 gap-3">
              <AlertCircle className="w-10 h-10 text-rose-500" />
              <span className="text-[13.5px]">{t('calendar.loadFailed')}</span>
            </div>
          ) : filteredFiles.length === 0 ? (
            <div className="h-full flex flex-col items-center justify-center text-slate-400 dark:text-slate-500">
              <FileText className="w-12 h-12 stroke-[1.5] mb-2 text-slate-300 dark:text-slate-700" />
              <span className="text-[13.5px]">
                {lang === 'vi' ? 'Không tìm thấy tệp nào' : 'No files found'}
              </span>
            </div>
          ) : viewMode === 'grid' ? (
            /* GRID VIEW */
            <div className="space-y-6">
              <div>
                <h3 className="mb-3 text-[12px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">
                  {searchQuery ? (lang === 'vi' ? 'Kết quả tìm kiếm' : 'Search results') : (lang === 'vi' ? 'Tệp gần đây' : 'Recent files')}
                </h3>
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
                  {filteredFiles.map(file => {
                    const isSelected = selectedIds.includes(file.id);
                    const meta = getFileMetadata(file);
                    const iconInfo = getFileIconInfo(meta.mimeType);

                    return (
                      <div
                        key={file.id}
                        onClick={() => handleToggleSelect(file.id)}
                        className={`group relative flex flex-col overflow-hidden rounded-lg border bg-white shadow-sm transition-all cursor-pointer dark:bg-slate-900 ${
                          isSelected
                            ? 'border-brand-500 ring-2 ring-brand-500/10 bg-brand-50/5'
                            : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700'
                        }`}
                      >
                        {/* Thumbnail area */}
                        <div className="relative flex aspect-[5/3] items-center justify-center border-b border-slate-100 bg-slate-50 dark:border-slate-800 dark:bg-slate-800/40">
                          {isSelected && (
                            <div className="absolute top-2.5 right-2.5 z-10 w-5 h-5 rounded-full bg-brand-600 text-white flex items-center justify-center shadow-md animate-in zoom-in duration-100">
                              <Check className="w-3 h-3 stroke-[3]" />
                            </div>
                          )}
                          
                          {/* File Preview Mockup */}
                          <div className="flex h-12 w-9 flex-col justify-between rounded border border-slate-200 bg-white p-1 shadow-sm dark:border-slate-700 dark:bg-slate-900">
                            <div className="flex items-center justify-between">
                              <div className="h-1 w-4 rounded bg-slate-200 dark:bg-slate-700" />
                              {iconInfo.icon}
                            </div>
                            <div className="space-y-1">
                              <div className="w-full h-1 rounded bg-slate-100 dark:bg-slate-800" />
                              <div className="w-3/4 h-1 rounded bg-slate-100 dark:bg-slate-800" />
                              <div className="w-1/2 h-1 rounded bg-slate-100 dark:bg-slate-800" />
                            </div>
                          </div>
                        </div>

                        {/* Card Footer Info */}
                        <div className="flex items-center gap-2 px-2.5 py-2">
                          <span className={`rounded-md p-1 ${iconInfo.color}`}>
                            {iconInfo.icon}
                          </span>
                          <span className="flex-1 truncate text-[12.5px] font-medium text-slate-700 dark:text-slate-200" title={file.title}>
                            {file.title}
                          </span>
                          {formatFileSize(Number(meta.size)) && (
                            <span className="shrink-0 text-[11px] text-slate-400">{formatFileSize(Number(meta.size))}</span>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            </div>
          ) : (
            /* LIST VIEW */
            <div className="overflow-hidden rounded-lg border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="border-b border-slate-100 bg-slate-50 text-[12px] font-semibold text-slate-400 dark:border-slate-800 dark:bg-slate-900/60 dark:text-slate-500">
                    <th className="w-10 px-3 py-2 text-center"></th>
                    <th className="py-2.5 px-2">{lang === 'vi' ? 'Tên' : 'Name'}</th>
                    <th className="w-36 px-3 py-2.5">{lang === 'vi' ? 'Ngày sửa đổi' : 'Last modified'}</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredFiles.map(file => {
                    const isSelected = selectedIds.includes(file.id);
                    const meta = getFileMetadata(file);
                    const iconInfo = getFileIconInfo(meta.mimeType);

                    return (
                      <tr
                        key={file.id}
                        onClick={() => handleToggleSelect(file.id)}
                        className={`border-b border-slate-100 dark:border-slate-850 last:border-b-0 cursor-pointer text-[13px] transition-colors ${
                          isSelected
                            ? 'bg-brand-50/15 hover:bg-brand-50/25 dark:bg-brand-500/5 dark:hover:bg-brand-500/10'
                            : 'hover:bg-slate-50 dark:hover:bg-slate-800/40'
                        }`}
                      >
                        <td className="px-3 py-2.5 text-center">
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => {}} // handled by row click
                            className="w-4 h-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500 dark:border-slate-700 dark:bg-slate-800"
                          />
                        </td>
                        <td className="flex items-center gap-2.5 px-2 py-2.5 font-medium text-slate-700 dark:text-slate-200">
                          <span className={`p-1 rounded ${iconInfo.color}`}>
                            {iconInfo.icon}
                          </span>
                          <span className="truncate max-w-[450px]" title={file.title}>{file.title}</span>
                        </td>
                        <td className="px-3 py-2.5 text-slate-400 dark:text-slate-500">
                          {new Date(file.occurredAt).toLocaleDateString(lang === 'vi' ? 'vi-VN' : 'en-US')}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {/* Footer */}
        {activeTab === 'drive' && (
          <div className="flex shrink-0 justify-end gap-2.5 border-t border-slate-100 bg-white px-5 py-3 dark:border-slate-800 dark:bg-slate-900">
            <button 
              type="button" 
              onClick={onClose} 
              className="h-9 px-4 rounded-full border border-slate-200 dark:border-slate-700 text-[13px] font-semibold text-slate-600 dark:text-slate-450 hover:bg-slate-50 dark:hover:bg-slate-800 transition-colors"
            >
              {t('common.cancel')}
            </button>
            <button
              type="button"
              onClick={handleInsert}
              disabled={selectedIds.length === 0}
              className="h-9 px-6 rounded-full bg-brand-600 hover:bg-brand-700 disabled:bg-slate-100 disabled:text-slate-400 dark:disabled:bg-slate-800 dark:disabled:text-slate-600 text-white text-[13px] font-semibold transition-all hover:shadow-md active:shadow-none"
            >
              {lang === 'vi' ? `Chèn (${selectedIds.length})` : `Insert (${selectedIds.length})`}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
