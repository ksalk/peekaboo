import type { Camera, Nvr, RecordingSearchResult, User } from './types';

interface ProblemDetails {
  title?: string;
  detail?: string;
}

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export const UNAUTHORIZED_EVENT = 'peekaboo:unauthorized';

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body !== undefined) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(path, {
    credentials: 'same-origin',
    ...init,
    headers,
  });

  if (response.status === 204) {
    return undefined as T;
  }

  if (response.status === 401 && path !== '/api/auth/login') {
    window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
  }

  if (response.status === 429) {
    throw new ApiError(429, 'Too many attempts. Try again in a few minutes.');
  }

  const contentType = response.headers.get('content-type') ?? '';
  const payload = contentType.includes('json')
    ? ((await response.json()) as ProblemDetails)
    : null;

  if (!response.ok) {
    throw new ApiError(
      response.status,
      payload?.detail ?? payload?.title ?? `Request failed (${response.status})`,
    );
  }

  return payload as T;
}

export function errorMessage(error: unknown): string {
  return error instanceof Error && error.message ? error.message : 'Something went wrong.';
}

export const api = {
  me: () => request<User>('/api/auth/me'),
  login: (username: string, password: string) =>
    request<User>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),
  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>('/api/auth/password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword, newPassword }),
    }),

  listNvrs: () => request<Nvr[]>('/api/nvrs'),
  createNvr: (
    name: string,
    baseUrl: string,
    transport: Nvr['transport'] = 'rtsp',
    sdkPort = 8000,
  ) =>
    request<Nvr>('/api/nvrs', {
      method: 'POST',
      body: JSON.stringify({ name, baseUrl, transport, sdkPort }),
    }),
  updateNvr: (
    id: string,
    name: string,
    baseUrl: string,
    transport?: Nvr['transport'],
    sdkPort?: number,
  ) => {
    const body: Record<string, unknown> = { name, baseUrl };
    if (transport !== undefined) {
      body.transport = transport;
    }
    if (sdkPort !== undefined) {
      body.sdkPort = sdkPort;
    }
    return request<Nvr>(`/api/nvrs/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    });
  },
  deleteNvr: (id: string) =>
    request<void>(`/api/nvrs/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  listCameras: () => request<Camera[]>('/api/cameras'),
  createCamera: (nvrId: string, name: string, mainStreamId: string, subStreamId: string) =>
    request<Camera>(`/api/nvrs/${encodeURIComponent(nvrId)}/cameras`, {
      method: 'POST',
      body: JSON.stringify({ name, mainStreamId, subStreamId }),
    }),
  updateCamera: (
    nvrId: string,
    id: string,
    name: string,
    mainStreamId: string,
    subStreamId: string,
  ) =>
    request<Camera>(`/api/nvrs/${encodeURIComponent(nvrId)}/cameras/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify({ name, mainStreamId, subStreamId }),
    }),
  deleteCamera: (nvrId: string, id: string) =>
    request<void>(
      `/api/nvrs/${encodeURIComponent(nvrId)}/cameras/${encodeURIComponent(id)}`,
      { method: 'DELETE' },
    ),

  listRecordings: (cameraId: string, start: string, end: string, signal?: AbortSignal) =>
    request<RecordingSearchResult>(
      `/api/cameras/${encodeURIComponent(cameraId)}/recordings` +
        `?start=${encodeURIComponent(start)}&end=${encodeURIComponent(end)}`,
      { signal },
    ),

  listUsers: () => request<User[]>('/api/users'),
  createUser: (username: string, password: string, isAdmin: boolean) =>
    request<User>('/api/users', {
      method: 'POST',
      body: JSON.stringify({ username, password, isAdmin }),
    }),
  deleteUser: (id: string) =>
    request<void>(`/api/users/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  resetPassword: (id: string, newPassword: string) =>
    request<void>(`/api/users/${encodeURIComponent(id)}/password`, {
      method: 'PUT',
      body: JSON.stringify({ newPassword }),
    }),
};
