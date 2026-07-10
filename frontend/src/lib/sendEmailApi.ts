import api from './api';
import { contactsApi, type ContactSuggestion } from './contactsApi';

export type { ContactSuggestion };

/** File người dùng tự đính kèm — nội dung base64 (không kèm prefix data URI). */
export interface AttachmentUpload {
  filename: string;
  mimeType: string;
  contentBase64: string;
}

/** Tổng dung lượng đính kèm tối đa (khớp giới hạn BE / Gmail ~25MB). */
export const MAX_ATTACHMENT_TOTAL_BYTES = 25 * 1024 * 1024;

/** Đọc 1 File thành AttachmentUpload (base64), tách bỏ prefix "data:...;base64,". */
export function fileToAttachmentUpload(file: File): Promise<AttachmentUpload> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result as string;
      const base64 = result.includes(',') ? result.slice(result.indexOf(',') + 1) : result;
      resolve({
        filename: file.name,
        mimeType: file.type || 'application/octet-stream',
        contentBase64: base64,
      });
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

export interface SendEmailRequest {
  connectionId: string;
  to: string[];
  cc: string[];
  bcc: string[];
  subject: string;
  bodyHtml: string;
  attachments?: AttachmentUpload[];
}

export interface SendEmailResult {
  messageId: string;
  sentAt: string;
}

export interface EmailAttachmentDto {
  attachmentId: string;
  filename: string;
  mimeType: string;
  size: number;
}

export interface EmailThreadMessageDto {
  messageId: string;
  from: string | null;
  to: string[];
  cc: string[];
  bcc: string[];
  subject: string | null;
  bodyHtml: string | null;
  bodyPlainText: string | null;
  occurredAt: string;
  isUnread: boolean;
  isStarred: boolean;
  hasAttachment: boolean;
  attachments: EmailAttachmentDto[];
}

export interface EmailThreadResponse {
  threadId: string;
  subject: string | null;
  messages: EmailThreadMessageDto[];
}

export interface ReplyEmailRequest {
  connectionId: string;
  itemId: string;
  cc?: string[];
  bcc?: string[];
  bodyHtml: string;
  replyAll: boolean;
  attachments?: AttachmentUpload[];
}

export interface ForwardEmailRequest {
  connectionId: string;
  itemId: string;
  to: string[];
  cc?: string[];
  bcc?: string[];
  bodyHtml: string;
  includeAttachments: boolean;
  attachments?: AttachmentUpload[];
}

export interface SendInThreadResult {
  messageId: string;
  threadId: string;
  sentAt: string;
}

export const sendEmailApi = {
  send: async (data: SendEmailRequest): Promise<SendEmailResult> => {
    const response = await api.post('/emails/send', data);
    return response.data;
  },

  getSignature: async (connectionId: string): Promise<string> => {
    const response = await api.get('/emails/signature', { params: { connectionId } });
    return response.data?.signature ?? '';
  },

  /** Gợi ý contact — OData /api/Contacts (SQL push-down). */
  suggestContacts: (connectionId: string, q: string, limit = 10) =>
    contactsApi.suggestContacts(connectionId, q, limit),

  getThread: async (itemId: string): Promise<EmailThreadResponse> => {
    const response = await api.get(`/emails/${itemId}/thread`);
    return response.data;
  },

  reply: async (data: ReplyEmailRequest): Promise<SendInThreadResult> => {
    const response = await api.post('/emails/reply', data);
    return response.data;
  },

  forward: async (data: ForwardEmailRequest): Promise<SendInThreadResult> => {
    const response = await api.post('/emails/forward', data);
    return response.data;
  },

  /**
   * Lấy binary 1 attachment dạng Blob (để preview inline hoặc download).
   * Truyền thẳng messageId + attachmentId (id client đã lấy khi mở thread) — backend
   * gọi Gmail attachments.get trực tiếp, không re-fetch thread (id Gmail đổi mỗi lần đọc).
   * filename/mimeType để backend set Content-Type/tên file tải về.
   */
  fetchAttachmentBlob: async (
    itemId: string, messageId: string, attachmentId: string,
    filename?: string, mimeType?: string,
  ): Promise<Blob> => {
    const response = await api.get(`/emails/${itemId}/messages/${messageId}/attachments/${attachmentId}`, {
      params: { filename, mimeType },
      responseType: 'blob',
    });
    return response.data as Blob;
  },

  /** Tải toàn bộ attachment của 1 message dưới dạng 1 file .zip. */
  downloadAllAttachments: async (itemId: string, messageId: string): Promise<void> => {
    const response = await api.get(`/emails/${itemId}/messages/${messageId}/attachments/zip`, {
      responseType: 'blob',
    });
    const url = window.URL.createObjectURL(response.data as Blob);
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', 'attachments.zip');
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(url);
  },

  downloadAttachment: async (
    itemId: string, messageId: string, attachmentId: string, filename: string, mimeType?: string,
  ): Promise<void> => {
    const blob = await sendEmailApi.fetchAttachmentBlob(itemId, messageId, attachmentId, filename, mimeType);
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', filename);
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(url);
  },
};
