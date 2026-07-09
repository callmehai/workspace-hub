import { useEffect, useRef, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import toast from 'react-hot-toast';
import { Camera, Check, KeyRound, Mail, Pencil, Shield, User as UserIcon, X, ZoomIn } from 'lucide-react';
import { useAuth } from '../hooks/useAuth';
import { useI18n } from '../hooks/useI18n';
import { usersApi } from '../lib/usersApi';
import type { ApiError } from '../types/auth';

const MAX_AVATAR_BYTES = 5 * 1024 * 1024;
const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

/**
 * Trang Hồ sơ người dùng (SCRUM-74 + avatar upload SCRUM-75 + đổi tên/mật khẩu + xem ảnh cỡ lớn).
 * Đổi theme/ngôn ngữ dùng nút ở Header (ThemeLangControls).
 */
export const ProfilePage = () => {
  const { user, updateUser } = useAuth();
  const { t } = useI18n();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [isLightboxOpen, setLightboxOpen] = useState(false);
  const [isEditingName, setEditingName] = useState(false);
  const [nameDraft, setNameDraft] = useState(user?.fullName ?? '');
  const [isChangingPassword, setChangingPassword] = useState(false);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');

  const initial = user?.fullName ? user.fullName.charAt(0).toUpperCase() : 'U';
  const isAdmin = user?.role === 'Admin';
  const canChangePassword = user?.authProvider !== 'Google';

  // Đóng lightbox bằng Escape.
  useEffect(() => {
    if (!isLightboxOpen) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setLightboxOpen(false);
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isLightboxOpen]);

  const uploadMutation = useMutation({
    mutationFn: (file: File) => usersApi.uploadAvatar(file),
    onSuccess: (updated) => {
      updateUser(updated);
      toast.success(t('profile.avatarUploaded'));
    },
    onError: () => toast.error(t('errors.generic')),
  });

  const removeMutation = useMutation({
    mutationFn: () => usersApi.deleteAvatar(),
    onSuccess: (updated) => {
      updateUser(updated);
      toast.success(t('profile.avatarRemoved'));
    },
    onError: () => toast.error(t('errors.generic')),
  });

  const updateNameMutation = useMutation({
    mutationFn: (fullName: string) => usersApi.updateProfile(fullName),
    onSuccess: (updated) => {
      updateUser(updated);
      setEditingName(false);
      toast.success(t('profile.nameUpdated'));
    },
    onError: (err) => {
      const msg = isAxiosError<ApiError>(err) ? err.response?.data?.message : undefined;
      toast.error(msg ?? t('errors.generic'));
    },
  });

  const changePasswordMutation = useMutation({
    mutationFn: () => usersApi.changePassword(currentPassword, newPassword),
    onSuccess: () => {
      toast.success(t('profile.passwordUpdated'));
      setChangingPassword(false);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
    },
    onError: (err) => {
      const msg = isAxiosError<ApiError>(err) ? err.response?.data?.message : undefined;
      toast.error(msg ?? t('errors.generic'));
    },
  });

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;

    if (!ALLOWED_TYPES.includes(file.type)) {
      toast.error(t('profile.avatarHint'));
      return;
    }
    if (file.size > MAX_AVATAR_BYTES) {
      toast.error(t('profile.avatarHint'));
      return;
    }
    uploadMutation.mutate(file);
  };

  const startEditingName = () => {
    setNameDraft(user?.fullName ?? '');
    setEditingName(true);
  };

  const submitName = () => {
    const trimmed = nameDraft.trim();
    if (!trimmed || trimmed === user?.fullName) {
      setEditingName(false);
      return;
    }
    updateNameMutation.mutate(trimmed);
  };

  const submitPasswordChange = () => {
    if (newPassword !== confirmPassword) {
      toast.error(t('profile.passwordMismatch'));
      return;
    }
    changePasswordMutation.mutate();
  };

  const isAvatarBusy = uploadMutation.isPending || removeMutation.isPending;

  return (
    <div className="min-h-full bg-slate-50 dark:bg-slate-950">
      <div className="mx-auto max-w-[760px] px-6 py-8">
        {/* ── Header ── */}
        <div className="mb-6">
          <h1 className="text-[22px] font-semibold text-slate-900 dark:text-slate-100">
            {t('profile.title')}
          </h1>
          <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">{t('profile.subtitle')}</p>
        </div>

        {/* ── Avatar card (SCRUM-75 — R2 upload + xem cỡ lớn) ── */}
        <section className="mb-5 rounded-xl border border-slate-200 bg-white p-6 dark:border-slate-800 dark:bg-slate-900">
          <div className="flex items-center gap-5">
            <div className="relative shrink-0">
              {user?.avatarUrl ? (
                <button
                  type="button"
                  onClick={() => setLightboxOpen(true)}
                  title={t('profile.viewAvatar')}
                  className="group relative block h-20 w-20 overflow-hidden rounded-full"
                >
                  <img src={user.avatarUrl} alt={user.fullName} className="h-full w-full object-cover" />
                  <span className="absolute inset-0 flex items-center justify-center bg-black/0 text-white opacity-0 transition group-hover:bg-black/40 group-hover:opacity-100">
                    <ZoomIn className="h-5 w-5" />
                  </span>
                </button>
              ) : (
                <div className="flex h-20 w-20 items-center justify-center rounded-full bg-brand-50 text-2xl font-semibold text-brand-600 dark:bg-slate-800 dark:text-brand-300">
                  {initial}
                </div>
              )}
              <button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                disabled={isAvatarBusy}
                title={t('profile.changeAvatar')}
                className="absolute -bottom-1 -right-1 flex h-7 w-7 items-center justify-center rounded-full border-2 border-white bg-brand-600 text-white transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-900"
              >
                <Camera className="h-3.5 w-3.5" />
              </button>
              <input
                ref={fileInputRef}
                type="file"
                accept="image/jpeg,image/png,image/webp"
                className="hidden"
                onChange={handleFileChange}
              />
            </div>
            <div className="min-w-0">
              <div className="truncate text-lg font-semibold text-slate-900 dark:text-slate-100">
                {user?.fullName ?? t('nav.user')}
              </div>
              <div className="truncate text-sm text-slate-500 dark:text-slate-400">{user?.email}</div>
              <div className="mt-3 flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => fileInputRef.current?.click()}
                  disabled={isAvatarBusy}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-3 py-1.5 text-[13px] font-medium text-slate-600 transition hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700"
                >
                  <Camera className="h-3.5 w-3.5" />
                  {t('profile.changeAvatar')}
                </button>
                {user?.avatarUrl && (
                  <button
                    type="button"
                    onClick={() => removeMutation.mutate()}
                    disabled={isAvatarBusy}
                    className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-3 py-1.5 text-[13px] font-medium text-slate-500 transition hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-400 dark:hover:bg-red-500/10 dark:hover:text-red-400"
                  >
                    <X className="h-3.5 w-3.5" />
                    {t('profile.removeAvatar')}
                  </button>
                )}
              </div>
              <p className="mt-1.5 text-[12px] text-slate-400 dark:text-slate-500">{t('profile.avatarHint')}</p>
            </div>
          </div>
        </section>

        {/* ── Account info ── */}
        <section className="mb-5 rounded-xl border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
          <div className="border-b border-slate-100 px-6 py-3.5 dark:border-slate-800">
            <h2 className="text-[13px] font-semibold uppercase tracking-[0.04em] text-slate-400 dark:text-slate-500">
              {t('profile.account')}
            </h2>
          </div>
          <dl className="divide-y divide-slate-100 dark:divide-slate-800">
            <InfoRow
              icon={<UserIcon className="h-4 w-4" />}
              label={t('profile.fullName')}
              value={
                isEditingName ? (
                  <div className="flex items-center gap-1.5">
                    <input
                      autoFocus
                      value={nameDraft}
                      onChange={(e) => setNameDraft(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') submitName();
                        if (e.key === 'Escape') setEditingName(false);
                      }}
                      placeholder={t('profile.namePlaceholder')}
                      className="min-w-0 flex-1 rounded-md border border-slate-200 bg-white px-2 py-1 text-sm text-slate-800 focus:border-brand-400 focus:outline-none dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
                    />
                    <button
                      type="button"
                      onClick={submitName}
                      disabled={updateNameMutation.isPending}
                      title={t('common.save')}
                      className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-emerald-600 hover:bg-emerald-50 disabled:opacity-60 dark:hover:bg-emerald-500/10"
                    >
                      <Check className="h-4 w-4" />
                    </button>
                    <button
                      type="button"
                      onClick={() => setEditingName(false)}
                      title={t('common.cancel')}
                      className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800"
                    >
                      <X className="h-4 w-4" />
                    </button>
                  </div>
                ) : (
                  <div className="flex items-center gap-2">
                    <span className="truncate">{user?.fullName ?? '—'}</span>
                    <button
                      type="button"
                      onClick={startEditingName}
                      title={t('profile.editName')}
                      className="flex h-6 w-6 shrink-0 items-center justify-center rounded-md text-slate-400 transition hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-800 dark:hover:text-slate-300"
                    >
                      <Pencil className="h-3.5 w-3.5" />
                    </button>
                  </div>
                )
              }
            />
            <InfoRow icon={<Mail className="h-4 w-4" />} label={t('profile.email')} value={user?.email ?? '—'} />
            <InfoRow
              icon={<Shield className="h-4 w-4" />}
              label={t('profile.role')}
              value={
                <span
                  className={`inline-flex items-center rounded-full px-2 py-0.5 text-[12px] font-medium ${
                    isAdmin
                      ? 'bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-300'
                      : 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300'
                  }`}
                >
                  {isAdmin ? t('profile.roleAdmin') : t('profile.roleUser')}
                </span>
              }
            />
          </dl>
        </section>

        {/* ── Security — đổi mật khẩu ── */}
        <section className="mb-5 rounded-xl border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
          <div className="flex items-center justify-between border-b border-slate-100 px-6 py-3.5 dark:border-slate-800">
            <h2 className="text-[13px] font-semibold uppercase tracking-[0.04em] text-slate-400 dark:text-slate-500">
              {t('profile.security')}
            </h2>
          </div>
          <div className="px-6 py-4">
            {!canChangePassword ? (
              <p className="text-sm text-slate-400 dark:text-slate-500">{t('profile.googleAccountNoPassword')}</p>
            ) : !isChangingPassword ? (
              <button
                type="button"
                onClick={() => setChangingPassword(true)}
                className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-3 py-1.5 text-[13px] font-medium text-slate-600 transition hover:bg-slate-100 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700"
              >
                <KeyRound className="h-3.5 w-3.5" />
                {t('profile.changePassword')}
              </button>
            ) : (
              <div className="max-w-sm space-y-3">
                <PasswordField
                  label={t('profile.currentPassword')}
                  value={currentPassword}
                  onChange={setCurrentPassword}
                />
                <PasswordField label={t('profile.newPassword')} value={newPassword} onChange={setNewPassword} />
                <PasswordField
                  label={t('profile.confirmPassword')}
                  value={confirmPassword}
                  onChange={setConfirmPassword}
                />
                <div className="flex items-center gap-2 pt-1">
                  <button
                    type="button"
                    onClick={submitPasswordChange}
                    disabled={changePasswordMutation.isPending || !currentPassword || !newPassword}
                    className="rounded-lg bg-brand-600 px-3 py-1.5 text-[13px] font-medium text-white transition hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    {changePasswordMutation.isPending ? t('common.saving') : t('common.save')}
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setChangingPassword(false);
                      setCurrentPassword('');
                      setNewPassword('');
                      setConfirmPassword('');
                    }}
                    className="rounded-lg px-3 py-1.5 text-[13px] font-medium text-slate-500 transition hover:bg-slate-100 dark:hover:bg-slate-800"
                  >
                    {t('common.cancel')}
                  </button>
                </div>
              </div>
            )}
          </div>
        </section>
      </div>

      {/* ── Lightbox xem avatar cỡ lớn ── */}
      {isLightboxOpen && user?.avatarUrl && (
        <div
          role="dialog"
          aria-modal="true"
          onClick={() => setLightboxOpen(false)}
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-6 backdrop-blur-sm"
        >
          <button
            type="button"
            onClick={() => setLightboxOpen(false)}
            title={t('common.close')}
            className="absolute right-5 top-5 flex h-9 w-9 items-center justify-center rounded-full bg-white/10 text-white transition hover:bg-white/20"
          >
            <X className="h-5 w-5" />
          </button>
          <img
            src={user.avatarUrl}
            alt={user.fullName}
            onClick={(e) => e.stopPropagation()}
            className="max-h-[80vh] max-w-[90vw] rounded-2xl object-contain shadow-2xl"
          />
        </div>
      )}
    </div>
  );
};

function InfoRow({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
}) {
  return (
    <div className="flex items-center gap-4 px-6 py-4">
      <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400">
        {icon}
      </div>
      <dt className="w-32 shrink-0 text-[13px] text-slate-400 dark:text-slate-500">{label}</dt>
      <dd className="min-w-0 flex-1 text-sm font-medium text-slate-800 dark:text-slate-100">{value}</dd>
    </div>
  );
}

function PasswordField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-[13px] font-medium text-slate-500 dark:text-slate-400">{label}</span>
      <input
        type="password"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        autoComplete="new-password"
        className="w-full rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-800 focus:border-brand-400 focus:outline-none dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
      />
    </label>
  );
}
