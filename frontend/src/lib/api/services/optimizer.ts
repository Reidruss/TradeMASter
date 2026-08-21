import { api } from '../client';
import type {
	OptimizationPlan,
	OptimizationExecutionResult
} from '../types';

export interface RebalanceScheduleInfo {
	nextScheduledRebalanceUtc: string;
	intervalDays: number;
	frequency: string;
}

export const optimizerService = {
	runOptimization: (portfolioId?: string) =>
		api.post<OptimizationPlan>('/api/optimizer/run', { portfolioId }),

	executePlan: (plan: OptimizationPlan) =>
		api.post<OptimizationExecutionResult>('/api/optimizer/execute', plan),

	getSchedule: () =>
		api.get<RebalanceScheduleInfo>('/api/optimizer/schedule')
};
