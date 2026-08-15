/**
 * TypeScript types matching ASP.NET Core backend DTOs and models
 */

export interface WeatherForecast {
	date: string; // ISO DateOnly string (YYYY-MM-DD)
	temperatureC: number;
	temperatureF: number;
	summary: string | null;
}

export interface TodoItem {
	id: string; // GUID
	title: string;
	description: string | null;
	isCompleted: boolean;
	createdAt: string; // ISO Date string
	updatedAt: string | null;
}

export interface CreateTodoRequest {
	title: string;
	description?: string | null;
}

export interface UpdateTodoRequest {
	title: string;
	description?: string | null;
	isCompleted: boolean;
}

export interface HealthInfo {
	status: 'Healthy' | 'Degraded' | 'Unhealthy' | string;
	frameworkVersion: string;
	serverTimeUtc: string;
	uptime: string;
	environment: string;
}

export interface ApiErrorResponse {
	error?: string;
	message?: string;
	errors?: Record<string, string[]>;
	status?: number;
}
