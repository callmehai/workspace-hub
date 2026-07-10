import { useState, useMemo, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { X, Search, Grid, List, FileText, Check, AlertCircle, Loader2, CloudUpload, Laptop } from 'lucide-react';
import { DriveIcon } from '../../lib/brandIcons';
import { itemsApi } from '../../lib/itemsApi';
import { useI18n } from '../../hooks/useI18n';
import type { ItemResponse } from '../../types/items';

interface GoogleDrivePickerModalProps {
  open: boolean;
  connectionId: string;
  initialSelectedIds: string[];
  onClose: () => void;
  onSelect: (selectedIds: string[]) => void;
}

type TabType = 'drive' | 'computer';

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

  // Simulated upload states
  const [uploading, setUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [uploadedFile, setUploadedFile] = useState<{ name: string; size: number } | null>(null);
  const [localMockedFiles, setLocalMockedFiles] = useState<ItemResponse[]>([]);
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Fetch Drive files
  const { data: driveFilesData, isLoading, isError } = useQuery({
    queryKey: ['items', 'drive-files', connectionId],
    queryFn: () => itemsApi.getItems({ types: ['File'], limit: 100 }),
    enabled: open && !!connectionId,
  });

  const driveFiles = useMemo(() => {
    const apiFiles = driveFilesData?.items || [];
    return [...localMockedFiles, ...apiFiles];
  }, [driveFilesData, localMockedFiles]);

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
    onSelect(selectedIds);
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

  const handleSimulatedUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    setUploading(true);
    setUploadProgress(0);
    setUploadedFile({ name: file.name, size: file.size });

    // Simulate progress upload
    let currentProgress = 0;
    const interval = setInterval(() => {
      currentProgress += 10;
      setUploadProgress(currentProgress);
      if (currentProgress >= 100) {
        clearInterval(interval);
        
        // Add new mocked file to localMockedFiles
        const newMockItem: ItemResponse = {
          id: `mock-file-${Date.now()}`,
          title: file.name,
          type: 'File',
          status: 'Inbox',
          isImportant: false,
          occurredAt: new Date().toISOString(),
          connectionId: connectionId || 'simulated-conn-id',
          metadataJson: JSON.stringify({
            mimeType: file.type || 'application/octet-stream',
            size: file.size,
            starred: false,
            shared: false,
          }),
        };

        setLocalMockedFiles(prev => [newMockItem, ...prev]);
        setSelectedIds(prev => [...prev, newMockItem.id]);
        
        // Finalize state and switch tab
        setTimeout(() => {
          setUploading(false);
          setUploadedFile(null);
          setActiveTab('drive'); // switch to drive tab to view it
        }, 500);
      }
    }, 150);
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
      className="fixed inset-0 z-[10000] flex items-center justify-center bg-slate-900/50 p-4 backdrop-blur-sm"
      onMouseDown={e => {
        e.stopPropagation();
        onClose();
      }}
    >
      <div 
        className="w-full max-w-4.5xl h-[85vh] flex flex-col rounded-2xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 shadow-2xl overflow-hidden animate-in fade-in zoom-in-95 duration-200"
        onMouseDown={e => e.stopPropagation()}
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex flex-col border-b border-slate-100 dark:border-slate-800 px-5 pt-4 pb-0 shrink-0">
          <div className="flex items-center justify-between gap-4 mb-3.5">
            <div className="flex items-center gap-3">
              <DriveIcon className="w-6 h-6 shrink-0" />
              <h2 className="text-[17px] font-medium text-slate-800 dark:text-slate-100">
                {lang === 'vi' ? 'Chọn từ Google Drive' : 'Pick from Google Drive'}
              </h2>
            </div>
            
            {/* Search Bar */}
            <div className="flex-1 max-w-xl relative">
              <Search className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
              <input
                type="text"
                value={searchQuery}
                onChange={e => setSearchQuery(e.target.value)}
                placeholder={lang === 'vi' ? 'Tìm kiếm trong Drive hoặc dán URL' : 'Search in Drive or paste URL'}
                className="w-full h-10 pl-10 pr-4 rounded-full border border-slate-200 bg-slate-50 dark:border-slate-800 dark:bg-slate-800 text-[13px] text-slate-900 dark:text-slate-100 placeholder:text-slate-400 outline-none transition focus:border-brand-500 focus:bg-white dark:focus:bg-slate-950 focus:ring-2 focus:ring-brand-500/15"
              />
            </div>

            <button 
              type="button" 
              onClick={onClose} 
              className="rounded-full p-2 text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-850 hover:text-slate-700 dark:hover:text-slate-200"
            >
              <X className="w-5 h-5" />
            </button>
          </div>

          {/* Navigation Tabs */}
          <div className="flex items-end justify-between">
            <div className="flex gap-6 overflow-x-auto">
              {tabs.map(tab => (
                <button
                  key={tab.id}
                  type="button"
                  onClick={() => setActiveTab(tab.id)}
                  className={`pb-3 text-[13.5px] font-medium border-b-2 whitespace-nowrap transition-colors ${
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
            <div className="flex items-center gap-1 pb-3 text-slate-400 dark:text-slate-500">
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
        <div className="flex-1 overflow-y-auto p-5 bg-slate-50 dark:bg-slate-950/20">
          {activeTab === 'computer' ? (
            /* UPLOAD VIEW */
            <div className="h-full flex flex-col items-center justify-center p-6">
              <div 
                className="w-full max-w-lg border-2 border-dashed border-slate-300 dark:border-slate-800 rounded-2xl p-10 flex flex-col items-center justify-center text-center bg-white dark:bg-slate-900 shadow-sm"
                onDragOver={e => e.preventDefault()}
                onDrop={e => {
                  e.preventDefault();
                  const file = e.dataTransfer.files?.[0];
                  if (file) {
                    const mockEvent = { target: { files: [file] } } as any;
                    handleSimulatedUpload(mockEvent);
                  }
                }}
              >
                <input
                  type="file"
                  ref={fileInputRef}
                  onChange={handleSimulatedUpload}
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
                        className="bg-brand-600 h-full rounded-full transition-all duration-150" 
                        style={{ width: `${uploadProgress}%` }}
                      />
                    </div>
                    <div className="text-[11.5px] text-slate-400 dark:text-slate-500">
                      {uploadProgress}%
                    </div>
                  </div>
                ) : (
                  <>
                    <CloudUpload className="w-14 h-14 stroke-[1.25] text-slate-300 dark:text-slate-700 mb-4" />
                    <h3 className="text-[14.5px] font-semibold text-slate-800 dark:text-slate-200 mb-1">
                      {lang === 'vi' ? 'Kéo tệp vào đây' : 'Drag files here'}
                    </h3>
                    <p className="text-[12px] text-slate-400 dark:text-slate-500 mb-5">
                      {lang === 'vi' ? 'Hoặc chọn tệp trực tiếp từ thiết bị của bạn' : 'Or select files from your computer'}
                    </p>
                    <button
                      type="button"
                      onClick={() => fileInputRef.current?.click()}
                      className="px-5 h-9 rounded-full bg-brand-600 hover:bg-brand-700 text-white text-[13px] font-semibold transition-all hover:shadow-md"
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
                <h3 className="text-[12px] font-semibold text-slate-400 dark:text-slate-500 uppercase tracking-wider mb-3">
                  {searchQuery ? (lang === 'vi' ? 'Kết quả tìm kiếm' : 'Search results') : (lang === 'vi' ? 'Tệp của bạn' : 'Your Files')}
                </h3>
                <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-4">
                  {filteredFiles.map(file => {
                    const isSelected = selectedIds.includes(file.id);
                    const meta = getFileMetadata(file);
                    const iconInfo = getFileIconInfo(meta.mimeType);

                    return (
                      <div
                        key={file.id}
                        onClick={() => handleToggleSelect(file.id)}
                        className={`group relative flex flex-col rounded-xl bg-white dark:bg-slate-900 border cursor-pointer overflow-hidden transition-all shadow-sm ${
                          isSelected
                            ? 'border-brand-500 ring-2 ring-brand-500/10 bg-brand-50/5'
                            : 'border-slate-200 dark:border-slate-800 hover:border-slate-300 dark:hover:border-slate-700'
                        }`}
                      >
                        {/* Thumbnail area */}
                        <div className="aspect-[4/3] flex items-center justify-center bg-slate-50 dark:bg-slate-800/40 relative border-b border-slate-100 dark:border-slate-800">
                          {isSelected && (
                            <div className="absolute top-2.5 right-2.5 z-10 w-5 h-5 rounded-full bg-brand-600 text-white flex items-center justify-center shadow-md animate-in zoom-in duration-100">
                              <Check className="w-3 h-3 stroke-[3]" />
                            </div>
                          )}
                          
                          {/* File Preview Mockup */}
                          <div className={`w-12 h-16 rounded shadow-sm flex flex-col justify-between p-1.5 border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-900`}>
                            <div className="flex items-center justify-between">
                              <div className="w-5 h-1 rounded bg-slate-200 dark:bg-slate-700" />
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
                        <div className="p-3 flex items-center gap-2.5">
                          <span className={`p-1.5 rounded-lg ${iconInfo.color}`}>
                            {iconInfo.icon}
                          </span>
                          <span className="text-[13px] font-medium text-slate-700 dark:text-slate-200 truncate flex-1" title={file.title}>
                            {file.title}
                          </span>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            </div>
          ) : (
            /* LIST VIEW */
            <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl overflow-hidden shadow-sm">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="border-b border-slate-100 dark:border-slate-800 text-[12px] font-semibold text-slate-400 dark:text-slate-500 bg-slate-50 dark:bg-slate-900/60">
                    <th className="py-2.5 px-4 w-12 text-center"></th>
                    <th className="py-2.5 px-2">{lang === 'vi' ? 'Tên' : 'Name'}</th>
                    <th className="py-2.5 px-4 w-40">{lang === 'vi' ? 'Ngày sửa đổi' : 'Last modified'}</th>
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
                        <td className="py-3 px-4 text-center">
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => {}} // handled by row click
                            className="w-4 h-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500 dark:border-slate-700 dark:bg-slate-800"
                          />
                        </td>
                        <td className="py-3 px-2 flex items-center gap-2.5 font-medium text-slate-700 dark:text-slate-200">
                          <span className={`p-1 rounded ${iconInfo.color}`}>
                            {iconInfo.icon}
                          </span>
                          <span className="truncate max-w-[450px]" title={file.title}>{file.title}</span>
                        </td>
                        <td className="py-3 px-4 text-slate-400 dark:text-slate-500">
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
        <div className="px-5 py-4 border-t border-slate-100 dark:border-slate-800 bg-white dark:bg-slate-900 flex justify-end gap-2.5 shrink-0">
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
      </div>
    </div>
  );
}
