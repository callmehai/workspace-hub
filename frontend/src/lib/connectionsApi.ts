import api from './api';

export interface ConnectionDto {
  id: string;
  provider: string;
  serviceType: string;
  providerAccountId: string;
  maskedToken: string;
  status: string;
  expiresAt: string;
  lastSyncedAt?: string;
  createdAt: string;
}

export interface OAuthStartRequest {
  integrationKey: string;
  serviceType: string;
  redirectUri: string;
}

export interface OAuthStartResponse {
  authorizationUrl: string;
  state: string;
}

export interface OAuthCallbackRequest {
  code: string;
  state: string;
}

export interface OAuthCallbackResponse {
  integrationKey: string;
  providerAccountId: string;
  connections: { id: string; serviceType: string; status: string }[];
}

export const connectionsApi = {
  getConnections: async (): Promise<ConnectionDto[]> => {
    const response = await api.get('/connections');
    return response.data;
  },

  startOAuth: async (data: OAuthStartRequest): Promise<OAuthStartResponse> => {
    const response = await api.post('/connections/oauth/start', data);
    return response.data;
  },

  oauthCallback: async (data: OAuthCallbackRequest): Promise<OAuthCallbackResponse> => {
    const response = await api.post('/connections/oauth/callback', data);
    return response.data;
  },

  disconnect: async (id: string): Promise<void> => {
    await api.delete(`/connections/${id}`);
  }
};
