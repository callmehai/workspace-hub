import axios, { isAxiosError } from 'axios'
import type { InternalAxiosRequestConfig } from 'axios'
import toast from 'react-hot-toast'

/** Cờ đánh dấu request đã thử refresh 1 lần (tránh vòng lặp refresh vô hạn). */
type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean }

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
  timeout: 10000,
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

// SCRUM-63: tự refresh access token khi gặp 401. Single-flight — nhiều request 401
// đồng thời chỉ kích hoạt MỘT lần /auth/refresh, rồi cùng retry sau khi xong.
let refreshPromise: Promise<void> | null = null

const redirectToLogin = () => {
  if (window.location.pathname !== '/login') {
    window.location.assign('/login')
  }
}

const runRefresh = (): Promise<void> => {
  if (!refreshPromise) {
    // Dùng axios "trần" để gọi /auth/refresh, tránh đệ quy interceptor.
    refreshPromise = axios
      .post('/auth/refresh', null, {
        baseURL: api.defaults.baseURL,
        withCredentials: true,
      })
      .then(() => undefined)
      .finally(() => {
        refreshPromise = null
      })
  }
  return refreshPromise
}

api.interceptors.response.use(
  (res) => res,
  async (error) => {
    const status = error.response?.status
    const original = error.config as RetriableConfig | undefined
    const url: string = original?.url ?? ''

    // 401 → thử refresh 1 lần rồi retry. Không refresh cho chính endpoint auth
    // (login/refresh/logout) và không retry lần 2 (_retried).
    const isAuthEndpoint = url.includes('/auth/login') || url.includes('/auth/refresh') ||
      url.includes('/auth/logout') || url.includes('/auth/google')
    if (status === 401 && original && !original._retried && !isAuthEndpoint) {
      original._retried = true
      try {
        await runRefresh()
        return api(original) // retry request gốc với cookie access mới
      } catch {
        redirectToLogin() // refresh fail → hết phiên
        return Promise.reject(error)
      }
    }

    // 401 còn lại (kể cả refresh fail) → về /login.
    if (status === 401) {
      redirectToLogin()
    }
    // 403 CsrfError → cookie CSRF thiếu/lệch (bị xoá tay hoặc trình duyệt chặn cookie).
    else if (status === 403 && error.response?.data?.error === 'CsrfError') {
      toast.error('Phiên bảo mật không hợp lệ. Vui lòng tải lại trang và thử lại.')
    }
    return Promise.reject(error)
  },
)

export const handleApiError = (
  error: unknown,
  fallbackMessage: string = 'Có lỗi xảy ra',
  options?: {
    onConflict?: () => void;
    navigate?: (path: string) => void;
  }
) => {
  if (isAxiosError(error)) {
    const status = error.response?.status;
    const errorData = error.response?.data as { message?: string } | undefined;

    if (status === 409) {
      toast.error('Dữ liệu trên máy chủ đã thay đổi. Đang tự động cập nhật lại...');
      if (options?.onConflict) {
        options.onConflict();
      }
      return;
    }

    if (status === 403) {
      toast.error('Quyền truy cập không đủ (Thiếu scope). Vui lòng kết nối lại tài khoản.');
      if (options?.navigate) {
        options.navigate('/integrations');
      }
      return;
    }

    if (status === 502) {
      toast.error('Lỗi từ nhà cung cấp dịch vụ (Google/Jira). Vui lòng thử lại sau.');
      return;
    }

    const serverMessage = errorData?.message;
    toast.error(serverMessage || fallbackMessage);
  } else {
    toast.error(fallbackMessage);
  }
};

export default api
