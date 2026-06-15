import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { Link, useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import api from '../lib/api'

export const RegisterPage = () => {
  const navigate = useNavigate()
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  const register = useMutation({
    mutationFn: async () => {
      await api.post('/auth/register', { fullName, email, password })
    },
    onSuccess: () => {
      toast.success('Đăng ký thành công, mời đăng nhập')
      navigate('/login', { replace: true })
    },
    onError: (error) => {
      const msg = isAxiosError<{ message?: string }>(error) ? error.response?.data?.message : undefined
      toast.error(msg || 'Đăng ký thất bại (email có thể đã được sử dụng)')
    },
  })

  const handleRegister = (e: React.FormEvent) => {
    e.preventDefault()
    
    if (fullName.trim().length === 0) {
      toast.error('Vui lòng nhập họ tên')
      return
    }
    if (!email.match(/^[^\s@]+@[^\s@]+\.[^\s@]+$/)) {
      toast.error('Email không hợp lệ')
      return
    }
    if (password.length < 8) {
      toast.error('Mật khẩu phải từ 8 ký tự trở lên')
      return
    }
    
    register.mutate()
  }

  return (
    <div className="flex h-full items-center justify-center bg-gray-50">
      <form
        onSubmit={handleRegister}
        className="w-80 space-y-4 rounded-lg border border-gray-200 bg-white p-6 shadow-sm"
      >
        <h1 className="text-xl font-bold text-gray-900">Đăng ký</h1>
        <input
          required
          placeholder="Họ tên"
          value={fullName}
          onChange={(e) => setFullName(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
        />
        <input
          type="email"
          required
          placeholder="Email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
        />
        <input
          type="password"
          required
          minLength={8}
          placeholder="Mật khẩu (≥ 8 ký tự)"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
        />
        <button
          type="submit"
          disabled={register.isPending}
          className="w-full rounded bg-brand-600 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50"
        >
          {register.isPending ? 'Đang tạo...' : 'Đăng ký'}
        </button>
        <p className="text-center text-sm text-gray-500">
          Đã có tài khoản?{' '}
          <Link to="/login" className="text-brand-600 hover:underline">
            Đăng nhập
          </Link>
        </p>
      </form>
    </div>
  )
}
