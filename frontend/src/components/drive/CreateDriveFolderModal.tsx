import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Loader2, Folder } from 'lucide-react';
import toast from 'react-hot-toast';
import { driveApi } from '../../lib/driveApi';
import { connectionsApi } from '../../lib/connectionsApi';
import { itemsApi } from '../../lib/itemsApi';
import { handleApiError } from '../../lib/errorUtils';
import { useI18n } from '../../hooks/useI18n';
import { Select } from '../Select';

interface Props {
  isOpen: boolean;
  onClose: () => void;
  /** Gợi ý connection Drive (từ Integrations card) */
  defaultConnectionId?: string;
  /** Gợi ý folder cha (từ ItemDetail khi đang xem folder Drive) */
  defaultParentItemId?: string | null;
}

type BodyProps = Omit<Props, 'isOpen'>;

/** SCRUM-79 B3 — tạo folder trên Google Drive. */
export function CreateDriveFolderModal({
  isOpen,
  onClose,
  defaultConnectionId,
  defaultParentItemId = null,
}: Props) {
  // Unmount body khi đóng → reset form, không cần useEffect setState
  if (!isOpen) return null;

  return (
    <CreateDriveFolderModalBody
      onClose={onClose}
      defaultConnectionId={defaultConnectionId}
      defaultParentItemId={defaultParentItemId}
    />
  );
}

function CreateDriveFolderModalBody({
  onClose,
  defaultConnectionId,
  defaultParentItemId = null,
}: BodyProps) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { t } = useI18n();

  const [name, setName] = useState('');
  const [userConnectionId, setUserConnectionId] = useState<string | undefined>();

  const { data: connections = [] } = useQuery({
    queryKey: ['connections'],
    queryFn: connectionsApi.getConnections,
  });

  const driveConnections = useMemo(
    () => connections.filter((c) => c.serviceType === 'Drive' && c.status === 'Active'),
    [connections],
  );

  const connectionId = useMemo(() => {
    if (userConnectionId) return userConnectionId;
    if (defaultConnectionId && driveConnections.some((c) => c.id === defaultConnectionId)) {
      return defaultConnectionId;
    }
    return driveConnections[0]?.id ?? '';
  }, [userConnectionId, defaultConnectionId, driveConnections]);

  // KHÔNG cho user chọn folder cha — tạo thẳng vào context hiện tại (folder đang mở / detail),
  // rỗng = My Drive gốc. Chỉ lấy TÊN folder cha để hiện hint "sẽ tạo ở đâu" (reuse cache ['item', id]).
  const parentItemId = defaultParentItemId ?? '';
  const { data: parentItem } = useQuery({
    queryKey: ['item', parentItemId],
    queryFn: () => itemsApi.getItemById(parentItemId),
    enabled: !!parentItemId,
  });

  const targetHint = parentItemId
    ? (parentItem?.title
        ? t('drive.createFolder.targetNamed').replace('{name}', parentItem.title)
        : t('drive.createFolder.targetCurrent'))
    : t('drive.createFolder.targetRoot');

  const createMutation = useMutation({
    mutationFn: () =>
      driveApi.createFolder({
        connectionId,
        name: name.trim(),
        parentItemId: parentItemId || null,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['items'] });
      toast.success(t('drive.createFolder.created'));
      onClose();
    },
    onError: (err) => handleApiError(err, t('drive.createFolder.createFail'), { navigate }),
  });

  if (driveConnections.length === 0) {
    return (
      <div className="fixed inset-0 z-[60] flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-sm">
        <div className="bg-white dark:bg-slate-900 rounded-xl p-6 max-w-sm shadow-xl text-center">
          <p className="text-sm text-slate-600 dark:text-slate-300 mb-4">{t('drive.createFolder.noConnection')}</p>
          <button type="button" onClick={onClose} className="px-4 py-2 text-sm font-medium text-brand-600">{t('common.close')}</button>
        </div>
      </div>
    );
  }

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-sm">
      <div className="bg-white dark:bg-slate-900 rounded-xl w-full max-w-xl shadow-xl flex flex-col">
        <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-800">
          <h2 className="text-[16px] font-semibold text-slate-900 dark:text-slate-100">{t('drive.createFolder.title')}</h2>
        </div>
        <div className="p-5 pb-8 space-y-4">
          {driveConnections.length > 1 && (
            <div>
              <label className="block text-[13px] font-medium mb-1.5">{t('drive.createFolder.connection')}</label>
              <Select
                value={connectionId}
                onChange={setUserConnectionId}
                options={driveConnections.map((c) => ({
                  value: c.id,
                  label: c.providerAccountId || c.id,
                }))}
                className="h-9"
              />
            </div>
          )}
          <div>
            <label className="block text-[13px] font-medium mb-1.5">{t('drive.createFolder.name')}</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t('drive.createFolder.namePlaceholder')}
              className="w-full h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-[13px]"
              autoFocus
              onKeyDown={(e) => {
                if (e.key === 'Enter' && name.trim() && connectionId && !createMutation.isPending) {
                  createMutation.mutate();
                }
              }}
            />
          </div>
          {/* Tạo thẳng vào context hiện tại — chỉ báo nơi tạo, không cho chọn. */}
          <p className="flex items-center gap-1.5 text-[12px] text-slate-500 dark:text-slate-400">
            <Folder className="w-3.5 h-3.5 shrink-0 text-slate-400 dark:text-slate-500" />
            {targetHint}
          </p>
        </div>
        <div className="px-5 py-4 border-t border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-800 rounded-b-xl flex justify-end gap-2">
          <button type="button" onClick={onClose} className="px-4 py-2 text-[13px] font-medium text-slate-600">{t('common.cancel')}</button>
          <button
            type="button"
            onClick={() => createMutation.mutate()}
            disabled={!name.trim() || !connectionId || createMutation.isPending}
            className="px-4 py-2 rounded-lg bg-brand-600 text-white text-[13px] font-medium hover:bg-brand-700 disabled:opacity-50 inline-flex items-center gap-2"
          >
            {createMutation.isPending && <Loader2 className="w-4 h-4 animate-spin" />}
            {createMutation.isPending ? t('drive.createFolder.creating') : t('drive.createFolder.create')}
          </button>
        </div>
      </div>
    </div>
  );
}
