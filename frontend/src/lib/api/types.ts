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

export enum StrategyType {
	CommitteeConsensus = 0,
	MacdRsiMomentum = 1,
	EmaTrendBreakout = 2,
	MeanReversionBollinger = 3
}

export enum RebalanceAction {
	Hold = 0,
	Buy = 1,
	Sell = 2,
	Trim = 3,
	Add = 4
}

export enum TradePlanStatus {
	Proposed = 0,
	Approved = 1,
	Rejected = 2,
	Expired = 3,
	Invalidated = 4
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

export interface AgentDecisionDto {
	id: string;
	deliberationSessionId: string;
	symbol: string;
	role: AgentRole;
	direction: SignalDirection;
	confidenceScore: number;
	reasoning: string;
	keyFactorsJson: string;
	evaluatedAt: string;
}

export interface DeliberationSessionDto {
	id: string;
	symbol: string;
	decisions: AgentDecisionDto[];
	finalConsensusSummary: string;
	finalVerdict: DecisionVerdict;
	overallConfidence: number;
	isRiskApproved: boolean;
	riskNotes?: string | null;
	executedOrderId?: string | null;
	createdAt: string;
}

export interface DebateMessageDto {
	speakerRole: string;
	speakerName: string;
	content: string;
	timestampUtc: string;
}

export interface DeliberationResultDto {
	session: DeliberationSessionDto;
	decisions: AgentDecisionDto[];
	debateLog: DebateMessageDto[];
	recommendedOrder?: CreateOrderRequest | null;
	executedOrder?: Order | null;
}

export interface BacktestRequest {
	symbol: string;
	timeFrame: TimeFrame;
	strategy: StrategyType;
	candleLimit?: number;
	initialBalance?: number;
	slippagePercent?: number;
	commissionPerTrade?: number;
	stopLossPercent?: number;
	takeProfitPercent?: number;
	maxPositionSizePercent?: number;
}

export interface BacktestTrade {
	id: string;
	symbol: string;
	side: OrderSide;
	entryTime: string;
	entryPrice: number;
	exitTime: string;
	exitPrice: number;
	quantity: number;
	pnL: number;
	returnPercent: number;
	exitReason: string;
}

export interface EquityPoint {
	timestamp: string;
	equity: number;
	cash: number;
	drawdownPercent: number;
}

export interface BacktestPerformanceMetrics {
	initialBalance: number;
	finalEquity: number;
	netProfit: number;
	totalReturnPercent: number;
	buyAndHoldReturnPercent: number;
	sharpeRatio: number;
	sortinoRatio: number;
	maxDrawdownPercent: number;
	maxDrawdownDollars: number;
	totalTrades: number;
	winningTrades: number;
	losingTrades: number;
	winRatePercent: number;
	profitFactor: number;
	averageTradeReturnPercent: number;
	averageWinPercent: number;
	averageLossPercent: number;
	largestWinDollars: number;
	largestLossDollars: number;
}

export interface BacktestResult {
	request: BacktestRequest;
	metrics: BacktestPerformanceMetrics;
	trades: BacktestTrade[];
	equityCurve: EquityPoint[];
}

export interface StrategyInfo {
	type: StrategyType;
	name: string;
	description: string;
	defaultTimeFrame: string;
}

export interface RobinhoodAuthRequest {
	username?: string;
	password?: string;
	mfaCode?: string;
	bearerToken?: string;
	rememberMe?: boolean;
	useDemoMode: boolean;
}

export interface RobinhoodOAuthUrlResponse {
	authorizationUrl: string;
	state: string;
	clientId: string;
}

export interface RobinhoodOAuthExchangeRequest {
	code: string;
	state: string;
}

export interface RobinhoodAccountInfo {
	accountNumber: string;
	accountType: string;
	totalEquity: number;
	cashAvailable: number;
	buyingPower: number;
	isConnected: boolean;
	lastSyncedUtc: string;
	statusMessage: string;
	username?: string | null;
	isDemoMode: boolean;
}

export interface SavedRobinhoodSessionDto {
	hasSavedSession: boolean;
	accountNumber?: string | null;
	username?: string | null;
	isDemoMode: boolean;
	lastConnectedAtUtc?: string | null;
}

export interface RobinhoodHoldingItem {
	symbol: string;
	name: string;
	quantity: number;
	averageCostBasis: number;
	currentPrice: number;
	currentMarketValue: number;
	unrealizedPnL: number;
	unrealizedPnLPercent: number;
	portfolioWeightPercent: number;
}

export interface AllocationDeltaItem {
	symbol: string;
	currentQuantity: number;
	currentPrice: number;
	currentValue: number;
	currentWeightPercent: number;
	targetWeightPercent: number;
	weightDeltaPercent: number;
	action: RebalanceAction;
	recommendedQuantity: number;
	estimatedTradeValue: number;
	personaRationale: string;
	committeeSignal: SignalDirection;
}

export interface OptimizationPlan {
	id: string;
	portfolioId: string;
	generatedAtUtc: string;
	nextScheduledRebalanceUtc: string;
	currentTotalEquity: number;
	currentCash: number;
	projectedCash: number;
	estimatedTotalTurnoverPercent: number;
	allocations: AllocationDeltaItem[];
	executiveConsensusRationale: string;
	isRiskApproved: boolean;
	riskAuditorNotes: string;
	executableOrders: CreateOrderRequest[];
	livePolicyVersion: number;
}

export interface OptimizationExecutionResult {
	planId: string;
	ordersExecuted: number;
	totalCapitalRotated: number;
	executedOrders: Order[];
	summary: string;
}

export interface MacroRegimeAssessment {
	regime: string;
	targetEquityPercent: number;
	targetCashPercent: number;
	vix: number;
	tenYearYield: number;
	rationale: string;
	keyRisks: string[];
}

export interface MarketCandidateAssessment {
	symbol: string;
	name: string;
	sector: string;
	lastPrice: number;
	marketCap: number;
	averageDailyVolume: number;
	marketScreenScore: number;
	fundamentalHealthScore: number;
	technicalMomentumScore: number;
	sentimentScore: number;
	compositeConvictionScore: number;
	annualizedVolatilityPercent: number;
	atrStopLossPrice: number;
	direction: SignalDirection;
	isApproved: boolean;
	rationale: string;
	riskFlags: string[];
	hasVerifiedFundamentals: boolean;
	fundamentalDataQuality: string;
	fundamentalSources?: string[] | null;
}

export interface TargetAllocation {
	symbol: string;
	sector: string;
	targetWeightPercent: number;
	targetValue: number;
	currentWeightPercent: number;
	weightDeltaPercent: number;
	estimatedQuantity: number;
	stopLossPrice: number;
}

export interface MarketScanRequest {
	deepAnalysisCount?: number;
	minimumMarketCap?: number;
	minimumSharePrice?: number;
	minimumDailyVolume?: number;
	maxSingleAssetPercent?: number;
	maxSectorPercent?: number;
	maxTurnoverPercent?: number;
	minimumFundamentalHealthScore?: number;
	maxCandidateVolatilityPercent?: number;
	maxProjectedPortfolioVolatilityPercent?: number;
	maxDailyVaR95Percent?: number;
	isMockRun?: boolean;
	mockPortfolioEquity?: number;
}

export interface MarketIntelligenceRun {
	id: string;
	isMockRun: boolean;
	startedAtUtc: string;
	completedAtUtc: string;
	totalSecuritiesScanned: number;
	eligibleSecurities: number;
	macroRegime: MacroRegimeAssessment;
	candidates: MarketCandidateAssessment[];
	targetAllocations: TargetAllocation[];
	targetCashPercent: number;
	estimatedTurnoverPercent: number;
	projectedAnnualizedVolatilityPercent: number;
	parametricDailyVaR95Percent: number;
	isRiskApproved: boolean;
	riskAuditorFeedback: string;
	proposedPaperOrders: CreateOrderRequest[];
	reflectionSummary: string;
	performanceMetrics: {
		observationCount: number;
		annualizedSharpeRatio?: number | null;
		maxDrawdownPercent: number;
		winRatePercent: number;
		cumulativeReturnPercent: number;
	};
	dataSourceSummary: string;
	tradePlanId?: string | null;
	tradePlanHash?: string | null;
	tradePlanStatus?: TradePlanStatus | null;
	tradePlanExpiresAtUtc?: string | null;
}

export interface TradePlanHoldingSnapshot {
	symbol: string;
	quantity: number;
	currentPrice: number;
	currentMarketValue: number;
	portfolioWeightPercent: number;
}

export interface TradePlanOrderSnapshot {
	symbol: string;
	side: OrderSide;
	type: OrderType;
	quantity: number;
	limitPrice?: number | null;
	stopPrice?: number | null;
	estimatedNotional: number;
	isFullLiquidation: boolean;
}

export interface ImmutableTradePlanPayload {
	sourceRunId: string;
	portfolioId: string;
	createdAtUtc: string;
	expiresAtUtc: string;
	policyVersion: number;
	account: {
		accountLastFour: string;
		asOfUtc: string;
		totalEquity: number;
		cashAvailable: number;
		buyingPower: number;
		holdings: TradePlanHoldingSnapshot[];
	};
	macroRegime: MacroRegimeAssessment;
	targetAllocations: TargetAllocation[];
	orders: TradePlanOrderSnapshot[];
	risk: {
		isRiskApproved: boolean;
		feedback: string;
		estimatedTurnoverPercent: number;
		projectedAnnualizedVolatilityPercent: number;
		parametricDailyVaR95Percent: number;
		targetCashPercent: number;
	};
	reflectionSummary: string;
	dataSourceSummary: string;
	candidateProvenance: Record<string, string[]>;
}

export interface TradePlanView {
	id: string;
	sourceRunId: string;
	portfolioId: string;
	status: TradePlanStatus;
	planHash: string;
	createdAtUtc: string;
	expiresAtUtc: string;
	policyVersion: number;
	requiresSecondaryConfirmation: boolean;
	secondaryConfirmationReasons: string[];
	approvedAtUtc?: string | null;
	rejectedAtUtc?: string | null;
	invalidatedAtUtc?: string | null;
	decisionReason?: string | null;
	payload: ImmutableTradePlanPayload;
}

export interface LivePortfolioPolicySnapshot {
	liveTradingEnabled: boolean;
	allowedAssetTypes: AssetType[];
	allowedExchanges: string[];
	allowedOrderTypes: OrderType[];
	regularMarketHoursOnly: boolean;
	fractionalSharesEnabled: boolean;
	minimumCashReservePercent: number;
	maxOrderNotionalPercent: number;
	maxOrderNotionalAmount: number;
	maxDailyTurnoverPercent: number;
	maxDailyLossPercent: number;
	maxPositionPercent: number;
	maxSectorPercent: number;
	maxAnnualizedVolatilityPercent: number;
	maxDailyVaR95Percent: number;
	maxDrawdownPercent: number;
	maxQuoteAgeSeconds: number;
	maxAccountSnapshotAgeSeconds: number;
	approvalExpiryMinutes: number;
	maxPriceDriftPercent: number;
	maxPositionDriftPercent: number;
	orderTimeoutSeconds: number;
	cancelReplaceEnabled: boolean;
	maxCancelReplaceAttempts: number;
	emergencyHaltActive: boolean;
	emergencyHaltReason?: string | null;
	emergencyHaltedAtUtc?: string | null;
	policyVersion: number;
	updatedAtUtc: string;
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
