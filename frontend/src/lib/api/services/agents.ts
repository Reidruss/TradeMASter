import { api } from '../client';
import type { DeliberationResultDto, DeliberationSessionDto } from '../types';

export const agentService = {
	deliberate: (symbol: string, autoExecute: boolean = false) =>
		api.post<DeliberationResultDto>('/api/agents/deliberate', { symbol, autoExecute }),

	getHistory: () =>
		api.get<DeliberationSessionDto[]>('/api/agents/history'),

	getSession: (sessionId: string) =>
		api.get<DeliberationSessionDto>(`/api/agents/session/${sessionId}`)
};
