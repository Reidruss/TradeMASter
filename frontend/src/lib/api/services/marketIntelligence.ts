import { api } from '../client';
import type { MarketIntelligenceRun, MarketScanRequest } from '../types';

export const marketIntelligenceService = {
	runScan: (request: MarketScanRequest = {}) =>
		api.post<MarketIntelligenceRun>('/api/market-intelligence/scan', request),

	getLatest: () =>
		api.get<MarketIntelligenceRun | null>('/api/market-intelligence/latest')
};
