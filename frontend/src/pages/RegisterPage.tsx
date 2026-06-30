import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { Link, useNavigate } from 'react-router-dom';
import toast from 'react-hot-toast';
import { AlertCircle } from 'lucide-react';
import api from '../lib/api';
import { authApi } from '../lib/authApi';
import { EMAIL_RE } from '../lib/validation';
import { GoogleSignInButton } from '../components/auth/GoogleSignInButton';
import type { ApiError } from '../types/auth';

export const RegisterPage = () => {
  const navigate = useNavigate();
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [errFullName, setErrFullName] = useState('');
  const [errEmail, setErrEmail] = useState('');
  const [errPwd, setErrPwd] = useState('');
  const [errConfirm, setErrConfirm] = useState('');
  const [banner, setBanner] = useState('');

  const register = useMutation({
    mutationFn: async () => {
      await api.post('/auth/register', { fullName, email, password });
    },
    onSuccess: () => {
      toast.success('Đăng ký thành công, mời đăng nhập');
      navigate('/login', { replace: true });
    },
    onError: (error) => {
      const data = isAxiosError<ApiError>(error) ? error.response?.data : undefined;
      // 409 email trùng hoặc lỗi khác → banner.
      setBanner(data?.message ?? 'Đăng ký thất bại (email có thể đã được sử dụng)');
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

    const eFullName = fullName.trim().length === 0 ? 'Vui lòng nhập họ tên' : '';
    const eEmail = !EMAIL_RE.test(email) ? 'Email không hợp lệ' : '';
    const ePwd = password.length < 8 ? 'Mật khẩu phải từ 8 ký tự trở lên' : '';
    const eConfirm = confirm !== password ? 'Mật khẩu nhập lại không khớp' : '';
    setErrFullName(eFullName);
    setErrEmail(eEmail);
    setErrPwd(ePwd);
    setErrConfirm(eConfirm);
    if (eFullName || eEmail || ePwd || eConfirm) return;

    register.mutate();
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
          <h1 className="mb-1 text-[22px] font-semibold text-gray-900">Tạo tài khoản</h1>
          <p className="mb-5 text-sm text-gray-500">Bắt đầu gom tất cả công việc về một nơi.</p>

          {banner && (
            <div className="mb-4 flex items-center gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-[13px] text-red-700">
              <AlertCircle className="h-4 w-4 flex-none" />
              <span>{banner}</span>
            </div>
          )}

          <form onSubmit={handleRegister} noValidate>
            <label htmlFor="fullName" className="mb-1.5 block text-[13px] font-medium text-gray-900">Họ và tên</label>
            <input
              id="fullName"
              value={fullName}
              onChange={(e) => setFullName(e.target.value)}
              placeholder="Nguyễn Văn A"
              className={`h-[38px] w-full rounded-lg border px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errFullName ? 'border-red-400' : 'border-gray-300'
              }`}
            />
            {errFullName && <div className="mt-1 text-xs text-red-600">{errFullName}</div>}

            <label htmlFor="email" className="mb-1.5 mt-3.5 block text-[13px] font-medium text-gray-900">Email</label>
            <input
              id="email"
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="ban@congty.vn"
              className={`h-[38px] w-full rounded-lg border px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errEmail ? 'border-red-400' : 'border-gray-300'
              }`}
            />
            {errEmail && <div className="mt-1 text-xs text-red-600">{errEmail}</div>}

            <label htmlFor="password" className="mb-1.5 mt-3.5 block text-[13px] font-medium text-gray-900">Mật khẩu</label>
            <input
              id="password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Tối thiểu 8 ký tự"
              className={`h-[38px] w-full rounded-lg border px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errPwd ? 'border-red-400' : 'border-gray-300'
              }`}
            />
            {errPwd && <div className="mt-1 text-xs text-red-600">{errPwd}</div>}

            <label htmlFor="confirm" className="mb-1.5 mt-3.5 block text-[13px] font-medium text-gray-900">Nhập lại mật khẩu</label>
            <input
              id="confirm"
              type="password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              placeholder="Nhập lại mật khẩu"
              className={`h-[38px] w-full rounded-lg border px-3 text-sm outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-500/30 ${
                errConfirm ? 'border-red-400' : 'border-gray-300'
              }`}
            />
            {errConfirm && <div className="mt-1 text-xs text-red-600">{errConfirm}</div>}

            <button
              type="submit"
              disabled={register.isPending}
              className="mt-4 h-10 w-full rounded-lg bg-brand-600 text-sm font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
            >
              {register.isPending ? 'Đang tạo...' : 'Đăng ký'}
            </button>
          </form>

          {/* Divider */}
          <div className="my-[18px] flex items-center gap-3">
            <div className="h-px flex-1 bg-gray-200" />
            <span className="text-xs text-gray-400">hoặc</span>
            <div className="h-px flex-1 bg-gray-200" />
          </div>

          <GoogleSignInButton
            isPending={googleMutation.isPending}
            onClick={() => {
              setBanner('');
              googleMutation.mutate();
            }}
            label="Đăng ký"
          />
        </div>

        <p className="mt-4 text-center text-sm text-gray-500">
          Đã có tài khoản?{' '}
          <Link to="/login" className="font-semibold text-brand-600 hover:text-brand-700">
            Đăng nhập
          </Link>
        </p>
      </div>
    </div>
  );
};
