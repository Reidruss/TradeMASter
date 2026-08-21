import { api } from '../client';
import type { LivePortfolioPolicySnapshot } from '../types';

export const livePolicyService = {
	get: () => api.get<LivePortfolioPolicySnapshot>('/api/live-policy/'),

	activateEmergencyHalt: (reason: string) =>
		api.post<LivePortfolioPolicySnapshot>('/api/live-policy/emergency-halt', { reason }),

	clearEmergencyHalt: (confirmation: string) =>
		api.post<LivePortfolioPolicySnapshot>('/api/live-policy/resume', { confirmation })
};
