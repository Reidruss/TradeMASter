import { api } from '../client';
import type { LiveExecutionBatchView, TradePlanView } from '../types';

export const tradePlanService = {
	getLatest: () => api.get<TradePlanView | null>('/api/trade-plans/latest'),

	get: (id: string) => api.get<TradePlanView>(`/api/trade-plans/${id}`),

	approve: (id: string, planHash: string, confirmation: string, secondaryConfirmation?: string) =>
		api.post<TradePlanView>(`/api/trade-plans/${id}/approve`, {
			planHash,
			confirmation,
			secondaryConfirmation: secondaryConfirmation || null
		}),

	reject: (id: string, planHash: string, reason: string) =>
		api.post<TradePlanView>(`/api/trade-plans/${id}/reject`, { planHash, reason }),

	getExecution: (id: string) =>
		api.get<LiveExecutionBatchView | null>(`/api/trade-plans/${id}/execution`),

	execute: (id: string, planHash: string, confirmation: string) =>
		api.post<LiveExecutionBatchView>(`/api/trade-plans/${id}/execute`, {
			planHash,
			confirmation
		})
};
