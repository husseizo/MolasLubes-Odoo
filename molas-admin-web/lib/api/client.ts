import { useAuthStore } from '@/features/auth/auth-store';

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5050';
const API_KEY = process.env.NEXT_PUBLIC_API_KEY ?? '';

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly data?: unknown,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function refreshToken(): Promise<boolean> {
  try {
    const res = await fetch('/api/auth/refresh', { method: 'POST' });
    return res.ok;
  } catch {
    return false;
  }
}

type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  params?: Record<string, string | number | boolean | undefined | null>;
  skipAuth?: boolean;
};

function buildUrl(path: string, params?: Record<string, string | number | boolean | undefined | null>): string {
  const url = new URL(path, API_URL);
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        url.searchParams.set(key, String(value));
      }
    });
  }
  return url.toString();
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, params, skipAuth = false } = options;

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'X-Api-Key': API_KEY,
  };

  if (!skipAuth) {
    const sapUserCode = useAuthStore.getState().sapUserCode;
    if (sapUserCode && method === 'GET' && params) {
      // actorSapUserCode is appended via params by the caller
    }
  }

  const url = buildUrl(path, params);

  let response = await fetch(url, {
    method,
    headers,
    credentials: 'include',
    body: body ? JSON.stringify(body) : undefined,
  });

  if (response.status === 401 && !skipAuth) {
    const refreshed = await refreshToken();
    if (refreshed) {
      response = await fetch(url, {
        method,
        headers,
        credentials: 'include',
        body: body ? JSON.stringify(body) : undefined,
      });
    } else {
      useAuthStore.getState().clear();
      window.location.href = '/login';
      throw new ApiError(401, 'Session expired');
    }
  }

  if (!response.ok) {
    let errorData: unknown;
    try {
      errorData = await response.json();
    } catch {
      errorData = await response.text();
    }
    const message =
      typeof errorData === 'object' && errorData !== null && 'message' in errorData
        ? String((errorData as Record<string, unknown>).message)
        : `Request failed with status ${response.status}`;
    throw new ApiError(response.status, message, errorData);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

export const apiClient = {
  get<T>(path: string, params?: Record<string, string | number | boolean | undefined | null>): Promise<T> {
    return request<T>(path, { method: 'GET', params });
  },
  post<T>(path: string, body?: unknown): Promise<T> {
    return request<T>(path, { method: 'POST', body });
  },
  put<T>(path: string, body?: unknown): Promise<T> {
    return request<T>(path, { method: 'PUT', body });
  },
  patch<T>(path: string, body?: unknown): Promise<T> {
    return request<T>(path, { method: 'PATCH', body });
  },
  delete<T>(path: string): Promise<T> {
    return request<T>(path, { method: 'DELETE' });
  },
};
