import axios from 'axios'

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

// 401 → đẩy về /login (cookie đã hết hạn / chưa đăng nhập). Backend tự xoá cookie.
api.interceptors.response.use(
  (res) => res,
  (error) => {
    if (error.response?.status === 401) {
      if (window.location.pathname !== '/login') {
        window.location.assign('/login')
      }
    }
    return Promise.reject(error)
  },
)

export default api
