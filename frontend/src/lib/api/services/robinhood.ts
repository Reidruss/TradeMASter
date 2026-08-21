import { api } from '../client';
import type {
	RobinhoodAccountInfo,
	RobinhoodAuthRequest,
	RobinhoodHoldingItem,
	SavedRobinhoodSessionDto,
	RobinhoodOAuthUrlResponse,
	RobinhoodOAuthExchangeRequest,
	Portfolio
} from '../types';

export const robinhoodService = {
	getOAuthUrl: (redirectUri?: string) =>
		api.get<RobinhoodOAuthUrlResponse>(`/api/robinhood/oauth/url${redirectUri ? `?redirectUri=${encodeURIComponent(redirectUri)}` : ''}`),

	exchangeOAuthCode: (request: RobinhoodOAuthExchangeRequest) =>
		api.post<RobinhoodAccountInfo>('/api/robinhood/oauth/exchange', request),

	connect: (request: RobinhoodAuthRequest) =>
		api.post<RobinhoodAccountInfo>('/api/robinhood/connect', request),

	disconnect: () =>
		api.post<{ message: string }>('/api/robinhood/disconnect', {}),

	getSavedSession: () =>
		api.get<SavedRobinhoodSessionDto>('/api/robinhood/session'),

	getStatus: () =>
		api.get<RobinhoodAccountInfo>('/api/robinhood/status'),

	getHoldings: () =>
		api.get<RobinhoodHoldingItem[]>('/api/robinhood/holdings'),

	setCustomHoldings: (customHoldings: RobinhoodHoldingItem[]) =>
		api.post<RobinhoodHoldingItem[]>('/api/robinhood/holdings/custom', customHoldings),

	syncPortfolio: () =>
		api.post<Portfolio>('/api/robinhood/sync', {})
};
