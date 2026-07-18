import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { X, Loader2, UserPlus, Users, Trash2, Shield, User } from 'lucide-react';
import { foldersApi } from '../../lib/itemsApi';
import { friendsApi } from '../../lib/friendsApi';
import { handleApiError } from '../../lib/errorUtils';
import { useI18n } from '../../hooks/useI18n';
import { Select } from '../Select';
import { FriendMultiSelect } from '../FriendMultiSelect';
import toast from 'react-hot-toast';

interface FolderShareDialogProps {
  isOpen: boolean;
  onClose: () => void;
  folderId: string;
  folderName: string;
  isOwner: boolean;
}

export const FolderShareDialog: React.FC<FolderShareDialogProps> = ({
  isOpen,
  onClose,
  folderId,
  folderName,
  isOwner,
}) => {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [selectedFriendIds, setSelectedFriendIds] = useState<string[]>([]);
  const [permission, setPermission] = useState<'Viewer' | 'Editor'>('Viewer');

  // Fetch current shares of the folder (only if owner)
  const { data: shares = [], isLoading: isLoadingShares } = useQuery({
    queryKey: ['folderShares', folderId],
    queryFn: () => foldersApi.getShares(folderId),
    enabled: isOpen && isOwner,
  });

  // Fetch friends overview to pick friends to invite
  const { data: friendsOverview, isLoading: isLoadingFriends } = useQuery({
    queryKey: ['friendsOverview'],
    queryFn: () => friendsApi.getOverview(),
    enabled: isOpen && isOwner,
  });

  // Filter friends that are already shared with
  const sharedUserIds = new Set(shares.map((s) => s.sharedWithUserId));
  const availableFriends = friendsOverview?.friends.filter((f) => !sharedUserIds.has(f.userId)) || [];

  // Bỏ chọn những người vừa được share xong (không còn trong danh sách khả dụng).
  React.useEffect(() => {
    setSelectedFriendIds((prev) => prev.filter((id) => availableFriends.some((f) => f.userId === id)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [shares]);

  // Invite Mutation — mời NHIỀU người cùng lúc (gọi song song, tổng hợp kết quả).
  const inviteMutation = useMutation({
    mutationFn: async (payload: { friendUserIds: string[]; permission: 'Viewer' | 'Editor' }) => {
      const results = await Promise.allSettled(
        payload.friendUserIds.map((friendUserId) =>
          foldersApi.inviteShare(folderId, { friendUserId, permission: payload.permission }),
        ),
      );
      const failed = results.filter((r) => r.status === 'rejected');
      return { total: payload.friendUserIds.length, failed };
    },
    onSuccess: ({ total, failed }) => {
      const ok = total - failed.length;
      if (ok > 0) toast.success(t('share.inviteSent').replace('{count}', String(ok)));
      if (failed.length > 0) {
        handleApiError(
          (failed[0] as PromiseRejectedResult).reason,
          t('share.inviteFailCount').replace('{count}', String(failed.length)),
        );
      }
      setSelectedFriendIds([]);
      queryClient.invalidateQueries({ queryKey: ['folderShares', folderId] });
      queryClient.invalidateQueries({ queryKey: ['folders'] });
    },
    onError: (err) => {
      handleApiError(err, t('share.inviteFail'));
    },
  });

  // Update Permission Mutation
  const updatePermissionMutation = useMutation({
    mutationFn: ({ shareId, permission }: { shareId: string; permission: 'Viewer' | 'Editor' }) =>
      foldersApi.updateShareRole(folderId, shareId, { permission }),
    onSuccess: () => {
      toast.success(t('share.permissionUpdated'));
      queryClient.invalidateQueries({ queryKey: ['folderShares', folderId] });
    },
    onError: (err) => {
      handleApiError(err, t('share.permissionUpdateFail'));
    },
  });

  // Revoke Share Mutation (Delete share)
  const revokeShareMutation = useMutation({
    mutationFn: (shareId: string) => foldersApi.revokeShare(folderId, shareId),
    onSuccess: () => {
      toast.success(t('share.revoked'));
      queryClient.invalidateQueries({ queryKey: ['folderShares', folderId] });
      queryClient.invalidateQueries({ queryKey: ['folders'] });
    },
    onError: (err) => {
      handleApiError(err, t('share.revokeFail'));
    },
  });

  const handleInvite = (e: React.FormEvent) => {
    e.preventDefault();
    if (selectedFriendIds.length === 0) {
      toast.error(t('share.needFriend'));
      return;
    }
    inviteMutation.mutate({ friendUserIds: selectedFriendIds, permission });
  };

  const permissionOptions = [
    { value: 'Viewer', label: t('share.roleViewer') },
    { value: 'Editor', label: t('share.roleEditor') },
  ];

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      {/* Backdrop */}
      <div className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm" onClick={onClose} />

      {/* Modal Box */}
      <div className="relative w-full max-w-[520px] bg-white dark:bg-slate-900 rounded-xl shadow-2xl border border-slate-200 dark:border-slate-800 flex flex-col overflow-hidden max-h-[90vh]">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-slate-100 dark:border-slate-800 shrink-0">
          <div className="flex items-center gap-2">
            <Users className="w-5 h-5 text-brand-600 dark:text-brand-400" />
            <h2 className="text-[17px] font-semibold text-slate-900 dark:text-slate-100 truncate max-w-[340px]">
              {t('share.title').replace('{name}', folderName)}
            </h2>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 text-slate-400 dark:text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800 hover:text-slate-900 dark:hover:text-slate-100 rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-5 space-y-6">
          {isOwner ? (
            <>
              {/* Form Invite */}
              <form
                onSubmit={handleInvite}
                className="bg-slate-50 dark:bg-slate-800/40 p-4 rounded-xl border border-slate-100 dark:border-slate-800 space-y-4"
              >
                <h3 className="text-sm font-semibold text-slate-800 dark:text-slate-200 flex items-center gap-1.5">
                  <UserPlus className="w-4 h-4 text-brand-500" />
                  {t('share.inviteHeading')}
                </h3>
                <div className="flex flex-col sm:flex-row gap-3 sm:items-start">
                  <div className="flex-1 min-w-0">
                    <FriendMultiSelect
                      friends={availableFriends}
                      value={selectedFriendIds}
                      onChange={setSelectedFriendIds}
                      disabled={inviteMutation.isPending || isLoadingFriends}
                      className="h-[38px]"
                    />
                  </div>
                  <div className="w-full sm:w-[140px] shrink-0">
                    <Select
                      value={permission}
                      onChange={(v) => setPermission(v as 'Viewer' | 'Editor')}
                      options={permissionOptions}
                      disabled={inviteMutation.isPending}
                      className="h-[38px]"
                    />
                  </div>
                  <button
                    type="submit"
                    disabled={inviteMutation.isPending || selectedFriendIds.length === 0}
                    className="h-[38px] px-4 text-sm font-medium text-white bg-brand-600 rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-1.5 shrink-0"
                  >
                    {inviteMutation.isPending ? <Loader2 className="w-4 h-4 animate-spin" /> : t('share.shareAction')}
                  </button>
                </div>
                {availableFriends.length === 0 && !isLoadingFriends && (
                  <p className="text-xs text-slate-400 dark:text-slate-500">{t('share.noFriendsAvailable')}</p>
                )}
              </form>

              {/* Shares List */}
              <div className="space-y-3">
                <h3 className="text-sm font-semibold text-slate-800 dark:text-slate-200">{t('share.peopleWithAccess')}</h3>
                {isLoadingShares ? (
                  <div className="flex items-center justify-center py-6">
                    <Loader2 className="w-6 h-6 animate-spin text-slate-400" />
                  </div>
                ) : shares.length === 0 ? (
                  <div className="text-center py-8 border border-dashed border-slate-200 dark:border-slate-800 rounded-xl text-slate-400 dark:text-slate-500 text-sm">
                    {t('share.notSharedYet')}
                  </div>
                ) : (
                  <div className="border border-slate-100 dark:border-slate-800 rounded-xl overflow-hidden divide-y divide-slate-100 dark:divide-slate-800 bg-white dark:bg-slate-900">
                    {shares.map((share) => (
                      <div
                        key={share.shareId}
                        className="flex items-center justify-between p-3.5 hover:bg-slate-50 dark:hover:bg-slate-800/20 transition-colors"
                      >
                        <div className="flex items-center gap-3 min-w-0">
                          {share.sharedWithUserAvatar ? (
                            <img
                              src={share.sharedWithUserAvatar}
                              alt={share.sharedWithUserName}
                              referrerPolicy="no-referrer"
                              className="w-9 h-9 rounded-full object-cover shrink-0"
                            />
                          ) : (
                            <div className="w-9 h-9 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-600 dark:text-slate-400 flex items-center justify-center font-medium text-sm shrink-0">
                              <User className="w-4 h-4" />
                            </div>
                          )}
                          <div className="min-w-0">
                            <div className="text-sm font-medium text-slate-900 dark:text-slate-100 flex items-center gap-1.5">
                              <span className="truncate">{share.sharedWithUserName}</span>
                              {share.status === 'Pending' && (
                                <span className="px-1.5 py-0.5 rounded text-[10px] font-semibold bg-amber-50 text-amber-700 border border-amber-200 dark:bg-amber-500/10 dark:text-amber-400 dark:border-amber-500/20 shrink-0">
                                  {t('share.pending')}
                                </span>
                              )}
                            </div>
                            <div className="text-xs text-slate-400 dark:text-slate-500 truncate">
                              {t('share.sharedAt').replace('{date}', new Date(share.sharedAt).toLocaleDateString())}
                            </div>
                          </div>
                        </div>

                        {/* Actions (Only owner can update/revoke) */}
                        <div className="flex items-center gap-2 shrink-0">
                          <div className="w-[130px]">
                            <Select
                              value={share.permission}
                              onChange={(v) =>
                                updatePermissionMutation.mutate({
                                  shareId: share.shareId,
                                  permission: v as 'Viewer' | 'Editor',
                                })
                              }
                              options={permissionOptions}
                              disabled={updatePermissionMutation.isPending}
                              className="h-[30px]"
                            />
                          </div>
                          <button
                            onClick={() => {
                              if (confirm(t('share.confirmRevoke').replace('{name}', share.sharedWithUserName))) {
                                revokeShareMutation.mutate(share.shareId);
                              }
                            }}
                            disabled={revokeShareMutation.isPending}
                            className="p-1.5 text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-500/10 rounded-lg transition-colors"
                            title={t('share.revokeAction')}
                          >
                            {revokeShareMutation.isPending ? (
                              <Loader2 className="w-4 h-4 animate-spin" />
                            ) : (
                              <Trash2 className="w-4 h-4" />
                            )}
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </>
          ) : (
            <div className="flex flex-col items-center justify-center py-10 text-center space-y-3">
              <div className="w-12 h-12 rounded-full bg-indigo-50 dark:bg-brand-500/10 text-brand-600 dark:text-brand-400 flex items-center justify-center">
                <Shield className="w-6 h-6" />
              </div>
              <div>
                <h4 className="text-sm font-semibold text-slate-950 dark:text-slate-50">{t('share.sharedFolder')}</h4>
                <p className="text-xs text-slate-400 dark:text-slate-500 mt-1 max-w-[280px]">
                  {t('share.viewerNotice')}
                </p>
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="px-5 py-4 border-t border-slate-100 dark:border-slate-800 flex justify-end shrink-0">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-200 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
          >
            {t('common.close')}
          </button>
        </div>
      </div>
    </div>
  );
};
