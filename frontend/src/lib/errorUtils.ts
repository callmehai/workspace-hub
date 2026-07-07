import axios from 'axios';
import toast from 'react-hot-toast';
import { translate } from '../i18n/translations';

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
  fallbackMessage: string = translate('errors.generic'),
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
        toast.error(data?.message || translate('errors.conflict'));
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
        toast.error(translate('errors.forbiddenScope'));
        if (options?.navigate) {
          options.navigate('/integrations');
        }
      }
      return;
    }

    if (status === 502) {
      if (!options?.silent) {
        toast.error(translate('errors.provider'));
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
