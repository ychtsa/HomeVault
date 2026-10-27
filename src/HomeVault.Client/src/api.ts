import type { CatalogItem, ItemDraft, Resident, SignupData } from './types';

export class ApiError extends Error {
  constructor(message: string, public status: number) {
    super(message);
    this.name = 'ApiError';
  }
}

let csrfToken: string | null = null;
export function resetCsrf() { csrfToken = null; }

async function readError(response: Response): Promise<ApiError> {
  let message = response.status === 401
    ? '登入已逾時，請重新登入。'
    : response.status === 429
      ? '操作太頻繁了，請稍候再試。'
      : '暫時無法完成操作，請稍後再試。';
  try {
    const problem = await response.json();
    const validation = Object.values(problem.errors ?? {}).flat().filter((value): value is string => typeof value === 'string');
    message = validation.join(' ') || problem.detail || problem.title || message;
  } catch { /* An upstream proxy may return HTML; keep the useful fallback. */ }
  return new ApiError(message, response.status);
}

async function csrf(): Promise<string> {
  if (csrfToken) return csrfToken;
  const response = await fetch('/api/auth/csrf', { credentials: 'same-origin' });
  if (!response.ok) { if (response.status === 401) resetCsrf(); throw await readError(response); }
  const data = await response.json();
  if (typeof data.token !== 'string') throw new ApiError('無法建立安全連線，請重新整理頁面。', 500);
  csrfToken = data.token;
  return data.token;
}

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const headers = new Headers({ Accept: 'application/json' });
  let requestBody: BodyInit | undefined;
  if (method !== 'GET') headers.set('X-CSRF-TOKEN', await csrf());
  if (body instanceof FormData) requestBody = body;
  else if (body !== undefined) {
    headers.set('Content-Type', 'application/json');
    requestBody = JSON.stringify(body);
  }
  let response: Response;
  try {
    response = await fetch(path, { method, headers, body: requestBody, credentials: 'same-origin' });
  } catch {
    throw new ApiError('無法連線到伺服器，請確認網路後再試。', 0);
  }
  if (!response.ok) { if (response.status === 401) resetCsrf(); throw await readError(response); }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

export const api = {
  me: () => request<Resident>('/api/auth/me'),
  login: async (username: string, password: string) => {
    const resident = await request<Resident>('/api/auth/login', 'POST', { username, password });
    resetCsrf();
    return resident;
  },
  signup: (data: SignupData) => request<Resident>('/api/auth/signup', 'POST', data),
  logout: async () => { await request<void>('/api/auth/logout', 'POST'); resetCsrf(); },
  items: () => request<CatalogItem[]>('/api/items'),
  createItem: (data: ItemDraft) => request<CatalogItem>('/api/items', 'POST', data),
  updateItem: (id: string, data: ItemDraft) => request<CatalogItem>(`/api/items/${encodeURIComponent(id)}`, 'PUT', data),
  deleteItem: (id: string) => request<void>(`/api/items/${encodeURIComponent(id)}`, 'DELETE'),
  uploadImage: (id: string, file: File) => {
    const body = new FormData();
    body.append('file', file);
    return request<CatalogItem>(`/api/items/${encodeURIComponent(id)}/image`, 'POST', body);
  },
  removeImage: (id: string) => request<void>(`/api/items/${encodeURIComponent(id)}/image`, 'DELETE'),
  report: async () => {
    let response: Response;
    try { response = await fetch('/api/items/report', { credentials: 'same-origin', headers: { Accept: 'application/pdf' } }); }
    catch { throw new ApiError('無法連線到伺服器，請確認網路後再試。', 0); }
    if (!response.ok) { if (response.status === 401) resetCsrf(); throw await readError(response); }
    return response.blob();
  },
};
