import axios from 'axios';
import toast from 'react-hot-toast';
import { translate, type TranslationKey } from '../i18n/translations';

export interface ApiErrorResponse {
  error?: string;
  /** Mã lỗi nghiệp vụ ổn định do BE trả (xem `ErrorCodes` phía backend). Có thể vắng mặt. */
  code?: string;
  message?: string;
  details?: string[];
  traceId?: string;
}

/**
 * Map mã lỗi BE → key i18n.
 *
 * BE là API nên KHÔNG dịch: `message` giữ tiếng Anh cho log/Swagger, còn `code` là hợp đồng ổn
 * định để FE dịch theo ngôn ngữ đang chọn. Mã không có trong bảng này (hoặc response không kèm
 * `code`) sẽ rơi về message chung theo status code — không vỡ, chỉ kém cụ thể hơn.
 */
const ERROR_CODE_KEYS: Record<string, TranslationKey> = {
  FOLDER_OWNER_ONLY: 'errors.folderOwnerOnly',
  ITEM_NOT_OWNED: 'errors.itemNotOwned',
  ITEMS_NOT_OWNED: 'errors.itemsNotOwned',
  ITEM_ALREADY_IN_FOLDER: 'errors.itemAlreadyInFolder',
  SHARED_VIEWER_READ_ONLY: 'errors.sharedViewerReadOnly',
  NOT_YOUR_CONNECTION: 'errors.notYourConnection',
  ITEM_NOT_CALENDAR_EVENT: 'errors.itemNotCalendarEvent',
  ITEM_NOT_LINKED_TO_CONNECTION: 'errors.itemNotLinkedToConnection',
};

/** Message đã dịch theo `code` của BE; undefined nếu không có mã hoặc mã chưa được map. */
function translateErrorCode(data: ApiErrorResponse | undefined): string | undefined {
  const key = data?.code ? ERROR_CODE_KEYS[data.code] : undefined;
  return key ? translate(key) : undefined;
}

export interface HandleApiErrorOptions {
  onConflict?: () => void;
  conflictMessage?: string;
  navigate?: (path: string) => void;
  silent?: boolean;
}

/** Status HTTP từ lỗi Axios (nếu có). */
export function getApiErrorStatus(err: unknown): number | undefined {
  if (axios.isAxiosError(err)) return err.response?.status;
  return undefined;
}

export function isNotFoundApiError(err: unknown): boolean {
  return getApiErrorStatus(err) === 404;
}

/**
 * Parse lỗi Axios theo format backend chuẩn và hiển thị toast.
 * Ưu tiên: Xử lý status code (404, 409, 403, 502) > details[] > message > fallbackMessage
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

    // Ưu tiên: mã lỗi đã dịch > message thô của BE (tiếng Anh) > message chung theo status.
    const localized = translateErrorCode(data);

    if (status === 404) {
      if (!options?.silent) {
        toast.error(localized || data?.message || translate('item.notFoundHint'));
      }
      return;
    }

    if (status === 409) {
      if (!options?.silent) {
        toast.error(options?.conflictMessage || localized || data?.message || translate('errors.conflict'));
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
        // Ưu tiên message BE (vd. Viewer folder chia sẻ cố sửa, "not your connection").
        // KHÔNG điều hướng sang /integrations: BE không phát 403-thiếu-scope (mọi
        // ForbiddenException → "ForbiddenError"; lỗi scope/token thật đi 401/502). Trước đây
        // mọi 403 đều đá người dùng sang màn Kết nối dịch vụ, rất khó hiểu.
        toast.error(localized || data?.message || translate('errors.forbiddenScope'));
      }
      return;
    }

    if (status === 502) {
      if (!options?.silent) {
        toast.error(translate('errors.provider'));
      }
      return;
    }
    
    // Mã lỗi đã dịch (vd 422 BusinessRule) — đặt trước details/message để không lộ tiếng Anh.
    if (localized) {
      if (!options?.silent) {
        toast.error(localized);
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
