import { api } from '../client';
import type { Portfolio, Position, RiskParameters, UpdateRiskParametersRequest } from '../types';

export const portfolioService = {
	getPortfolio: () =>
		api.get<Portfolio>('/api/portfolio'),

	getPositions: () =>
		api.get<Position[]>('/api/portfolio/positions'),

	getRisk: () =>
		api.get<RiskParameters>('/api/portfolio/risk'),

	updateRisk: (request: UpdateRiskParametersRequest) =>
		api.put<RiskParameters>('/api/portfolio/risk', request)
};
