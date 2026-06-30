import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import toast from 'react-hot-toast';
import { AlertCircle } from 'lucide-react';
import { useAuth } from '../hooks/useAuth';
import api from '../lib/api';
import type { ApiError, AuthResponse } from '../types/auth';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export const Login = () => {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errEmail, setErrEmail] = useState('');
  const [errPwd, setErrPwd] = useState('');
  const [banner, setBanner] = useState('');

  const loginMutation = useMutation({
    mutationFn: async () =>
      (await api.post<AuthResponse>('/auth/login', { email, password })).data,
    onSuccess: (data) => {
      login(data.accessToken, data.user);
      toast.success('Đăng nhập thành công!');
      navigate('/', { replace: true });
    },
    onError: (error) => {
      const data = isAxiosError<ApiError>(error) ? error.response?.data : undefined;
      // 401 (sai thông tin / account bị khoá) hiển thị ở banner; còn lại fallback chung.
      setBanner(data?.message ?? 'Đăng nhập thất bại. Vui lòng kiểm tra lại thông tin.');
    },
  });

  const handleLogin = (e: React.FormEvent) => {
    e.preventDefault();
    setBanner('');

    const eEmail = !EMAIL_RE.test(email) ? 'Email không hợp lệ' : '';
    const ePwd = password.length < 8 ? 'Mật khẩu phải từ 8 ký tự trở lên' : '';
    setErrEmail(eEmail);
    setErrPwd(ePwd);
    if (eEmail || ePwd) return;

    loginMutation.mutate();
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50 p-6 font-sans text-gray-900">
      <div className="w-full max-w-[404px]">
        {/* Logo */}
        <div className="mb-[22px] flex items-center justify-center gap-2.5">
          <div className="flex h-9 w-9 items-center justify-center rounded-[9px] bg-brand-600 text-[17px] font-bold text-white">
            W
          </div>
          <span className="text-lg font-semibold text-gray-900">Workspace Hub</span>
        </div>

        {/* Card */}
        <div className="rounded-[14px] border border-gray-200 bg-white p-7 shadow-sm">
          <h1 className="mb-1 text-[22px] font-semibold text-gray-900">Đăng nhập</h1>
          <p className="mb-5 text-sm text-gray-500">Chào mừng quay lại Workspace Hub của bạn.</p>

          {banner && (
            <div className="mb-4 flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-[13px] text-red-700">
              <AlertCircle className="h-4 w-4 flex-none" />
              <span>{banner}</span>
            </div>
          )}

          <form onSubmit={handleLogin} noValidate>
            <label className="mb-1.5 block text-[13px] font-medium text-gray-900">Email</label>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="ban@congty.vn"
              className={`h-[38px] w-full rounded-lg border px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errEmail ? 'border-red-400' : 'border-gray-300'
              }`}
            />
            {errEmail && <div className="mt-1 text-xs text-red-600">{errEmail}</div>}

            <label className="mb-1.5 mt-3.5 block text-[13px] font-medium text-gray-900">Mật khẩu</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Tối thiểu 8 ký tự"
              className={`h-[38px] w-full rounded-lg border px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errPwd ? 'border-red-400' : 'border-gray-300'
              }`}
            />
            {errPwd && <div className="mt-1 text-xs text-red-600">{errPwd}</div>}

            <button
              type="submit"
              disabled={loginMutation.isPending}
              className="mt-4 h-10 w-full rounded-lg bg-brand-600 text-sm font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
            >
              {loginMutation.isPending ? 'Đang đăng nhập...' : 'Đăng nhập'}
            </button>
          </form>

          {/* Divider */}
          <div className="my-[18px] flex items-center gap-3">
            <div className="h-px flex-1 bg-gray-200" />
            <span className="text-xs text-gray-400">hoặc</span>
            <div className="h-px flex-1 bg-gray-200" />
          </div>

          <button
            type="button"
            className="flex h-10 w-full items-center justify-center gap-2.5 rounded-lg border border-gray-300 bg-white text-sm font-medium text-gray-900 transition-colors hover:bg-gray-50"
          >
            <svg className="h-5 w-5" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
              <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" fill="#4285F4" />
              <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" fill="#34A853" />
              <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" fill="#FBBC05" />
              <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" fill="#EA4335" />
            </svg>
            <span>Đăng nhập bằng Google</span>
          </button>
        </div>

        <p className="mt-4 text-center text-sm text-gray-500">
          Chưa có tài khoản?{' '}
          <Link to="/register" className="font-semibold text-brand-600 hover:text-brand-700">
            Đăng ký
          </Link>
        </p>
      </div>
    </div>
  );
};
