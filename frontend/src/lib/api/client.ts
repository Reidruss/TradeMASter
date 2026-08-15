import type { ApiErrorResponse } from './types';

export class ApiError extends Error {
	public status: number;
	public data: ApiErrorResponse;

	constructor(status: number, message: string, data: ApiErrorResponse = {}) {
		super(message);
		this.name = 'ApiError';
		this.status = status;
		this.data = data;
	}
}

export interface RequestOptions extends RequestInit {
	params?: Record<string, string | number | boolean | undefined | null>;
}

class ApiClient {
	private defaultHeaders: HeadersInit = {
		'Content-Type': 'application/json',
		Accept: 'application/json'
	};

	private buildUrl(endpoint: string, params?: Record<string, string | number | boolean | undefined | null>): string {
		const base = endpoint.startsWith('/') ? endpoint : `/${endpoint}`;
		if (!params) return base;

		const query = new URLSearchParams();
		Object.entries(params).forEach(([key, val]) => {
			if (val !== undefined && val !== null) {
				query.append(key, String(val));
			}
		});

		const queryString = query.toString();
		return queryString ? `${base}?${queryString}` : base;
	}

	async request<T>(endpoint: string, options: RequestOptions = {}): Promise<T> {
		const { params, headers, ...customConfig } = options;
		const url = this.buildUrl(endpoint, params);

		const config: RequestInit = {
			method: 'GET',
			headers: {
				...this.defaultHeaders,
				...headers
			},
			...customConfig
		};

		let response: Response;
		try {
			response = await fetch(url, config);
		} catch (networkError: any) {
			throw new ApiError(0, `Network connection failed: ${networkError.message || 'Cannot reach server'}`);
		}

		if (response.status === 204) {
			return undefined as unknown as T;
		}

		let data: any;
		const contentType = response.headers.get('content-type');
		if (contentType && contentType.includes('application/json')) {
			try {
				data = await response.json();
			} catch {
				data = null;
			}
		} else {
			data = await response.text();
		}

		if (!response.ok) {
			const errorMessage =
				data?.message || data?.error || `Request failed with status ${response.status} (${response.statusText})`;
			throw new ApiError(response.status, errorMessage, data);
		}

		return data as T;
	}

	get<T>(endpoint: string, options?: RequestOptions): Promise<T> {
		return this.request<T>(endpoint, { ...options, method: 'GET' });
	}

	post<T>(endpoint: string, body?: unknown, options?: RequestOptions): Promise<T> {
		return this.request<T>(endpoint, {
			...options,
			method: 'POST',
			body: body ? JSON.stringify(body) : undefined
		});
	}

	put<T>(endpoint: string, body?: unknown, options?: RequestOptions): Promise<T> {
		return this.request<T>(endpoint, {
			...options,
			method: 'PUT',
			body: body ? JSON.stringify(body) : undefined
		});
	}

	patch<T>(endpoint: string, body?: unknown, options?: RequestOptions): Promise<T> {
		return this.request<T>(endpoint, {
			...options,
			method: 'PATCH',
			body: body ? JSON.stringify(body) : undefined
		});
	}

	delete<T = void>(endpoint: string, options?: RequestOptions): Promise<T> {
		return this.request<T>(endpoint, { ...options, method: 'DELETE' });
	}
}

export const api = new ApiClient();
