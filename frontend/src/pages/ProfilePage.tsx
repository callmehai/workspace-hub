import { Camera, Mail, Shield, User as UserIcon } from 'lucide-react';
import { useAuth } from '../hooks/useAuth';
import { useI18n } from '../hooks/useI18n';

/**
 * Trang Hồ sơ người dùng (SCRUM-74).
 * Hiển thị thông tin tài khoản. Đổi theme/ngôn ngữ dùng nút ở Header (ThemeLangControls).
 * Vùng Ảnh đại diện đã bố trí sẵn nút "Đổi ảnh đại diện" (disabled) — nối R2 upload ở task sau.
 */
export const ProfilePage = () => {
  const { user } = useAuth();
  const { t } = useI18n();

  const initial = user?.fullName ? user.fullName.charAt(0).toUpperCase() : 'U';
  const isAdmin = user?.role === 'Admin';

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

        {/* ── Avatar card (prep R2 upload) ── */}
        <section className="mb-5 rounded-xl border border-slate-200 bg-white p-6 dark:border-slate-800 dark:bg-slate-900">
          <div className="flex items-center gap-5">
            <div className="relative shrink-0">
              <div className="flex h-20 w-20 items-center justify-center rounded-full bg-brand-50 text-2xl font-semibold text-brand-600 dark:bg-slate-800 dark:text-brand-300">
                {initial}
              </div>
              <span className="absolute -bottom-1 -right-1 flex h-7 w-7 items-center justify-center rounded-full border-2 border-white bg-slate-200 text-slate-400 dark:border-slate-900 dark:bg-slate-700 dark:text-slate-400">
                <Camera className="h-3.5 w-3.5" />
              </span>
            </div>
            <div className="min-w-0">
              <div className="truncate text-lg font-semibold text-slate-900 dark:text-slate-100">
                {user?.fullName ?? t('nav.user')}
              </div>
              <div className="truncate text-sm text-slate-500 dark:text-slate-400">{user?.email}</div>
              <div className="mt-3 flex items-center gap-2">
                <button
                  type="button"
                  disabled
                  title={t('profile.avatarHint')}
                  className="inline-flex cursor-not-allowed items-center gap-1.5 rounded-lg border border-slate-200 bg-slate-50 px-3 py-1.5 text-[13px] font-medium text-slate-400 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-500"
                >
                  <Camera className="h-3.5 w-3.5" />
                  {t('profile.changeAvatar')}
                </button>
                <span className="rounded-full bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-600 dark:bg-amber-500/10 dark:text-amber-400">
                  {t('common.comingSoon')}
                </span>
              </div>
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
            <InfoRow icon={<UserIcon className="h-4 w-4" />} label={t('profile.fullName')} value={user?.fullName ?? '—'} />
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
      </div>
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
      <dd className="min-w-0 flex-1 truncate text-sm font-medium text-slate-800 dark:text-slate-100">{value}</dd>
    </div>
  );
}
