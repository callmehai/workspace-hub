import api from './api';

export interface IntegrationCatalogDto {
  id: string;
  key: string;
  displayName: string;
  isEnabled: boolean;
}

export const integrationsApi = {
  getCatalog: async (): Promise<IntegrationCatalogDto[]> => {
    const response = await api.get<IntegrationCatalogDto[]>('/integrations');
    return response.data;
  },
};
