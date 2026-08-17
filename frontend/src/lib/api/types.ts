/**
 * TypeScript types matching ASP.NET Core backend DTOs and models
 */

export enum AssetType {
	Stock = 0,
	Crypto = 1,
	Forex = 2,
	Commodity = 3,
	Index = 4,
	Etf = 5
}

export enum OrderType {
	Market = 0,
	Limit = 1,
	Stop = 2,
	StopLimit = 3
}

export enum OrderSide {
	Buy = 0,
	Sell = 1
}

export enum OrderStatus {
	Pending = 0,
	Open = 1,
	PartiallyFilled = 2,
	Filled = 3,
	Canceled = 4,
	Rejected = 5,
	Expired = 6
}

export enum TimeFrame {
	OneMinute = 0,
	FiveMinutes = 1,
	FifteenMinutes = 2,
	OneHour = 3,
	FourHours = 4,
	OneDay = 5,
	OneWeek = 6
}

export enum AgentRole {
	TechnicalAnalyst = 0,
	FundamentalAnalyst = 1,
	SentimentAnalyst = 2,
	RiskAuditor = 3,
	PortfolioArbiter = 4,
	ExecutionStrategist = 5
}

export enum SignalDirection {
	Neutral = 0,
	Bullish = 1,
	Bearish = 2,
	StrongBuy = 3,
	StrongSell = 4
}

export enum DecisionVerdict {
	Hold = 0,
	Buy = 1,
	Sell = 2,
	Vetoed = 3
}

export interface PriceTick {
	symbol: string;
	price: number;
	volume: number;
	bid?: number;
	ask?: number;
	change24h: number;
	changePercent24h: number;
	timestamp: string;
}

export interface Candle {
	symbol: string;
	timeFrame: TimeFrame;
	open: number;
	high: number;
	low: number;
	close: number;
	volume: number;
	timestamp: string;
	isBullish?: boolean;
	bodySize?: number;
	range?: number;
}

export interface Asset {
	id: string;
	symbol: string;
	name: string;
	type: AssetType;
	exchange: string;
	currency: string;
	isTradable: boolean;
	lastPrice: number;
	previousClose: number;
	change24h: number;
	changePercent24h: number;
	volume24h: number;
	lastPriceUpdatedUtc?: string;
}

export interface Position {
	id: string;
	portfolioId: string;
	symbol: string;
	quantity: number;
	averageEntryPrice: number;
	currentPrice: number;
	unrealizedPnL: number;
	unrealizedPnLPercent: number;
	realizedPnL: number;
	totalCostBasis: number;
	currentMarketValue: number;
	createdAt: string;
	updatedAt?: string;
}

export interface RiskParameters {
	maxPositionSizePercent: number;
	maxPortfolioDrawdownPercent: number;
	defaultStopLossPercent: number;
	defaultTakeProfitPercent: number;
	requireHumanApprovalForLive: boolean;
	maxDailyLossAmount: number;
}

export interface Portfolio {
	id: string;
	name: string;
	cashBalance: number;
	initialBalance: number;
	riskConfig: RiskParameters;
	positions: Position[];
	orders: Order[];
	totalPositionValue: number;
	totalEquity: number;
	totalUnrealizedPnL: number;
	totalRealizedPnL: number;
	totalPnL: number;
	totalPnLPercent: number;
	createdAt: string;
	updatedAt?: string;
}

export interface Order {
	id: string;
	portfolioId: string;
	symbol: string;
	side: OrderSide;
	type: OrderType;
	quantity: number;
	limitPrice?: number | null;
	stopPrice?: number | null;
	filledPrice?: number | null;
	filledQuantity: number;
	status: OrderStatus;
	submittedAt: string;
	filledAt?: string | null;
	rejectionReason?: string | null;
	deliberationSessionId?: string | null;
}

export interface CreateOrderRequest {
	symbol: string;
	side: OrderSide;
	type: OrderType;
	quantity: number;
	limitPrice?: number | null;
	stopPrice?: number | null;
}

export interface UpdateRiskParametersRequest {
	maxPositionSizePercent: number;
	maxPortfolioDrawdownPercent: number;
	defaultStopLossPercent: number;
	defaultTakeProfitPercent: number;
	requireHumanApprovalForLive: boolean;
	maxDailyLossAmount: number;
}

export interface ComponentStatus {
	status: 'Healthy' | 'Degraded' | 'Warning' | string;
	details: string;
}

export interface HealthInfo {
	status: 'Healthy' | 'Degraded' | 'Unhealthy' | string;
	frameworkVersion: string;
	serverTimeUtc: string;
	uptime: string;
	environment: string;
	components?: Record<string, ComponentStatus>;
}

export interface WeatherForecast {
	date: string;
	temperatureC: number;
	temperatureF: number;
	summary: string | null;
}

export interface TodoItem {
	id: string;
	title: string;
	description: string | null;
	isCompleted: boolean;
	createdAt: string;
	updatedAt: string | null;
}

export interface CreateTodoRequest {
	title: string;
	description?: string | null;
}

export interface UpdateTodoRequest {
	title: string;
	description?: string | null;
	isCompleted: boolean;
}

export interface ApiErrorResponse {
	error?: string;
	message?: string;
	errors?: Record<string, string[]>;
	status?: number;
}
