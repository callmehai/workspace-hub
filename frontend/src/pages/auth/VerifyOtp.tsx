import { useEffect, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { AlertCircle } from 'lucide-react';
import { useAuth } from '../../hooks/useAuth';
import { authApi } from '../../lib/authApi';
import type { ApiError } from '../../types/auth';
import { useI18n } from '../../hooks/useI18n';

/**
 * SCRUM-64 — màn nhập OTP xác minh SĐT. Vào từ Register (state.email) hoặc Login bị
 * chặn (PHONE_NOT_VERIFIED). Verify thành công → đăng nhập luôn (cookie auth set).
 */
export const VerifyOtp = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { login } = useAuth();
  const { t } = useI18n();

  // email + cooldown ban đầu truyền qua router state khi điều hướng từ Register/Login.
  const state = (location.state ?? {}) as { email?: string; cooldown?: number };
  const [email] = useState(state.email ?? '');
  const [code, setCode] = useState('');
  const [banner, setBanner] = useState('');
  const [cooldown, setCooldown] = useState(state.cooldown ?? 0);

  // Không có email (vào trực tiếp URL) → quay về đăng ký.
  useEffect(() => {
    if (!email) navigate('/register', { replace: true });
  }, [email, navigate]);

  // Đếm ngược cooldown gửi lại.
  useEffect(() => {
    if (cooldown <= 0) return;
    const t = setInterval(() => setCooldown((c) => (c > 0 ? c - 1 : 0)), 1000);
    return () => clearInterval(t);
  }, [cooldown]);

  const verify = useMutation({
    mutationFn: () => authApi.verifyOtp(email, code),
    onSuccess: (data) => {
      login(data.user);
      toast.success(t('verifyOtp.success'));
      navigate('/', { replace: true });
    },
    onError: (err) => {
      const msg = isAxiosError<ApiError>(err) ? err.response?.data?.message : undefined;
      setBanner(msg ?? t('verifyOtp.invalidCode'));
    },
  });

  const resend = useMutation({
    mutationFn: () => authApi.sendOtp(email),
    onSuccess: (seconds) => {
      setCooldown(seconds);
      toast.success(t('verifyOtp.resent'));
    },
    onError: (err) => {
      const msg = isAxiosError<ApiError>(err) ? err.response?.data?.message : undefined;
      setBanner(msg ?? t('verifyOtp.resendFail'));
    },
  });

  const handleVerify = (e: React.FormEvent) => {
    e.preventDefault();
    setBanner('');
    if (!/^\d{6}$/.test(code)) {
      setBanner(t('verifyOtp.sixDigits'));
      return;
    }
    verify.mutate();
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50 dark:bg-slate-950 p-6 font-sans text-gray-900 dark:text-slate-100">
      <div className="w-full max-w-[404px]">
        <div className="mb-[22px] flex items-center justify-center gap-2.5">
          <div className="flex h-9 w-9 items-center justify-center rounded-[9px] bg-brand-600 text-[17px] font-bold text-white">
            W
          </div>
          <span className="text-lg font-semibold text-gray-900 dark:text-slate-100">Workspace Hub</span>
        </div>

        <div className="rounded-[14px] border border-gray-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-7 shadow-sm">
          <h1 className="mb-1 text-[22px] font-semibold text-gray-900 dark:text-slate-100">{t('verifyOtp.title')}</h1>
          <p className="mb-5 text-sm text-gray-500 dark:text-slate-400">
            {t('verifyOtp.subtitle')}{email ? ` (${email})` : ''}.
          </p>

          {banner && (
            <div className="mb-4 flex items-center gap-2 rounded-lg border border-red-200 dark:border-red-500/30 bg-red-50 dark:bg-red-500/10 px-3 py-2.5 text-[13px] text-red-700 dark:text-red-400">
              <AlertCircle className="h-4 w-4 flex-none" />
              <span>{banner}</span>
            </div>
          )}

          <form onSubmit={handleVerify} noValidate>
            <label htmlFor="otp" className="mb-1.5 block text-[13px] font-medium text-gray-900 dark:text-slate-100">{t('verifyOtp.label')}</label>
            <input
              id="otp"
              inputMode="numeric"
              maxLength={6}
              value={code}
              onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
              placeholder="••••••"
              className="h-[38px] w-full rounded-lg border border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 dark:placeholder-slate-500 px-3 text-center text-lg tracking-[0.4em] outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
            />

            <button
              type="submit"
              disabled={verify.isPending}
              className="mt-4 h-10 w-full rounded-lg bg-brand-600 text-sm font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
            >
              {verify.isPending ? t('verifyOtp.verifying') : t('verifyOtp.verify')}
            </button>
          </form>

          <div className="mt-4 text-center text-sm text-gray-500 dark:text-slate-400">
            {cooldown > 0 ? (
              <span>{t('verifyOtp.resendIn', { s: cooldown })}</span>
            ) : (
              <button
                type="button"
                onClick={() => { setBanner(''); resend.mutate(); }}
                disabled={resend.isPending}
                className="font-semibold text-brand-600 hover:text-brand-700 disabled:opacity-50"
              >
                {resend.isPending ? t('sendEmail.sending') : t('verifyOtp.resend')}
              </button>
            )}
          </div>
        </div>

        <p className="mt-4 text-center text-sm text-gray-500 dark:text-slate-400">
          <Link to="/login" className="font-semibold text-brand-600 hover:text-brand-700">
            {t('verifyOtp.backToLogin')}
          </Link>
        </p>
      </div>
    </div>
  );
};
