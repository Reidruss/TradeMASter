import { api } from '../client';
import type { PriceTick, Candle, Asset, TimeFrame } from '../types';

export const marketService = {
	getQuote: (symbol: string) =>
		api.get<PriceTick>(`/api/market/quote/${encodeURIComponent(symbol)}`),

	getCandles: (symbol: string, timeframe?: TimeFrame, limit?: number) => {
		const params = new URLSearchParams();
		if (timeframe !== undefined) params.append('timeframe', timeframe.toString());
		if (limit !== undefined) params.append('limit', limit.toString());
		const query = params.toString() ? `?${params.toString()}` : '';
		return api.get<Candle[]>(`/api/market/candles/${encodeURIComponent(symbol)}${query}`);
	},

	getAssets: (searchQuery?: string) => {
		const query = searchQuery ? `?query=${encodeURIComponent(searchQuery)}` : '';
		return api.get<Asset[]>(`/api/market/assets${query}`);
	},

	getWatchlist: () =>
		api.get<PriceTick[]>('/api/market/watchlist')
};
