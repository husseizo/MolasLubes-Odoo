import { headers } from 'next/headers';

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5050';
const API_KEY = process.env.NEXT_PUBLIC_API_KEY ?? '';

export class ServerApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly data?: unknown,
  ) {
    super(message);
    this.name = 'ServerApiError';
  }
}

async function serverRequest<T>(
  path: string,
  options: RequestInit & { params?: Record<string, string | number | boolean | undefined | null> } = {},
): Promise<T> {
  const { params, ...fetchOptions } = options;

  const url = new URL(path, API_URL);
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        url.searchParams.set(key, String(value));
      }
    });
  }

  const incomingHeaders = headers();
  const token = incomingHeaders.get('x-molas-token');

  const reqHeaders: Record<string, string> = {
    'Content-Type': 'application/json',
    'X-Api-Key': API_KEY,
  };
  if (token) {
    reqHeaders['Authorization'] = `Bearer ${token}`;
  }

  const response = await fetch(url.toString(), {
    ...fetchOptions,
    headers: {
      ...reqHeaders,
      ...(fetchOptions.headers as Record<string, string> | undefined),
    },
    next: { revalidate: 0 },
  });

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
        : `Server request failed with status ${response.status}`;
    throw new ServerApiError(response.status, message, errorData);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

export const serverApiClient = {
  get<T>(
    path: string,
    params?: Record<string, string | number | boolean | undefined | null>,
  ): Promise<T> {
    return serverRequest<T>(path, { method: 'GET', params });
  },
  post<T>(path: string, body?: unknown): Promise<T> {
    return serverRequest<T>(path, {
      method: 'POST',
      body: body ? JSON.stringify(body) : undefined,
    });
  },
};
