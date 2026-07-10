import api from './api';

// ── Jira metadata shapes (mirrors backend JiraModels.cs) ─────────────────────

export interface JiraProject {
  id: string;
  key: string;
  name: string;
}

export interface JiraIssueType {
  id: string;
  name: string;
  subtask: boolean;
}

export interface JiraPriority {
  id: string;
  name: string;
}

export interface JiraUser {
  accountId: string;
  displayName: string;
  email: string | null;
  active: boolean;
  /** Avatar 48x48 từ Jira (có thể null nếu user không có ảnh). */
  avatarUrl: string | null;
}

export interface JiraTransition {
  id: string;
  name: string;
  toStatusName: string | null;
}

/** Thông tin Jira site (Atlassian) của 1 connection — hiển thị tên account thay cho cloudId. */
export interface JiraSite {
  name: string;
  url: string;
}

// ── API client ────────────────────────────────────────────────────────────────

export const jiraApi = {
  /**
   * GET /api/jira/projects?connectionId=
   * List Jira projects for a connection (cached 5 min on BE).
   */
  getProjects: async (connectionId: string): Promise<JiraProject[]> => {
    const res = await api.get('/jira/projects', { params: { connectionId } });
    return res.data;
  },

  /**
   * GET /api/jira/issue-types?connectionId=&projectKey=
   * Issue types for a project (cached 5 min on BE).
   */
  getIssueTypes: async (connectionId: string, projectKey: string): Promise<JiraIssueType[]> => {
    const res = await api.get('/jira/issue-types', { params: { connectionId, projectKey } });
    return res.data;
  },

  /**
   * GET /api/jira/priorities?connectionId=
   * Priority list (cached 5 min on BE).
   */
  getPriorities: async (connectionId: string): Promise<JiraPriority[]> => {
    const res = await api.get('/jira/priorities', { params: { connectionId } });
    return res.data;
  },

  /**
   * GET /api/jira/assignable-users?connectionId=&projectKey=&query=
   * Assignable users for a project — NOT cached on BE (state-dependent).
   * Callers should debounce the `query` param.
   */
  getAssignableUsers: async (
    connectionId: string,
    projectKey: string,
    query?: string,
  ): Promise<JiraUser[]> => {
    const res = await api.get('/jira/assignable-users', {
      params: { connectionId, projectKey, query: query || undefined },
    });
    return res.data;
  },

  /**
   * GET /api/jira/transitions?connectionId=&itemId=
   * Available transitions for a specific issue — NOT cached (workflow state).
   */
  getTransitions: async (connectionId: string, itemId: string): Promise<JiraTransition[]> => {
    const res = await api.get('/jira/transitions', { params: { connectionId, itemId } });
    return res.data;
  },

  /**
   * GET /api/jira/site?connectionId=
   * Jira site info (name + url) for a connection — cached 5 min on BE.
   * Returns null when BE responds 204 (site could not be resolved).
   */
  getSite: async (connectionId: string): Promise<JiraSite | null> => {
    const res = await api.get('/jira/site', { params: { connectionId } });
    return res.status === 204 ? null : res.data;
  },
};
