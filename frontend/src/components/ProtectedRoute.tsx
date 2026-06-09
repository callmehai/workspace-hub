import { Navigate, Outlet } from 'react-router-dom'
import { tokenStore } from '../lib/api'

/**
 * Chặn route cần đăng nhập. Chưa có token → redirect /login.
 * (MVP: chỉ check token tồn tại; verify thật qua /api/auth/me khi BE ready — SCRUM-10.)
 */
export default function ProtectedRoute() {
  const token = tokenStore.get()
  return token ? <Outlet /> : <Navigate to="/login" replace />
}
