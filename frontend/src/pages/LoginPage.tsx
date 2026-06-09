import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import api, { tokenStore } from '../lib/api'

interface LoginResponse {
  accessToken: string
  user: { id: string; email: string; fullName: string; role: string }
}

export default function LoginPage() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  const login = useMutation({
    mutationFn: async () => {
      const { data } = await api.post<LoginResponse>('/auth/login', { email, password })
      return data
    },
    onSuccess: (data) => {
      tokenStore.set(data.accessToken)
      toast.success(`Chào ${data.user.fullName}`)
      navigate('/inbox', { replace: true })
    },
    onError: () => toast.error('Email hoặc mật khẩu không đúng'),
  })

  return (
    <div className="flex h-full items-center justify-center bg-gray-50">
      <form
        onSubmit={(e) => {
          e.preventDefault()
          login.mutate()
        }}
        className="w-80 space-y-4 rounded-lg border border-gray-200 bg-white p-6 shadow-sm"
      >
        <h1 className="text-xl font-bold text-gray-900">Đăng nhập</h1>
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
          placeholder="Mật khẩu"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          className="w-full rounded border border-gray-300 px-3 py-2 text-sm"
        />
        <button
          type="submit"
          disabled={login.isPending}
          className="w-full rounded bg-brand-600 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50"
        >
          {login.isPending ? 'Đang đăng nhập...' : 'Đăng nhập'}
        </button>
        <p className="text-center text-sm text-gray-500">
          Chưa có tài khoản?{' '}
          <Link to="/register" className="text-brand-600 hover:underline">
            Đăng ký
          </Link>
        </p>
      </form>
    </div>
  )
}
