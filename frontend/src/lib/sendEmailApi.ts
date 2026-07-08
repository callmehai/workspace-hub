import api from './api';

export interface SendEmailRequest {
  connectionId: string;
  to: string[];
  cc: string[];
  bcc: string[];
  subject: string;
  bodyHtml: string;
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
}

export interface ForwardEmailRequest {
  connectionId: string;
  itemId: string;
  to: string[];
  cc?: string[];
  bcc?: string[];
  bodyHtml: string;
  includeAttachments: boolean;
}

export interface SendInThreadResult {
  messageId: string;
  threadId: string;
  sentAt: string;
}

export interface ContactSuggestion {
  email: string;
  displayName?: string | null;
  source?: string;
}

interface ODataContactSuggestResponse {
  value?: ContactSuggestion[];
}

/** Escape single quote cho OData string literal. */
function odataEscape(value: string): string {
  return value.replace(/'/g, "''");
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

  /** Gợi ý contact — OData $filter/$top/$orderby (in-memory, giống scheduled-emails). */
  suggestContacts: async (connectionId: string, q: string, limit = 10): Promise<ContactSuggestion[]> => {
    if (q.trim().length < 2) return [];
    try {
      const term = odataEscape(q.trim());
      const filter = `contains(Email,'${term}') or contains(DisplayName,'${term}')`;
      const url =
        `/EmailContactSuggestions?connectionId=${connectionId}` +
        `&$filter=${encodeURIComponent(filter)}` +
        `&$top=${limit}` +
        `&$orderby=${encodeURIComponent('DisplayName')}`;
      const response = await api.get<ODataContactSuggestResponse | ContactSuggestion[]>(url);
      const data = response.data;
      if (data && typeof data === 'object' && Array.isArray((data as ODataContactSuggestResponse).value)) {
        return (data as ODataContactSuggestResponse).value!;
      }
      if (Array.isArray(data)) return data;
      return [];
    } catch {
      return [];
    }
  },

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

  downloadAttachment: async (itemId: string, attachmentId: string, filename: string): Promise<void> => {
    const response = await api.get(`/emails/${itemId}/attachments/${attachmentId}`, {
      responseType: 'blob',
    });
    const url = window.URL.createObjectURL(new Blob([response.data]));
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', filename);
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(url);
  },
};
