import { api } from '../client';
import type { HealthInfo } from '../types';

export const healthService = {
	async getHealth(): Promise<HealthInfo> {
		return api.get<HealthInfo>('/api/health');
	}
};
