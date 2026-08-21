import { api } from '../client';
import type { BacktestRequest, BacktestResult, StrategyInfo } from '../types';

export const backtestService = {
	runBacktest: (request: BacktestRequest) =>
		api.post<BacktestResult>('/api/backtest/run', request),

	getStrategies: () =>
		api.get<StrategyInfo[]>('/api/backtest/strategies')
};
