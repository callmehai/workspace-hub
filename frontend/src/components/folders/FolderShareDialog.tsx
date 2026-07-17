import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { X, Loader2, UserPlus, Users, Trash2, Shield, User } from 'lucide-react';
import { foldersApi } from '../../lib/itemsApi';
import { friendsApi } from '../../lib/friendsApi';
import { handleApiError } from '../../lib/errorUtils';
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
  const queryClient = useQueryClient();
  const [selectedFriendId, setSelectedFriendId] = useState('');
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
  const sharedUserIds = new Set(shares.map(s => s.sharedWithUserId));
  const availableFriends = friendsOverview?.friends.filter(f => !sharedUserIds.has(f.userId)) || [];

  // Tự động chọn người bạn đầu tiên nếu có danh sách khả dụng
  React.useEffect(() => {
    if (availableFriends.length > 0) {
      if (!selectedFriendId || !availableFriends.some(f => f.userId === selectedFriendId)) {
        setSelectedFriendId(availableFriends[0].userId);
      }
    } else {
      setSelectedFriendId('');
    }
  }, [availableFriends, selectedFriendId]);

  // Invite Mutation
  const inviteMutation = useMutation({
    mutationFn: (payload: { friendUserId: string; permission: 'Viewer' | 'Editor' }) =>
      foldersApi.inviteShare(folderId, payload),
    onSuccess: () => {
      toast.success('Đã gửi lời mời chia sẻ thư mục!');
      setSelectedFriendId('');
      queryClient.invalidateQueries({ queryKey: ['folderShares', folderId] });
      queryClient.invalidateQueries({ queryKey: ['folders'] });
    },
    onError: (err) => {
      handleApiError(err, 'Không thể gửi lời mời chia sẻ');
    },
  });

  // Update Permission Mutation
  const updatePermissionMutation = useMutation({
    mutationFn: ({ shareId, permission }: { shareId: string; permission: 'Viewer' | 'Editor' }) =>
      foldersApi.updateShareRole(folderId, shareId, { permission }),
    onSuccess: () => {
      toast.success('Đã cập nhật quyền thành công!');
      queryClient.invalidateQueries({ queryKey: ['folderShares', folderId] });
    },
    onError: (err) => {
      handleApiError(err, 'Không thể cập nhật quyền');
    },
  });

  // Revoke Share Mutation (Delete share)
  const revokeShareMutation = useMutation({
    mutationFn: (shareId: string) => foldersApi.revokeShare(folderId, shareId),
    onSuccess: () => {
      toast.success('Đã thu hồi quyền truy cập!');
      queryClient.invalidateQueries({ queryKey: ['folderShares', folderId] });
      queryClient.invalidateQueries({ queryKey: ['folders'] });
    },
    onError: (err) => {
      handleApiError(err, 'Không thể thu hồi quyền truy cập');
    },
  });

  const handleInvite = (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedFriendId) {
      toast.error('Vui lòng chọn bạn bè để chia sẻ');
      return;
    }
    inviteMutation.mutate({ friendUserId: selectedFriendId, permission });
  };

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
              Chia sẻ thư mục &ldquo;{folderName}&rdquo;
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
              <form onSubmit={handleInvite} className="bg-slate-50 dark:bg-slate-800/40 p-4 rounded-xl border border-slate-100 dark:border-slate-800 space-y-4">
                <h3 className="text-sm font-semibold text-slate-800 dark:text-slate-200 flex items-center gap-1.5">
                  <UserPlus className="w-4 h-4 text-brand-500" />
                  Mời bạn bè truy cập
                </h3>
                <div className="flex flex-col sm:flex-row gap-3">
                  <div className="flex-1">
                    <select
                      value={selectedFriendId}
                      onChange={(e) => setSelectedFriendId(e.target.value)}
                      disabled={inviteMutation.isPending || isLoadingFriends}
                      className="w-full h-[38px] px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors"
                    >
                      {availableFriends.map((friend) => (
                        <option key={friend.userId} value={friend.userId}>
                          {friend.fullName} ({friend.email})
                        </option>
                      ))}
                    </select>
                  </div>
                  <div className="w-full sm:w-[120px]">
                    <select
                      value={permission}
                      onChange={(e) => setPermission(e.target.value as 'Viewer' | 'Editor')}
                      disabled={inviteMutation.isPending}
                      className="w-full h-[38px] px-3 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-500 transition-colors"
                    >
                      <option value="Viewer">Người xem</option>
                      <option value="Editor">Người sửa</option>
                    </select>
                  </div>
                  <button
                    type="submit"
                    disabled={inviteMutation.isPending || !selectedFriendId}
                    className="h-[38px] px-4 text-sm font-medium text-white bg-brand-600 rounded-lg hover:bg-brand-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-1.5"
                  >
                    {inviteMutation.isPending ? (
                      <Loader2 className="w-4 h-4 animate-spin" />
                    ) : (
                      'Chia sẻ'
                    )}
                  </button>
                </div>
                {availableFriends.length === 0 && !isLoadingFriends && (
                  <p className="text-xs text-slate-400 dark:text-slate-500">
                    Không có bạn bè mới nào khả dụng để chia sẻ.
                  </p>
                )}
              </form>

              {/* Shares List */}
              <div className="space-y-3">
                <h3 className="text-sm font-semibold text-slate-800 dark:text-slate-200">
                  Người có quyền truy cập
                </h3>
                {isLoadingShares ? (
                  <div className="flex items-center justify-center py-6">
                    <Loader2 className="w-6 h-6 animate-spin text-slate-400" />
                  </div>
                ) : shares.length === 0 ? (
                  <div className="text-center py-8 border border-dashed border-slate-200 dark:border-slate-800 rounded-xl text-slate-400 dark:text-slate-500 text-sm">
                    Thư mục này hiện chưa chia sẻ với ai.
                  </div>
                ) : (
                  <div className="border border-slate-100 dark:border-slate-800 rounded-xl overflow-hidden divide-y divide-slate-100 dark:divide-slate-800 bg-white dark:bg-slate-900">
                    {shares.map((share) => (
                      <div key={share.shareId} className="flex items-center justify-between p-3.5 hover:bg-slate-50 dark:hover:bg-slate-800/20 transition-colors">
                        <div className="flex items-center gap-3 min-w-0">
                          {share.sharedWithUserAvatar ? (
                            <img
                              src={share.sharedWithUserAvatar}
                              alt={share.sharedWithUserName}
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
                                  Chờ nhận
                                </span>
                              )}
                            </div>
                            <div className="text-xs text-slate-400 dark:text-slate-500 truncate">
                              Chia sẻ lúc {new Date(share.sharedAt).toLocaleDateString()}
                            </div>
                          </div>
                        </div>

                        {/* Actions (Only owner can update/revoke) */}
                        <div className="flex items-center gap-2">
                          <select
                            value={share.permission}
                            onChange={(e) =>
                              updatePermissionMutation.mutate({
                                shareId: share.shareId,
                                permission: e.target.value as 'Viewer' | 'Editor',
                              })
                            }
                            disabled={updatePermissionMutation.isPending}
                            className="h-[30px] px-2 rounded-lg border border-slate-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-1 focus:ring-brand-500 transition-colors"
                          >
                            <option value="Viewer">Người xem</option>
                            <option value="Editor">Người sửa</option>
                          </select>
                          <button
                            onClick={() => {
                              if (confirm(`Bạn có chắc muốn thu hồi quyền chia sẻ của ${share.sharedWithUserName}?`)) {
                                revokeShareMutation.mutate(share.shareId);
                              }
                            }}
                            disabled={revokeShareMutation.isPending}
                            className="p-1.5 text-rose-500 hover:bg-rose-50 dark:hover:bg-rose-500/10 rounded-lg transition-colors"
                            title="Thu hồi quyền"
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
                <h4 className="text-sm font-semibold text-slate-950 dark:text-slate-50">
                  Thư mục chia sẻ
                </h4>
                <p className="text-xs text-slate-400 dark:text-slate-500 mt-1 max-w-[280px]">
                  Bạn được mời vào thư mục này. Chỉ chủ sở hữu thư mục mới có quyền chỉnh sửa danh sách chia sẻ.
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
            Đóng
          </button>
        </div>
      </div>
    </div>
  );
};
