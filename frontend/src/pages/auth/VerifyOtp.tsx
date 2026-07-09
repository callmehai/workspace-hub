import { useEffect, useState, useRef } from 'react';
import { useMutation } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { AlertCircle } from 'lucide-react';
import { useAuth } from '../../hooks/useAuth';
import { authApi } from '../../lib/authApi';
import type { ApiError } from '../../types/auth';
import { useI18n } from '../../hooks/useI18n';
import { auth } from '../../lib/firebase';
import { RecaptchaVerifier, signInWithPhoneNumber } from 'firebase/auth';
import type { ConfirmationResult } from 'firebase/auth';

declare global {
  interface Window {
    recaptchaVerifier: RecaptchaVerifier | undefined;
  }
}

// Chấp nhận SĐT VN dạng 0xxxxxxxxx (10 số) hoặc E.164 (+84xxxxxxxxx).
const PHONE_RE = /^(0[3-9]\d{8}|\+[1-9]\d{7,14})$/;
const normalizePhone = (phone: string): string => phone.startsWith('0') ? '+84' + phone.slice(1) : phone;

export const VerifyOtp = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { login } = useAuth();
  const { t } = useI18n();

  const state = (location.state ?? {}) as { email?: string; phone?: string };
  const [email] = useState(state.email ?? '');
  
  const [phone, setPhone] = useState(state.phone ?? '');
  const [phoneInput, setPhoneInput] = useState('');
  
  const [code, setCode] = useState('');
  const [banner, setBanner] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [confirmationResult, setConfirmationResult] = useState<ConfirmationResult | null>(null);

  const recaptchaRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!email) navigate('/register', { replace: true });
  }, [email, navigate]);

  const resetRecaptcha = () => {
    if (window.recaptchaVerifier) {
      try {
        window.recaptchaVerifier.clear();
      } catch {
        // ignore if already cleared
      }
      window.recaptchaVerifier = undefined;
    }
    // Clear the DOM container so a new widget can be rendered
    const container = document.getElementById('recaptcha-container');
    if (container) {
      container.innerHTML = '';
    }
  };

  const initRecaptcha = () => {
    // Always reset first to avoid "already rendered" error
    resetRecaptcha();
    window.recaptchaVerifier = new RecaptchaVerifier(auth, 'recaptcha-container', {
      size: 'invisible',
      callback: () => {
        // solved
      },
      'expired-callback': () => {
        console.warn('Recaptcha expired');
        resetRecaptcha();
        setBanner(t('verifyOtp.resendFail') || 'Mã captcha hết hạn, vui lòng thử lại.');
      }
    });
  };

  const sendFirebaseOtp = async (phoneToUse: string) => {
    if (!phoneToUse) {
      setBanner(t('verifyOtp.noPhoneToResend') || 'Không có số điện thoại để gửi mã.');
      return;
    }
    try {
      setBanner('');
      setIsSending(true);
      initRecaptcha();
      const appVerifier = window.recaptchaVerifier!;
      const confirmResult = await signInWithPhoneNumber(auth, phoneToUse, appVerifier);
      setConfirmationResult(confirmResult);
      toast.success(t('verifyOtp.resent') || 'Đã gửi mã OTP qua Firebase.');
    } catch (error: unknown) {
      console.error('Firebase Auth Error:', error);
      const msg = error instanceof Error ? error.message : '';
      setBanner(msg || t('verifyOtp.resendFail') || '');
      // Reset recaptcha so user can retry
      resetRecaptcha();
    } finally {
      setIsSending(false);
    }
  };

  // Tự động gửi OTP lần đầu khi vào trang nếu có state.phone
  useEffect(() => {
    if (state.phone && !confirmationResult && !isSending) {
      const phoneToUse = state.phone;
      setTimeout(() => {
        void sendFirebaseOtp(phoneToUse);
      }, 0);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const verify = useMutation({
    mutationFn: async () => {
      if (!confirmationResult) throw new Error('Chưa gửi mã OTP.');
      const result = await confirmationResult.confirm(code);
      const idToken = await result.user.getIdToken();
      return authApi.verifyPhone(email, idToken);
    },
    onSuccess: (data) => {
      login(data.user);
      toast.success(t('verifyOtp.success'));
      navigate('/', { replace: true });
    },
    onError: (err: unknown) => {
      if (isAxiosError<ApiError>(err)) {
        const msg = err.response?.data?.message;
        setBanner(msg ?? t('verifyOtp.invalidCode'));
      } else {
        const msg = err instanceof Error ? err.message : '';
        setBanner(msg || t('verifyOtp.invalidCode') || '');
      }
    },
  });

  const handleVerifyCode = (e: React.FormEvent) => {
    e.preventDefault();
    setBanner('');
    if (!/^\d{6}$/.test(code)) {
      setBanner(t('verifyOtp.sixDigits') || 'Mã OTP gồm 6 chữ số.');
      return;
    }
    verify.mutate();
  };

  const handleRequestOtp = (e: React.FormEvent) => {
    e.preventDefault();
    setBanner('');
    if (!PHONE_RE.test(phoneInput)) {
      setBanner(t('verifyOtp.invalidPhoneFormat') || 'Số điện thoại không hợp lệ (vd: 0912345678).');
      return;
    }
    const normalized = normalizePhone(phoneInput);
    setPhone(normalized);
    sendFirebaseOtp(normalized);
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
            {t('verifyOtp.subtitle')}{phone ? ` (${phone})` : ''}.
          </p>

          {banner && (
            <div className="mb-4 flex items-center gap-2 rounded-lg border border-red-200 dark:border-red-500/30 bg-red-50 dark:bg-red-500/10 px-3 py-2.5 text-[13px] text-red-700 dark:text-red-400">
              <AlertCircle className="h-4 w-4 flex-none" />
              <span>{banner}</span>
            </div>
          )}

          <div id="recaptcha-container" ref={recaptchaRef}></div>

          {!phone && !confirmationResult ? (
            <form onSubmit={handleRequestOtp} noValidate>
              <label htmlFor="phoneInput" className="mb-1.5 block text-[13px] font-medium text-gray-900 dark:text-slate-100">{t('verifyOtp.enterRegisteredPhone') || 'Nhập số điện thoại đã đăng ký'}</label>
              <input
                id="phoneInput"
                type="tel"
                value={phoneInput}
                onChange={(e) => setPhoneInput(e.target.value)}
                placeholder="0912345678"
                className="h-[38px] w-full rounded-lg border border-gray-300 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100 dark:placeholder-slate-500 px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30"
              />
              <button
                type="submit"
                disabled={isSending}
                className="mt-4 h-10 w-full rounded-lg bg-brand-600 text-sm font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
              >
                {isSending ? t('verifyOtp.sending') || 'Đang gửi...' : t('verifyOtp.sendOtp') || 'Gửi mã xác nhận'}
              </button>
            </form>
          ) : (
            <form onSubmit={handleVerifyCode} noValidate>
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
                disabled={verify.isPending || isSending}
                className="mt-4 h-10 w-full rounded-lg bg-brand-600 text-sm font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
              >
                {verify.isPending ? t('verifyOtp.verifying') : t('verifyOtp.verify')}
              </button>
            </form>
          )}

          {phone && (
            <div className="mt-4 text-center text-sm text-gray-500 dark:text-slate-400">
              <button
                type="button"
                onClick={() => { setBanner(''); sendFirebaseOtp(phone); }}
                disabled={isSending || verify.isPending}
                className="font-semibold text-brand-600 hover:text-brand-700 disabled:opacity-50"
              >
                {isSending ? t('verifyOtp.sending') || 'Đang gửi...' : t('verifyOtp.resend')}
              </button>
            </div>
          )}
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
