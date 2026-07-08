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
};
