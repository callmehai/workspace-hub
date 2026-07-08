import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import toast from 'react-hot-toast';
import { AlertCircle } from 'lucide-react';
import { useAuth } from '../hooks/useAuth';
import { useI18n } from '../hooks/useI18n';
import api from '../lib/api';
import { authApi } from '../lib/authApi';
import { EMAIL_RE } from '../lib/validation';
import { GoogleSignInButton } from '../components/auth/GoogleSignInButton';
import { ThemeLangControls } from '../components/ThemeLangControls';
import type { ApiError, AuthResultDto } from '../types/auth';

export const Login = () => {
  const navigate = useNavigate();
  const { login } = useAuth();
  const { t } = useI18n();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errEmail, setErrEmail] = useState('');
  const [errPwd, setErrPwd] = useState('');
  const [banner, setBanner] = useState('');

  const loginMutation = useMutation({
    mutationFn: async () =>
      (await api.post<AuthResultDto>('/auth/login', { email, password })).data,
    onSuccess: (data) => {
      login(data.user);
      toast.success(t('login.success'));
      navigate('/', { replace: true });
    },
    onError: async (error) => {
      const data = isAxiosError<ApiError>(error) ? error.response?.data : undefined;
      if (data?.message === 'PHONE_NOT_VERIFIED') {
        toast('Tài khoản chưa xác minh — vui lòng xác nhận số điện thoại.', { icon: '📱' });
        navigate('/verify-otp', { state: { email } });
        return;
      }
      // 401 (sai thông tin / account bị khoá) hiển thị ở banner; còn lại fallback chung.
      setBanner(data?.message ?? t('login.failed'));
    },
  });

  const googleMutation = useMutation({
    mutationFn: authApi.googleStart,
    onSuccess: (data) => {
      // Chuyển trình duyệt sang Google; state đã lưu server-side.
      window.location.assign(data.authorizationUrl);
    },
    onError: () => {
      setBanner('Không khởi tạo được đăng nhập Google. Vui lòng thử lại.');
    },
  });

  const handleLogin = (e: React.FormEvent) => {
    e.preventDefault();
    setBanner('');

    const eEmail = !EMAIL_RE.test(email) ? t('valid.emailInvalid') : '';
    const ePwd = password.length < 8 ? t('valid.passwordMin') : '';
    setErrEmail(eEmail);
    setErrPwd(ePwd);
    if (eEmail || ePwd) return;

    loginMutation.mutate();
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
          <h1 className="mb-1 text-[22px] font-semibold text-slate-900 dark:text-slate-100">{t('login.title')}</h1>
          <p className="mb-5 text-sm text-slate-500 dark:text-slate-400">{t('login.subtitle')}</p>

          {banner && (
            <div className="mb-4 flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 dark:border-red-900/50 dark:bg-red-950/40 px-3 py-2.5 text-[13px] text-red-700 dark:text-red-300">
              <AlertCircle className="h-4 w-4 flex-none" />
              <span>{banner}</span>
            </div>
          )}

          <form onSubmit={handleLogin} noValidate>
            <label htmlFor="email" className="mb-1.5 block text-[13px] font-medium text-slate-900 dark:text-slate-200">{t('login.email')}</label>
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

            <label htmlFor="password" className="mb-1.5 mt-3.5 block text-[13px] font-medium text-slate-900 dark:text-slate-200">{t('login.password')}</label>
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

            <button
              type="submit"
              disabled={loginMutation.isPending}
              className="mt-4 h-10 w-full rounded-lg bg-brand-600 text-sm font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
            >
              {loginMutation.isPending ? t('login.submitting') : t('login.submit')}
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
            label={t('login.submit')}
          />
        </div>

        <p className="mt-4 text-center text-sm text-slate-500 dark:text-slate-400">
          {t('login.noAccount')}{' '}
          <Link to="/register" className="font-semibold text-brand-600 hover:text-brand-700 dark:text-brand-400 dark:hover:text-brand-300">
            {t('login.registerLink')}
          </Link>
        </p>
      </div>
    </div>
  );
};
