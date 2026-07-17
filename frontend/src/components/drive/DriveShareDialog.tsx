import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Loader2, X, UserPlus, Link2 } from 'lucide-react';
import toast from 'react-hot-toast';
import { driveApi, getLinkRestrictConflictFromError } from '../../lib/driveApi';
import { handleApiError } from '../../lib/errorUtils';
import { useI18n } from '../../hooks/useI18n';
import { Select } from '../Select';
import { ConfirmDialog } from '../ConfirmDialog';
import { DriveLinkRestrictDialog } from './DriveLinkRestrictDialog';
import type { DriveLinkRestrictConflict, DrivePermission, DrivePermissionRole } from '../../types/drive';

interface Props {
  itemId: string;
  itemTitle?: string;
  isOpen: boolean;
  onClose: () => void;
}

const ROLE_OPTIONS: DrivePermissionRole[] = ['reader', 'commenter', 'writer'];

function roleLabelKey(role: DrivePermissionRole) {
  if (role === 'commenter') return 'drive.share.roleCommenter' as const;
  if (role === 'writer') return 'drive.share.roleWriter' as const;
  return 'drive.share.roleReader' as const;
}

/** SCRUM-79 B2 — dialog chia sẻ file/folder Drive (gọi /api/drive/items/{id}/permissions). */
export function DriveShareDialog({ itemId, itemTitle, isOpen, onClose }: Props) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { t } = useI18n();
  const [email, setEmail] = useState('');
  const [inviteRole, setInviteRole] = useState<DrivePermissionRole>('reader');
  const [notify, setNotify] = useState(true);
  // Permission đang chờ xác nhận gỡ (null = đóng dialog).
  const [removeTargetId, setRemoveTargetId] = useState<string | null>(null);
  // Case 1 — conflict tắt link khi folder mẹ đang public (popup giống Drive).
  const [linkRestrictConflict, setLinkRestrictConflict] = useState<DriveLinkRestrictConflict | null>(null);
  // Đang GET restrict-conflict trước khi tắt — chặn spam click.
  const [isPreviewingRestrict, setIsPreviewingRestrict] = useState(false);

  const permissionsQuery = useQuery({
    queryKey: ['drive-permissions', itemId],
    queryFn: () => driveApi.listPermissions(itemId),
    enabled: isOpen && !!itemId,
  });
  const permissions = permissionsQuery.data?.items ?? [];
  const linkPerm = permissions.find((p) => p.isLink);
  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['drive-permissions', itemId] });
  };

  const inviteMutation = useMutation({
    mutationFn: () =>
      driveApi.addPermission(itemId, { email: email.trim(), role: inviteRole, notify }),
    onSuccess: () => {
      setEmail('');
      invalidate();
      toast.success(t('drive.share.invited'));
    },
    onError: (err) => handleApiError(err, t('drive.share.loadError'), { navigate }),
  });
  const updateRoleMutation = useMutation({
    mutationFn: ({ permId, role }: { permId: string; role: DrivePermissionRole }) =>
      driveApi.updatePermission(itemId, permId, { role }),
    onSuccess: () => {
      invalidate();
      toast.success(t('drive.share.updated'));
    },
    onError: (err) => handleApiError(err, t('drive.share.loadError'), { navigate }),
  });
  const removeMutation = useMutation({
    mutationFn: (permId: string) => driveApi.removePermission(itemId, permId),
    onSuccess: () => {
      invalidate();
      toast.success(t('drive.share.removed'));
    },
    onError: (err) => handleApiError(err, t('drive.share.loadError'), { navigate }),
  });
  const linkMutation = useMutation({
    mutationFn: (payload: {
      enabled: boolean;
      role: DrivePermissionRole;
      confirmRestrictParent?: boolean;
      /** Folder mẹ — cập nhật cache khi Case 1 confirm. */
      parentItemId?: string | null;
    }) =>
      driveApi.setLinkSharing(itemId, {
        enabled: payload.enabled,
        role: payload.role,
        confirmRestrictParent: payload.confirmRestrictParent,
      }),
    onSuccess: async (data, vars) => {
      const parentItemId = vars.parentItemId ?? null;
      setLinkRestrictConflict(null);

      /** Bỏ dòng anyone khỏi cache permissions. */
      const stripLink = (old: { items: DrivePermission[] } | undefined) => ({
        items: (old?.items ?? []).filter((p) => !p.isLink),
      });

      // Huỷ refetch đang bay — tránh Google còn trả anyone ghi đè UI vừa tắt.
      await queryClient.cancelQueries({ queryKey: ['drive-permissions', itemId] });
      if (parentItemId) {
        await queryClient.cancelQueries({ queryKey: ['drive-permissions', parentItemId] });
      }

      if (vars.enabled) {
        queryClient.setQueryData(
          ['drive-permissions', itemId],
          (old: { items: DrivePermission[] } | undefined) => {
            const withoutLink = (old?.items ?? []).filter((p) => !p.isLink);
            const linkRow: DrivePermission = data ?? {
              id: 'link-optimistic',
              type: 'anyone',
              role: vars.role,
              isOwner: false,
              isLink: true,
            };
            return { items: [...withoutLink, linkRow] };
          },
        );
        void queryClient.invalidateQueries({ queryKey: ['drive-permissions', itemId] });
      } else {
        // Tắt link: ghi cache tắt ngay cho file (+ folder mẹ nếu Case 1).
        queryClient.setQueryData(['drive-permissions', itemId], stripLink);
        if (parentItemId) {
          queryClient.setQueryData(['drive-permissions', parentItemId], stripLink);
        }

        if (vars.confirmRestrictParent) {
          // Google list còn anyone vài giây sau delete — trì hoãn refetch.
          window.setTimeout(() => {
            void queryClient.invalidateQueries({ queryKey: ['drive-permissions', itemId] });
            if (parentItemId) {
              void queryClient.invalidateQueries({ queryKey: ['drive-permissions', parentItemId] });
            }
          }, 2500);
        } else {
          void queryClient.invalidateQueries({ queryKey: ['drive-permissions', itemId] });
        }
      }

      toast.success(
        vars.confirmRestrictParent
          ? t('drive.share.linkOffWithParent')
          : vars.enabled
            ? t('drive.share.linkOn')
            : t('drive.share.linkOff'),
      );
    },
    onError: (err, vars) => {
      // Case 1: BE 409 + conflict → hiện popup Drive, không toast lỗi.
      if (!vars.enabled && !vars.confirmRestrictParent) {
        const conflict = getLinkRestrictConflictFromError(err);
        if (conflict) {
          setLinkRestrictConflict(conflict);
          return;
        }
      }
      // Không navigate /integrations — 403 gỡ share thường là kế thừa quyền, không phải thiếu scope.
      handleApiError(err, t('drive.share.loadError'));
    },
  });

  const roleSelectOptions = ROLE_OPTIONS.map((r) => ({
    value: r,
    label: t(roleLabelKey(r)),
  }));

  // Toggle: ưu tiên mutation đang chạy; sau success giữ giá trị trong lúc refetch.
  const serverLinkEnabled = !!linkPerm;
  const serverLinkRole: DrivePermissionRole =
    linkPerm && ROLE_OPTIONS.includes(linkPerm.role as DrivePermissionRole)
      ? (linkPerm.role as DrivePermissionRole)
      : 'reader';

  const mutationLink =
    linkMutation.variables != null &&
    (linkMutation.isPending ||
      (linkMutation.isSuccess && permissionsQuery.isFetching))
      ? linkMutation.variables
      : null;

  const linkEnabled = mutationLink?.enabled ?? serverLinkEnabled;
  const linkRole = mutationLink?.role ?? serverLinkRole;

  const handleToggleLink = (enabled: boolean) => {
    // Tránh double-click làm toggle nháy khi đang gọi API / refetch / preview.
    if (linkMutation.isPending || permissionsQuery.isFetching || isPreviewingRestrict) return;

    // Tắt link: preview Case 1 trước — có conflict thì mở popup, KHÔNG nháy toggle tắt.
    if (!enabled) {
      void (async () => {
        setIsPreviewingRestrict(true);
        try {
          const conflict = await driveApi.getLinkRestrictConflict(itemId);
          if (conflict) {
            setLinkRestrictConflict(conflict);
            return;
          }
        } catch {
          // Preview lỗi → vẫn thử PUT (BE vẫn 409 nếu Case 1).
        } finally {
          setIsPreviewingRestrict(false);
        }
        linkMutation.mutate({ enabled: false, role: linkRole });
      })();
      return;
    }

    linkMutation.mutate({ enabled: true, role: linkRole });
  };
  const handleLinkRoleChange = (role: string) => {
    if (linkMutation.isPending) return;
    linkMutation.mutate({ enabled: true, role: role as DrivePermissionRole });
  };

  const renderPermissionRow = (perm: DrivePermission) => {
    const label = perm.displayName || perm.emailAddress || perm.type;
    const isBusy =
      updateRoleMutation.isPending || removeMutation.isPending || linkMutation.isPending;
    return (
      <div
        key={perm.id}
        className="flex items-center gap-2 py-2 border-b border-slate-100 dark:border-slate-800 last:border-0"
      >
        <div className="flex-1 min-w-0">
          <div className="text-[13px] font-medium text-slate-800 dark:text-slate-100 truncate">{label}</div>
          {perm.emailAddress && perm.displayName && (
            <div className="text-[11px] text-slate-500 dark:text-slate-400 truncate">{perm.emailAddress}</div>
          )}
          {perm.isOwner && (
            <span className="text-[10px] font-semibold uppercase text-slate-400">{t('drive.share.owner')}</span>
          )}
          {perm.isLink && (
            <span className="text-[10px] font-semibold uppercase text-brand-600 dark:text-brand-400">{t('drive.share.linkBadge')}</span>
          )}
        </div>
        {perm.isOwner ? (
          <span className="text-[12px] text-slate-500 shrink-0">{t('drive.share.roleOwner')}</span>
        ) : perm.isLink ? (
          <span className="text-[12px] text-slate-500 shrink-0">{t(roleLabelKey(perm.role as DrivePermissionRole))}</span>
        ) : (
          <>
            <div className="w-[152px] shrink-0">
              <Select
                value={perm.role}
                onChange={(v) => updateRoleMutation.mutate({ permId: perm.id, role: v as DrivePermissionRole })}
                options={roleSelectOptions}
                disabled={isBusy || !ROLE_OPTIONS.includes(perm.role as DrivePermissionRole)}
                className="h-8 text-[12px]"
                dropUp
              />
            </div>
            <button
              type="button"
              onClick={() => setRemoveTargetId(perm.id)}
              disabled={isBusy}
              className="text-[12px] font-medium text-rose-600 hover:underline shrink-0 disabled:opacity-50"
            >
              {t('drive.share.remove')}
            </button>
          </>
        )}
      </div>
    );
  };

  if (!isOpen) return null;
  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center p-4 bg-slate-900/40 backdrop-blur-sm">
      <div className="bg-white dark:bg-slate-900 rounded-xl w-full max-w-xl shadow-xl flex flex-col max-h-[min(90vh,720px)]">
        <div className="px-5 py-4 border-b border-slate-200 dark:border-slate-800 flex justify-between items-start gap-3 shrink-0">
          <div className="min-w-0">
            <h2 className="text-[16px] font-semibold text-slate-900 dark:text-slate-100">{t('drive.share.title')}</h2>
            {itemTitle && (
              <p className="text-[12px] text-slate-500 mt-0.5 truncate max-w-[360px]">{itemTitle}</p>
            )}
          </div>
          <button type="button" onClick={onClose} className="p-1 rounded-lg text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800">
            <X className="w-5 h-5" />
          </button>
        </div>
        <div className="p-5 pb-8 space-y-5 overflow-y-auto flex-1 min-h-0">
          {/* Mời email */}
          <div className="space-y-2">
            <label className="block text-[13px] font-medium text-slate-700 dark:text-slate-200">{t('drive.share.invite')}</label>
            <div className="flex gap-2 flex-wrap">
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder={t('drive.share.emailPlaceholder')}
                className="flex-1 min-w-[160px] h-9 px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-[13px]"
              />
              <div className="w-[152px]">
                <Select value={inviteRole} onChange={(v) => setInviteRole(v as DrivePermissionRole)} options={roleSelectOptions} className="h-9" />
              </div>
              <button
                type="button"
                onClick={() => inviteMutation.mutate()}
                disabled={!email.trim() || inviteMutation.isPending}
                className="h-9 px-3 inline-flex items-center gap-1.5 rounded-lg bg-brand-600 text-white text-[13px] font-medium hover:bg-brand-700 disabled:opacity-50"
              >
                {inviteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : <UserPlus className="w-4 h-4" />}
                {t('drive.share.inviteBtn')}
              </button>
            </div>
            <label className="inline-flex items-center gap-2 text-[12px] text-slate-600 cursor-pointer">
              <input type="checkbox" checked={notify} onChange={(e) => setNotify(e.target.checked)} className="rounded" />
              {t('drive.share.notify')}
            </label>
          </div>
          {/* Link anyone */}
          <div className="rounded-lg border border-slate-200 dark:border-slate-700 p-3 space-y-2">
            <div className="flex items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                <Link2 className="w-4 h-4 text-slate-500" />
                <span className="text-[13px] font-medium">{t('drive.share.linkSharing')}</span>
              </div>
              <button
                type="button"
                onClick={() => handleToggleLink(!linkEnabled)}
                disabled={linkMutation.isPending || isPreviewingRestrict}
                className={`relative w-10 h-5 rounded-full transition-colors ${linkEnabled ? 'bg-brand-600' : 'bg-slate-300'}`}
              >
                <span className={`absolute top-0.5 left-0.5 w-4 h-4 rounded-full bg-white transition-transform ${linkEnabled ? 'translate-x-5' : ''}`} />
              </button>
            </div>
            <p className="text-[11px] text-slate-500">{t('drive.share.linkSharingHint')}</p>
            {linkEnabled && (
              <div className="w-[152px]">
                <Select value={linkRole} onChange={handleLinkRoleChange} options={roleSelectOptions} className="h-8" disabled={linkMutation.isPending} />
              </div>
            )}
          </div>
          {/* Danh sách quyền */}
          <div>
            <h3 className="text-[12px] font-semibold uppercase text-slate-500 mb-2">{t('drive.share.peopleWithAccess')}</h3>
            {permissionsQuery.isLoading ? (
              <div className="flex justify-center py-6"><Loader2 className="w-6 h-6 animate-spin text-brand-600" /></div>
            ) : permissionsQuery.isError ? (
              <p className="text-[13px] text-rose-600">{t('drive.share.loadError')}</p>
            ) : permissions.length === 0 ? (
              <p className="text-[13px] text-slate-500">{t('drive.share.empty')}</p>
            ) : (
              <div>{permissions.map(renderPermissionRow)}</div>
            )}
          </div>
        </div>
        <div className="px-5 py-3 border-t border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-800 rounded-b-xl flex justify-end shrink-0">
          <button type="button" onClick={onClose} className="px-4 py-2 text-[13px] font-medium text-slate-600">{t('common.close')}</button>
        </div>
      </div>

      {/* Xác nhận gỡ quyền chia sẻ */}
      <ConfirmDialog
        open={removeTargetId !== null}
        tone="danger"
        message={t('drive.share.confirmRemove')}
        confirmLabel={t('drive.share.remove')}
        loading={removeMutation.isPending}
        onConfirm={() => {
          if (removeTargetId) {
            removeMutation.mutate(removeTargetId, { onSettled: () => setRemoveTargetId(null) });
          }
        }}
        onCancel={() => setRemoveTargetId(null)}
      />

      {/* Case 1 — popup giống Google Drive khi tắt link file trong folder public */}
      <DriveLinkRestrictDialog
        open={linkRestrictConflict !== null}
        conflict={linkRestrictConflict}
        loading={linkMutation.isPending && !!linkMutation.variables?.confirmRestrictParent}
        onCancel={() => setLinkRestrictConflict(null)}
        onConfirm={() => {
          const parentItemId = linkRestrictConflict?.parentItemId ?? null;
          linkMutation.mutate({
            enabled: false,
            role: linkRole,
            confirmRestrictParent: true,
            parentItemId,
          });
        }}
      />
    </div>
  );
}
