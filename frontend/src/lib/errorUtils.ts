import axios from 'axios';
import toast from 'react-hot-toast';

export interface ApiErrorResponse {
  error?: string;
  message?: string;
  details?: string[];
  traceId?: string;
}

export interface HandleApiErrorOptions {
  onConflict?: () => void;
  navigate?: (path: string) => void;
  silent?: boolean;
}

/**
 * Parse lỗi Axios theo format backend chuẩn và hiển thị toast.
 * Ưu tiên: Xử lý status code (409, 403, 502) > details[] > message > fallbackMessage
 */
export const handleApiError = (
  err: unknown, 
  fallbackMessage: string = 'Có lỗi xảy ra',
  options?: HandleApiErrorOptions
): void => {
  if (import.meta.env.DEV) {
    console.error('[API Error]:', err);
  }

  if (axios.isAxiosError(err)) {
    const status = err.response?.status;
    const data = err.response?.data as ApiErrorResponse | undefined;

    if (status === 409) {
      if (!options?.silent) {
        toast.error(data?.message || 'Dữ liệu trên máy chủ đã thay đổi. Đang tự động cập nhật lại...');
      }
      if (options?.onConflict) {
        options.onConflict();
      }
      return;
    }

    if (status === 403) {
      if (data?.error === 'CsrfError') {
        return; // Interceptor đã xử lý CsrfError
      }
      if (!options?.silent) {
        toast.error('Quyền truy cập không đủ (Thiếu scope). Vui lòng kết nối lại tài khoản.');
      }
      if (options?.navigate) {
        options.navigate('/integrations');
      }
      return;
    }

    if (status === 502) {
      if (!options?.silent) {
        toast.error('Lỗi từ nhà cung cấp dịch vụ (Google/Jira). Vui lòng thử lại sau.');
      }
      return;
    }
    
    if (data?.details && data.details.length > 0) {
      if (!options?.silent) {
        toast.error(data.details[0]); // Chỉ hiển thị lỗi đầu tiên tránh spam toast
      }
      return;
    }
    
    if (data?.message) {
      if (!options?.silent) {
        toast.error(data.message);
      }
      return;
    }

    if (data?.error) {
      if (!options?.silent) {
        toast.error(data.error);
      }
      return;
    }
  }
  
  if (!options?.silent) {
    toast.error(fallbackMessage);
  }
};
