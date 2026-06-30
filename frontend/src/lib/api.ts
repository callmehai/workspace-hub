import axios from 'axios'
import toast from 'react-hot-toast'

const CSRF_COOKIE = 'wh_csrf'
const CSRF_HEADER = 'X-CSRF-Token'

/** Đọc cookie không-HttpOnly theo tên (chỉ wh_csrf — access token là HttpOnly, JS không đọc được). */
const readCookie = (name: string): string | null => {
  const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`))
  return match ? decodeURIComponent(match[1]) : null
}

// Dev: dùng proxy '/api' (xem vite.config.ts). Prod: set VITE_API_URL.
// withCredentials: gửi kèm cookie auth (wh_access HttpOnly) trên mọi request (SCRUM-62).
const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? '/api',
  withCredentials: true,
})

// CSRF double-submit: gắn header X-CSRF-Token = cookie wh_csrf cho request mutating.
api.interceptors.request.use((config) => {
  const method = (config.method ?? 'get').toLowerCase()
  if (['post', 'put', 'patch', 'delete'].includes(method)) {
    const csrf = readCookie(CSRF_COOKIE)
    if (csrf) {
      config.headers[CSRF_HEADER] = csrf
    }
  }
  return config
})

api.interceptors.response.use(
  (res) => res,
  (error) => {
    const status = error.response?.status
    // 401 → đẩy về /login (cookie đã hết hạn / chưa đăng nhập). Backend tự xoá cookie.
    if (status === 401) {
      if (window.location.pathname !== '/login') {
        window.location.assign('/login')
      }
    }
    // 403 CsrfError → cookie CSRF thiếu/lệch (bị xoá tay hoặc trình duyệt chặn cookie).
    // Báo rõ thay vì để 403 im lặng khó debug.
    else if (status === 403 && error.response?.data?.error === 'CsrfError') {
      toast.error('Phiên bảo mật không hợp lệ. Vui lòng tải lại trang và thử lại.')
    }
    return Promise.reject(error)
  },
)

export default api
