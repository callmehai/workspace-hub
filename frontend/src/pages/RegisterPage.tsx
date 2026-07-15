import { useEffect, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import toast from 'react-hot-toast';
import { AlertCircle, UserPlus } from 'lucide-react';
import { authApi } from '../lib/authApi';
import { friendsApi } from '../lib/friendsApi';
import { EMAIL_RE } from '../lib/validation';
import { GoogleSignInButton } from '../components/auth/GoogleSignInButton';
import { ThemeLangControls } from '../components/ThemeLangControls';
import { useI18n } from '../hooks/useI18n';
import type { ApiError } from '../types/auth';

export const RegisterPage = () => {
  const navigate = useNavigate();
  const { t } = useI18n();
  const [searchParams] = useSearchParams();
  // Link mời kết bạn: /register?inviteToken= → đăng ký xong tự thành bạn với người mời.
  const inviteToken = searchParams.get('inviteToken');

  const { data: invite } = useQuery({
    queryKey: ['friend-invite', inviteToken],
    queryFn: () => friendsApi.getInviteByToken(inviteToken!),
    enabled: !!inviteToken,
    retry: false,
  });

  const [fullName, setFullName] = useState('');
  // Prefill email được mời — BE consume invite theo email đăng ký, đổi email khác sẽ không tự kết bạn.
  const [email, setEmail] = useState('');
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- prefill 1 lần khi invite load xong
    if (invite?.email) setEmail((cur) => cur || invite.email);
  }, [invite]);
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [errFullName, setErrFullName] = useState('');
  const [errEmail, setErrEmail] = useState('');
  const [errPwd, setErrPwd] = useState('');
  const [errConfirm, setErrConfirm] = useState('');
  const [banner, setBanner] = useState('');

  const register = useMutation({
    mutationFn: () => authApi.register({ fullName, email, password, inviteToken: inviteToken ?? undefined }),
    onSuccess: (result) => {
      toast.success(t('register.otpSent'));
      // SCRUM-64: chưa đăng nhập — sang màn nhập OTP, mang email + cooldown gửi lại.
      navigate('/verify-otp', {
        replace: true,
        state: { email: result.email, cooldown: result.resendCooldownSeconds },
      });
    },
    onError: (error) => {
      const data = isAxiosError<ApiError>(error) ? error.response?.data : undefined;
      // 409 email trùng hoặc lỗi khác → banner.
      setBanner(data?.message ?? t('register.failed'));
    },
  });

  const googleMutation = useMutation({
    mutationFn: authApi.googleStart,
    onSuccess: (data) => {
      window.location.assign(data.authorizationUrl);
    },
    onError: () => {
      setBanner('Không khởi tạo được đăng nhập Google. Vui lòng thử lại.');
    },
  });

  const handleRegister = (e: React.FormEvent) => {
    e.preventDefault();
    setBanner('');

    const eFullName = fullName.trim().length === 0 ? t('valid.nameRequired') : '';
    const eEmail = !EMAIL_RE.test(email) ? t('valid.emailInvalid') : '';
    const ePwd = password.length < 8 ? t('valid.passwordMin') : '';
    const eConfirm = confirm !== password ? t('valid.confirmMismatch') : '';
    setErrFullName(eFullName);
    setErrEmail(eEmail);
    setErrPwd(ePwd);
    setErrConfirm(eConfirm);
    if (eFullName || eEmail || ePwd || eConfirm) return;

    register.mutate();
  };

  return (
    <div className="relative flex min-h-screen items-center justify-center bg-slate-50 dark:bg-slate-950 p-6 font-sans text-slate-900 dark:text-slate-100">
      <div className="absolute right-4 top-4">
        <ThemeLangControls />
      </div>

      <div className="w-full max-w-[404px]">
        {/* Logo */}
        <div className="mb-[22px] flex items-center justify-center gap-2.5">
          <div className="flex h-9 w-9 items-center justify-center rounded-[9px] bg-brand-600 text-[17px] font-bold text-white">
            W
          </div>
          <span className="text-lg font-semibold text-slate-900 dark:text-slate-100">{t('common.appName')}</span>
        </div>

        {/* Card */}
        <div className="rounded-[14px] border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900 p-7 shadow-sm">
          <h1 className="mb-1 text-[22px] font-semibold text-slate-900 dark:text-slate-100">{t('register.title')}</h1>
          <p className="mb-5 text-sm text-slate-500 dark:text-slate-400">{t('register.subtitle')}</p>

          {invite && (
            <div className="mb-4 flex items-center gap-2 rounded-lg border border-brand-200 bg-brand-50 dark:border-brand-500/30 dark:bg-brand-500/10 px-3 py-2.5 text-[13px] text-brand-700 dark:text-brand-300">
              <UserPlus className="h-4 w-4 flex-none" />
              <span>{t('register.friendInviteBanner', { name: invite.inviterName })}</span>
            </div>
          )}

          {banner && (
            <div className="mb-4 flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/40 px-3 py-2.5 text-[13px] text-red-700 dark:text-red-300">
              <AlertCircle className="h-4 w-4 flex-none" />
              <span>{banner}</span>
            </div>
          )}

          <form onSubmit={handleRegister} noValidate>
            <label htmlFor="fullName" className="mb-1.5 block text-[13px] font-medium text-slate-900 dark:text-slate-200">{t('register.fullName')}</label>
            <input
              id="fullName"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              placeholder={t('register.fullNamePlaceholder')}
              className={`h-[38px] w-full rounded-lg border bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errFullName ? 'border-red-400' : 'border-slate-300 dark:border-slate-700'
              }`}
            />
            {errFullName && <div className="mt-1 text-xs text-red-600 dark:text-red-400">{errFullName}</div>}

            <label htmlFor="email" className="mb-1.5 mt-3.5 block text-[13px] font-medium text-slate-900 dark:text-slate-200">{t('login.email')}</label>
            <input
              id="email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="ban@congty.vn"
              className={`h-[38px] w-full rounded-lg border bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errEmail ? 'border-red-400' : 'border-slate-300 dark:border-slate-700'
              }`}
            />
            {errEmail && <div className="mt-1 text-xs text-red-600 dark:text-red-400">{errEmail}</div>}

            <label htmlFor="password" className="mb-1.5 mt-3.5 block text-[13px] font-medium text-slate-900 dark:text-slate-200">{t('register.password')}</label>
            <input
              id="password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder={t('login.passwordPlaceholder')}
              className={`h-[38px] w-full rounded-lg border bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errPwd ? 'border-red-400' : 'border-slate-300 dark:border-slate-700'
              }`}
            />
            {errPwd && <div className="mt-1 text-xs text-red-600 dark:text-red-400">{errPwd}</div>}

            <label htmlFor="confirm" className="mb-1.5 mt-3.5 block text-[13px] font-medium text-slate-900 dark:text-slate-200">{t('register.confirm')}</label>
            <input
              id="confirm"
              type="password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              placeholder={t('register.confirmPlaceholder')}
              className={`h-[38px] w-full rounded-lg border bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errConfirm ? 'border-red-400' : 'border-slate-300 dark:border-slate-700'
              }`}
            />
            {errConfirm && <div className="mt-1 text-xs text-red-600 dark:text-red-400">{errConfirm}</div>}

            <button
              type="submit"
              disabled={register.isPending}
              className="mt-4 h-10 w-full rounded-lg bg-brand-600 text-sm font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
            >
              {register.isPending ? t('register.submitting') : t('register.submit')}
            </button>
          </form>

          {/* Divider */}
          <div className="my-[18px] flex items-center gap-3">
            <div className="h-px flex-1 bg-slate-200 dark:bg-slate-800" />
            <span className="text-xs text-slate-400 dark:text-slate-500">{t('common.or')}</span>
            <div className="h-px flex-1 bg-slate-200 dark:bg-slate-800" />
          </div>

          <GoogleSignInButton
            isPending={googleMutation.isPending}
            onClick={() => {
              setBanner('');
              googleMutation.mutate();
            }}
            label={t('register.submit')}
          />
        </div>

        <p className="mt-4 text-center text-sm text-slate-500 dark:text-slate-400">
          {t('register.haveAccount')}{' '}
          <Link to="/login" className="font-semibold text-brand-600 hover:text-brand-700 dark:text-brand-400 dark:hover:text-brand-300">
            {t('register.loginLink')}
          </Link>
        </p>
      </div>
    </div>
  );
};
