import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import {
  UserPlus, Users, Star, Mail, Clock, Check, X, Link as LinkIcon, Loader2, Trash2, Send,
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { friendsApi, type FriendDto, type FriendInviteDto } from '../lib/friendsApi';
import { handleApiError } from '../lib/errorUtils';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { useI18n } from '../hooks/useI18n';
import { EMAIL_RE } from '../lib/validation';

/** Avatar chữ cái đầu (fallback khi chưa có avatarUrl). */
function FriendAvatar({ friend }: { friend: Pick<FriendDto, 'fullName' | 'avatarUrl'> }) {
  if (friend.avatarUrl) {
    return <img src={friend.avatarUrl} alt="" className="h-9 w-9 rounded-full object-cover" />;
  }
  return (
    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-brand-50 text-sm font-semibold text-brand-600 dark:bg-brand-500/15 dark:text-brand-300">
      {friend.fullName.charAt(0).toUpperCase()}
    </div>
  );
}

function SectionTitle({ icon: Icon, label, count }: { icon: typeof Users; label: string; count: number }) {
  return (
    <h2 className="mb-2 flex items-center gap-2 text-[13px] font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
      <Icon className="h-4 w-4" />
      {label}
      <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-semibold text-slate-500 dark:bg-slate-800 dark:text-slate-400">{count}</span>
    </h2>
  );
}

export const Friends = () => {
  const { t } = useI18n();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [email, setEmail] = useState('');
  const [unfriendTarget, setUnfriendTarget] = useState<FriendDto | null>(null);
  const [cancelInviteTarget, setCancelInviteTarget] = useState<FriendInviteDto | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ['friends'],
    queryFn: friendsApi.getOverview,
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['friends'] });

  const sendRequest = useMutation({
    mutationFn: (target: string) => friendsApi.sendRequest(target),
    onSuccess: (result) => {
      setEmail('');
      invalidate();
      if (result.outcome === 'AutoAccepted') toast.success(t('friends.autoAccepted'));
      else if (result.outcome === 'RequestSent') toast.success(t('friends.requestSent'));
      else if (result.emailSent) toast.success(t('friends.inviteMailSent', { email: result.invite?.email ?? '' }));
      else toast.success(t('friends.inviteCreatedNoMail'));
    },
    onError: (e) => handleApiError(e, t('friends.requestFail')),
  });

  const accept = useMutation({
    mutationFn: friendsApi.accept,
    onSuccess: () => { invalidate(); toast.success(t('friends.accepted')); },
    onError: (e) => handleApiError(e, t('errors.generic')),
  });

  const remove = useMutation({
    mutationFn: friendsApi.remove,
    onSuccess: () => { setUnfriendTarget(null); invalidate(); },
    onError: (e) => { setUnfriendTarget(null); handleApiError(e, t('errors.generic')); },
  });

  const setTier = useMutation({
    mutationFn: ({ id, tier }: { id: string; tier: 'Friend' | 'CloseFriend' }) => friendsApi.setTier(id, tier),
    onSuccess: invalidate,
    onError: (e) => handleApiError(e, t('errors.generic')),
  });

  const cancelInvite = useMutation({
    mutationFn: friendsApi.cancelInvite,
    onSuccess: () => { setCancelInviteTarget(null); invalidate(); },
    onError: (e) => { setCancelInviteTarget(null); handleApiError(e, t('errors.generic')); },
  });

  const handleSend = (e: React.FormEvent) => {
    e.preventDefault();
    const target = email.trim();
    if (!EMAIL_RE.test(target)) { toast.error(t('valid.emailInvalid')); return; }
    sendRequest.mutate(target);
  };

  const copyLink = async (link: string) => {
    await navigator.clipboard.writeText(link);
    toast.success(t('friends.linkCopied'));
  };

  const friends = data?.friends ?? [];
  const incoming = data?.incomingRequests ?? [];
  const outgoing = data?.outgoingRequests ?? [];
  const invites = data?.emailInvites ?? [];

  return (
    <div className="mx-auto max-w-3xl px-4 py-6">
      <div className="mb-1 flex items-center gap-2">
        <Users className="h-6 w-6 text-brand-600 dark:text-brand-400" />
        <h1 className="text-xl font-semibold text-slate-900 dark:text-slate-100">{t('friends.title')}</h1>
      </div>
      <p className="mb-5 text-sm text-slate-500 dark:text-slate-400">{t('friends.subtitle')}</p>

      {/* ── Form kết bạn theo email ── */}
      <form onSubmit={handleSend} className="mb-6 flex gap-2">
        <div className="relative flex-1">
          <Mail className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder={t('friends.emailPlaceholder')}
            className="h-10 w-full rounded-lg border border-slate-300 bg-white pl-9 pr-3 text-sm text-slate-900 outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
          />
        </div>
        <button
          type="submit"
          disabled={sendRequest.isPending}
          className="flex h-10 items-center gap-1.5 rounded-lg bg-brand-600 px-4 text-sm font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
        >
          {sendRequest.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <UserPlus className="h-4 w-4" />}
          {t('friends.sendRequest')}
        </button>
      </form>

      {isLoading ? (
        <div className="flex justify-center py-16 text-slate-400">
          <Loader2 className="h-6 w-6 animate-spin" />
        </div>
      ) : (
        <div className="space-y-7">
          {/* ── Lời mời đến ── */}
          {incoming.length > 0 && (
            <section>
              <SectionTitle icon={UserPlus} label={t('friends.incoming')} count={incoming.length} />
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white dark:divide-slate-800 dark:border-slate-800 dark:bg-slate-900">
                {incoming.map((f) => (
                  <li key={f.friendshipId} className="flex items-center gap-3 px-4 py-3">
                    <FriendAvatar friend={f} />
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-sm font-medium text-slate-900 dark:text-slate-100">{f.fullName}</div>
                      <div className="truncate text-xs text-slate-500 dark:text-slate-400">{f.email}</div>
                    </div>
                    <button
                      onClick={() => accept.mutate(f.friendshipId)}
                      disabled={accept.isPending}
                      className="flex h-8 items-center gap-1 rounded-lg bg-brand-600 px-3 text-xs font-semibold text-white hover:bg-brand-700 disabled:opacity-50"
                    >
                      <Check className="h-3.5 w-3.5" /> {t('friends.accept')}
                    </button>
                    <button
                      onClick={() => remove.mutate(f.friendshipId)}
                      disabled={remove.isPending}
                      className="flex h-8 items-center gap-1 rounded-lg border border-slate-200 px-3 text-xs font-medium text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800"
                    >
                      <X className="h-3.5 w-3.5" /> {t('friends.decline')}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {/* ── Bạn bè ── */}
          <section>
            <SectionTitle icon={Users} label={t('friends.list')} count={friends.length} />
            {friends.length === 0 ? (
              <div className="rounded-xl border border-dashed border-slate-200 px-4 py-10 text-center text-sm text-slate-400 dark:border-slate-800 dark:text-slate-500">
                {t('friends.empty')}
              </div>
            ) : (
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white dark:divide-slate-800 dark:border-slate-800 dark:bg-slate-900">
                {friends.map((f) => {
                  const close = f.myTier === 'CloseFriend';
                  return (
                    <li key={f.friendshipId} className="group flex items-center gap-3 px-4 py-3">
                      <FriendAvatar friend={f} />
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-1.5">
                          <span className="truncate text-sm font-medium text-slate-900 dark:text-slate-100">{f.fullName}</span>
                          {close && (
                            <span className="rounded-full bg-amber-50 px-2 py-0.5 text-[11px] font-semibold text-amber-700 dark:bg-amber-500/15 dark:text-amber-300">
                              {t('friends.closeFriend')}
                            </span>
                          )}
                        </div>
                        <div className="truncate text-xs text-slate-500 dark:text-slate-400">{f.email}</div>
                      </div>

                      {/* Bạn thân toggle */}
                      <button
                        title={close ? t('friends.unsetClose') : t('friends.setClose')}
                        onClick={() => setTier.mutate({ id: f.friendshipId, tier: close ? 'Friend' : 'CloseFriend' })}
                        className={`rounded-lg p-2 transition-colors ${
                          close
                            ? 'text-amber-500 hover:bg-amber-50 dark:hover:bg-amber-500/10'
                            : 'text-slate-300 hover:bg-slate-50 hover:text-amber-500 dark:text-slate-600 dark:hover:bg-slate-800'
                        }`}
                      >
                        <Star className="h-4 w-4" fill={close ? 'currentColor' : 'none'} />
                      </button>

                      {/* Gửi mail nhanh cho bạn */}
                      <button
                        title={t('friends.sendMail')}
                        onClick={() => navigate(`/send-email?to=${encodeURIComponent(f.email)}`)}
                        className="rounded-lg p-2 text-slate-400 hover:bg-slate-50 hover:text-brand-600 dark:text-slate-500 dark:hover:bg-slate-800 dark:hover:text-brand-400"
                      >
                        <Send className="h-4 w-4" />
                      </button>

                      <button
                        title={t('friends.unfriend')}
                        onClick={() => setUnfriendTarget(f)}
                        className="rounded-lg p-2 text-slate-400 hover:bg-red-50 hover:text-red-600 dark:text-slate-500 dark:hover:bg-red-500/10 dark:hover:text-red-400"
                      >
                        <Trash2 className="h-4 w-4" />
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </section>

          {/* ── Lời mời đã gửi (user trong app) ── */}
          {outgoing.length > 0 && (
            <section>
              <SectionTitle icon={Clock} label={t('friends.outgoing')} count={outgoing.length} />
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white dark:divide-slate-800 dark:border-slate-800 dark:bg-slate-900">
                {outgoing.map((f) => (
                  <li key={f.friendshipId} className="flex items-center gap-3 px-4 py-3">
                    <FriendAvatar friend={f} />
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-sm font-medium text-slate-900 dark:text-slate-100">{f.fullName}</div>
                      <div className="truncate text-xs text-slate-500 dark:text-slate-400">{f.email}</div>
                    </div>
                    <span className="text-xs text-slate-400 dark:text-slate-500">{t('friends.waiting')}</span>
                    <button
                      onClick={() => remove.mutate(f.friendshipId)}
                      className="flex h-8 items-center gap-1 rounded-lg border border-slate-200 px-3 text-xs font-medium text-slate-600 hover:bg-slate-50 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800"
                    >
                      {t('friends.cancelRequest')}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {/* ── Lời mời qua email (chưa có tài khoản) ── */}
          {invites.length > 0 && (
            <section>
              <SectionTitle icon={Mail} label={t('friends.emailInvites')} count={invites.length} />
              <ul className="divide-y divide-slate-100 rounded-xl border border-slate-200 bg-white dark:divide-slate-800 dark:border-slate-800 dark:bg-slate-900">
                {invites.map((inv) => (
                  <li key={inv.id} className="flex items-center gap-3 px-4 py-3">
                    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-slate-100 text-slate-400 dark:bg-slate-800 dark:text-slate-500">
                      <Mail className="h-4 w-4" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-sm font-medium text-slate-900 dark:text-slate-100">{inv.email}</div>
                      <div className="text-xs text-slate-400 dark:text-slate-500">{t('friends.inviteWaitingSignup')}</div>
                    </div>
                    <button
                      title={t('friends.copyLink')}
                      onClick={() => copyLink(inv.inviteLink)}
                      className="rounded-lg p-2 text-slate-400 hover:bg-slate-50 hover:text-brand-600 dark:text-slate-500 dark:hover:bg-slate-800 dark:hover:text-brand-400"
                    >
                      <LinkIcon className="h-4 w-4" />
                    </button>
                    <button
                      title={t('friends.cancelInvite')}
                      onClick={() => setCancelInviteTarget(inv)}
                      className="rounded-lg p-2 text-slate-400 hover:bg-red-50 hover:text-red-600 dark:text-slate-500 dark:hover:bg-red-500/10 dark:hover:text-red-400"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </div>
      )}

      <ConfirmDialog
        open={!!unfriendTarget}
        title={t('friends.unfriend')}
        message={t('friends.confirmUnfriend', { name: unfriendTarget?.fullName ?? '' })}
        confirmLabel={t('friends.unfriend')}
        tone="danger"
        loading={remove.isPending}
        onConfirm={() => unfriendTarget && remove.mutate(unfriendTarget.friendshipId)}
        onCancel={() => setUnfriendTarget(null)}
      />

      <ConfirmDialog
        open={!!cancelInviteTarget}
        title={t('friends.cancelInvite')}
        message={t('friends.confirmCancelInvite', { email: cancelInviteTarget?.email ?? '' })}
        confirmLabel={t('friends.cancelInvite')}
        tone="danger"
        loading={cancelInvite.isPending}
        onConfirm={() => cancelInviteTarget && cancelInvite.mutate(cancelInviteTarget.id)}
        onCancel={() => setCancelInviteTarget(null)}
      />
    </div>
  );
};
