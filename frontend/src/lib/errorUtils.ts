import axios from 'axios';
import toast from 'react-hot-toast';

export interface ApiErrorResponse {
  error?: string;
  message?: string;
  details?: string[];
  traceId?: string;
}

/**
 * Parse lỗi Axios theo format backend chuẩn và hiển thị toast.
 * Ưu tiên: details[] > message > fallbackMessage
 */
export const handleApiError = (err: unknown, fallbackMessage: string): void => {
  if (axios.isAxiosError(err)) {
    const data = err.response?.data as ApiErrorResponse | undefined;
    
    if (data?.details && data.details.length > 0) {
      data.details.forEach(d => toast.error(d));
      return;
    }
    
    if (data?.message) {
      toast.error(data.message);
      return;
    }
  }
  
  toast.error(fallbackMessage);
};
