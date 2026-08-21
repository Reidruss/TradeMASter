<script lang="ts">
	import { onMount } from 'svelte';
	import {
		robinhoodService,
		optimizerService,
		marketIntelligenceService,
		livePolicyService,
		tradePlanService,
		type RobinhoodHoldingItem,
		type MarketIntelligenceRun,
		type LivePortfolioPolicySnapshot,
		type TradePlanView,
		type LiveExecutionBatchView,
		TradePlanStatus,
		LiveExecutionBatchStatus,
		LiveExecutionAttemptStatus,
		OrderSide,
		OrderType
	} from '$lib/api';
	import { robinhoodAccount } from '$lib/stores/robinhood';

	// Account & Holdings
	let rhAccount = $derived($robinhoodAccount);
	let holdings = $state<RobinhoodHoldingItem[]>([]);
	let isSyncing = $state(false);

	// Cadence & Optimization
	let scheduleInfo = $state<{ nextScheduledRebalanceUtc: string; frequency: string } | null>(null);
	let countdownText = $state('Calculating...');
	let marketRun = $state<MarketIntelligenceRun | null>(null);
	let isOptimizing = $state(false);
	let activeRunMode = $state<'live' | 'mock' | null>(null);
	let optimizationStep = $state('');
	let marketScanError = $state<string | null>(null);
	let livePolicy = $state<LivePortfolioPolicySnapshot | null>(null);
	let policyActionPending = $state(false);
	let policyActionError = $state<string | null>(null);
	let haltReason = $state('Operator activated emergency halt from dashboard.');
	let resumeConfirmation = $state('');
	let tradePlan = $state<TradePlanView | null>(null);
	let planActionPending = $state(false);
	let planActionError = $state<string | null>(null);
	let approvalConfirmation = $state('');
	let secondaryApprovalConfirmation = $state('');
	let rejectionReason = $state('');
	let hashCopied = $state(false);
	let executionBatch = $state<LiveExecutionBatchView | null>(null);
	let executionConfirmation = $state('');
	let executionPending = $state(false);
	let executionError = $state<string | null>(null);

	async function loadAllData() {
		isSyncing = true;
		try {
			const acc = await robinhoodService.getStatus();
			const [hList, sched, policy, latestPlan] = await Promise.all([
				acc.isConnected ? robinhoodService.getHoldings() : Promise.resolve([]),
				optimizerService.getSchedule(),
				livePolicyService.get(),
				tradePlanService.getLatest()
			]);
			robinhoodAccount.set(acc);
			holdings = hList;
			scheduleInfo = sched;
			livePolicy = policy;
			tradePlan = latestPlan;
			executionBatch = latestPlan ? await tradePlanService.getExecution(latestPlan.id) : null;
			updateCountdown();
		} catch (err) {
			console.error('Failed to load portfolio state', err);
		} finally {
			isSyncing = false;
		}
	}

	function planStatusLabel(status: TradePlanStatus) {
		return TradePlanStatus[status]?.toUpperCase() ?? 'UNKNOWN';
	}

	function planStatusClass(status: TradePlanStatus) {
		if (status === TradePlanStatus.Approved) return 'badge-success';
		if (status === TradePlanStatus.Proposed) return 'badge-primary';
		return 'badge-danger';
	}

	function orderSideLabel(side: OrderSide) {
		return side === OrderSide.Buy ? 'BUY' : 'SELL';
	}

	function orderTypeLabel(type: OrderType) {
		return OrderType[type]?.toUpperCase() ?? 'UNKNOWN';
	}

	function executionStatusLabel(status: LiveExecutionBatchStatus) {
		return LiveExecutionBatchStatus[status]?.replace(/([a-z])([A-Z])/g, '$1 $2').toUpperCase() ?? 'UNKNOWN';
	}

	function attemptStatusLabel(status: LiveExecutionAttemptStatus) {
		return LiveExecutionAttemptStatus[status]?.replace(/([a-z])([A-Z])/g, '$1 $2').toUpperCase() ?? 'UNKNOWN';
	}

	function executionStatusClass(status: LiveExecutionBatchStatus) {
		if (status === LiveExecutionBatchStatus.Submitted) return 'badge-success';
		if (status === LiveExecutionBatchStatus.PreflightPassed || status === LiveExecutionBatchStatus.Submitting) return 'badge-primary';
		return 'badge-danger';
	}

	function attemptStatusClass(status: LiveExecutionAttemptStatus) {
		if (status === LiveExecutionAttemptStatus.BrokerAccepted) return 'badge-success';
		if (status === LiveExecutionAttemptStatus.Pending || status === LiveExecutionAttemptStatus.Submitting) return 'badge-primary';
		return 'badge-danger';
	}

	async function copyPlanHash() {
		if (!tradePlan) return;
		await navigator.clipboard.writeText(tradePlan.planHash);
		hashCopied = true;
		setTimeout(() => (hashCopied = false), 1500);
	}

	async function approveTradePlan() {
		if (!tradePlan) return;
		planActionPending = true;
		planActionError = null;
		try {
			tradePlan = await tradePlanService.approve(
				tradePlan.id,
				tradePlan.planHash,
				approvalConfirmation,
				secondaryApprovalConfirmation
			);
			approvalConfirmation = '';
			secondaryApprovalConfirmation = '';
			executionBatch = await tradePlanService.getExecution(tradePlan.id);
		} catch (err) {
			planActionError = err instanceof Error ? err.message : 'The exact plan could not be approved.';
			try { tradePlan = await tradePlanService.get(tradePlan.id); } catch { /* preserve the visible snapshot */ }
		} finally {
			planActionPending = false;
		}
	}

	async function executeApprovedPlan() {
		if (!tradePlan) return;
		executionPending = true;
		executionError = null;
		try {
			executionBatch = await tradePlanService.execute(
				tradePlan.id,
				tradePlan.planHash,
				executionConfirmation
			);
			executionConfirmation = '';
		} catch (err) {
			executionError = err instanceof Error ? err.message : 'Fresh broker preflight could not be completed.';
			try {
				tradePlan = await tradePlanService.get(tradePlan.id);
				executionBatch = await tradePlanService.getExecution(tradePlan.id);
			} catch { /* preserve the visible review state */ }
		} finally {
			executionPending = false;
		}
	}

	async function rejectTradePlan() {
		if (!tradePlan) return;
		planActionPending = true;
		planActionError = null;
		try {
			tradePlan = await tradePlanService.reject(tradePlan.id, tradePlan.planHash, rejectionReason.trim());
			rejectionReason = '';
		} catch (err) {
			planActionError = err instanceof Error ? err.message : 'The exact plan could not be rejected.';
		} finally {
			planActionPending = false;
		}
	}

	async function activateEmergencyHalt() {
		if (haltReason.trim().length < 5) return;
		policyActionPending = true;
		policyActionError = null;
		try {
			livePolicy = await livePolicyService.activateEmergencyHalt(haltReason.trim());
		} catch (err) {
			policyActionError = err instanceof Error ? err.message : 'Emergency halt could not be activated.';
		} finally {
			policyActionPending = false;
		}
	}

	async function clearEmergencyHalt() {
		policyActionPending = true;
		policyActionError = null;
		try {
			livePolicy = await livePolicyService.clearEmergencyHalt(resumeConfirmation);
			resumeConfirmation = '';
		} catch (err) {
			policyActionError = err instanceof Error ? err.message : 'Emergency halt could not be cleared.';
		} finally {
			policyActionPending = false;
		}
	}

	function updateCountdown() {
		if (!scheduleInfo?.nextScheduledRebalanceUtc) {
			countdownText = 'Bi-Weekly (14 Days)';
			return;
		}
		const target = new Date(scheduleInfo.nextScheduledRebalanceUtc).getTime();
		const now = new Date().getTime();
		const diffMs = Math.max(0, target - now);

		const days = Math.floor(diffMs / (1000 * 60 * 60 * 24));
		const hours = Math.floor((diffMs % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
		const mins = Math.floor((diffMs % (1000 * 60 * 60)) / (1000 * 60));

		countdownText = `${days}d ${hours}h ${mins}m`;
	}

	async function handleRunOptimization(isMockRun = false) {
		if (!isMockRun && !rhAccount?.isConnected) return;
		isOptimizing = true;
		activeRunMode = isMockRun ? 'mock' : 'live';
		marketScanError = null;
		optimizationStep = `1. Scanning the full U.S. listed-stock universe${isMockRun ? ' for a mock run' : ''}...`;

		setTimeout(() => {
			if (isOptimizing) optimizationStep = '2. Classifying the macro regime and screening liquidity...';
		}, 400);
		setTimeout(() => {
			if (isOptimizing) optimizationStep = '3. Deep-researching fundamentals, technicals, and sentiment...';
		}, 900);
		setTimeout(() => {
			if (isOptimizing) optimizationStep = '4. Optimizing risk-weighted allocations and running compliance checks...';
		}, 1400);

		try {
			marketRun = await marketIntelligenceService.runScan({
				deepAnalysisCount: isMockRun ? 5 : 8,
				isMockRun,
				mockPortfolioEquity: 10_000
			});
			if (!isMockRun && marketRun.tradePlanId) {
				tradePlan = await tradePlanService.get(marketRun.tradePlanId);
				executionBatch = await tradePlanService.getExecution(marketRun.tradePlanId);
				approvalConfirmation = '';
				secondaryApprovalConfirmation = '';
				rejectionReason = '';
				planActionError = null;
			}
		} catch (err) {
			console.error('Market intelligence scan error', err);
			marketScanError = err instanceof Error ? err.message : 'The market scan could not be completed.';
		} finally {
			isOptimizing = false;
			activeRunMode = null;
			optimizationStep = '';
		}
	}

	onMount(() => {
		loadAllData();

		const timer = setInterval(updateCountdown, 60000);

		return () => {
			clearInterval(timer);
		};
	});
	let totalUnrealizedPnL = $derived(holdings.reduce((sum, h) => sum + h.unrealizedPnL, 0));
</script>

<svelte:head>
	<title>TradeMASter • Robinhood Autonomous Agentic Portfolio</title>
</svelte:head>

<div class="command-center-page">
	<!-- Top App Header -->
	<header class="app-header card">
		<div class="header-main">
			<div class="header-badge-row">
				{#if rhAccount?.isConnected}
					<span class="rh-live-pill">
						<span class="dot dot-online"></span>
						<span>ROBINHOOD MCP CONNECTED</span>
					</span>
					<span class="account-id font-mono">{rhAccount.accountNumber}</span>
					<span class="account-type-pill">{rhAccount.accountType}</span>
				{:else}
					<span class="rh-disconnected-pill">
						<span class="dot dot-offline"></span>
						<span>ROBINHOOD DISCONNECTED</span>
					</span>
					<span class="account-id font-mono text-muted">No Account Linked</span>
				{/if}
			</div>
			<h1>Autonomous Portfolio Command Center</h1>
			<p>Your funded Robinhood Agentic account is read through MCP. Nine specialist roles scan the market, optimize allocation, and persist exact human-reviewed trade plans.</p>
		</div>

		<div class="header-actions">
			<button type="button" class="btn btn-secondary" onclick={loadAllData} disabled={isSyncing}>
				{#if isSyncing}
					<span class="spinner"></span>
					<span>Syncing...</span>
				{:else}
					<span>🔄 Sync MCP</span>
				{/if}
			</button>
			<button type="button" class="btn btn-primary" onclick={() => handleRunOptimization(false)} disabled={isOptimizing || !rhAccount?.isConnected}>
				{#if isOptimizing && activeRunMode === 'live'}
					<span class="spinner"></span>
					<span>Running Live Analysis...</span>
				{:else}
					<span>⚡ Run Live Analysis</span>
				{/if}
			</button>
			<button type="button" class="btn btn-secondary" onclick={() => handleRunOptimization(true)} disabled={isOptimizing}>
				{#if isOptimizing && activeRunMode === 'mock'}
					<span class="spinner"></span>
					<span>Running Mock...</span>
				{:else}
					<span>🧪 Run Mock Analysis</span>
				{/if}
			</button>
		</div>
	</header>

	<section class="card live-policy-card" class:halted={livePolicy?.emergencyHaltActive}>
		<div class="policy-heading">
			<div>
				<span class="stat-lbl">REAL-MONEY AUTHORITY BOUNDARY</span>
				<h2>Supervised Live Policy {livePolicy ? `v${livePolicy.policyVersion}` : ''}</h2>
			</div>
			<span class="badge {livePolicy?.emergencyHaltActive ? 'badge-danger' : 'badge-success'}">
				{livePolicy?.emergencyHaltActive ? 'EMERGENCY HALT ACTIVE' : 'POLICY ACTIVE'}
			</span>
		</div>
		{#if livePolicy}
			<div class="policy-metrics">
				<span><strong>Live submission</strong><small>{livePolicy.liveTradingEnabled ? 'Enabled' : 'Disabled and locked'}</small></span>
				<span><strong>Allowed scope</strong><small>Stocks/ETFs · limit orders · regular hours</small></span>
				<span><strong>Cash reserve</strong><small>{livePolicy.minimumCashReservePercent.toFixed(1)}% minimum</small></span>
				<span><strong>Order ceiling</strong><small>{livePolicy.maxOrderNotionalPercent.toFixed(1)}% or ${livePolicy.maxOrderNotionalAmount.toFixed(0)}, whichever is lower</small></span>
				<span><strong>Daily turnover</strong><small>{livePolicy.maxDailyTurnoverPercent.toFixed(1)}% maximum</small></span>
				<span><strong>Data freshness</strong><small>{livePolicy.maxQuoteAgeSeconds}s quote · {livePolicy.maxAccountSnapshotAgeSeconds}s account</small></span>
			</div>
			{#if livePolicy.emergencyHaltActive}
				<p class="halt-reason"><strong>Reason:</strong> {livePolicy.emergencyHaltReason}</p>
				<div class="policy-action-row">
					<input aria-label="Resume confirmation" bind:value={resumeConfirmation} placeholder="Type RESUME SUPERVISED OPERATIONS" />
					<button class="btn btn-secondary" type="button" onclick={clearEmergencyHalt} disabled={policyActionPending || resumeConfirmation !== 'RESUME SUPERVISED OPERATIONS'}>Clear Halt</button>
				</div>
			{:else}
				<div class="policy-action-row">
					<input aria-label="Emergency halt reason" bind:value={haltReason} maxlength="500" />
					<button class="btn btn-danger" type="button" onclick={activateEmergencyHalt} disabled={policyActionPending || haltReason.trim().length < 5}>Activate Emergency Halt</button>
				</div>
			{/if}
			{#if policyActionError}<p class="policy-error">{policyActionError}</p>{/if}
		{:else}
			<p>Loading the persisted safety boundary…</p>
		{/if}
	</section>

	<section class="card trade-plan-card" class:plan-approved={tradePlan?.status === TradePlanStatus.Approved}>
		<div class="policy-heading">
			<div>
				<span class="stat-lbl">IMMUTABLE HUMAN REVIEW GATE</span>
				<h2>Exact Trade Plan Review</h2>
			</div>
			{#if tradePlan}
				<span class="badge {planStatusClass(tradePlan.status)}">{planStatusLabel(tradePlan.status)}</span>
			{:else}
				<span class="badge">NO LIVE PLAN</span>
			{/if}
		</div>
		{#if tradePlan}
			<div class="plan-identity-grid">
				<span><small>Plan ID</small><strong class="font-mono">{tradePlan.id}</strong></span>
				<span><small>Account snapshot</small><strong>Agentic ••••{tradePlan.payload.account.accountLastFour}</strong></span>
				<span><small>Snapshot equity</small><strong class="font-mono">${tradePlan.payload.account.totalEquity.toLocaleString('en-US', { minimumFractionDigits: 2 })}</strong></span>
				<span><small>Expires</small><strong>{new Date(tradePlan.expiresAtUtc).toLocaleString()}</strong></span>
				<span><small>Policy</small><strong>Version {tradePlan.policyVersion}</strong></span>
			</div>

			<div class="plan-hash-row">
				<div><small>SHA-256 immutable payload hash</small><code>{tradePlan.planHash}</code></div>
				<button class="btn btn-secondary btn-compact" type="button" onclick={copyPlanHash}>{hashCopied ? 'Copied' : 'Copy hash'}</button>
			</div>

			<div class="plan-review-grid">
				<div class="plan-panel">
					<h3>Current Account Snapshot</h3>
					<div class="plan-money-row">
						<span>Cash <strong>${tradePlan.payload.account.cashAvailable.toFixed(2)}</strong></span>
						<span>Buying power <strong>${tradePlan.payload.account.buyingPower.toFixed(2)}</strong></span>
						<span>As of <strong>{new Date(tradePlan.payload.account.asOfUtc).toLocaleString()}</strong></span>
					</div>
					{#if tradePlan.payload.account.holdings.length === 0}
						<p class="text-muted">No positions were present in the captured Agentic account.</p>
					{:else}
						<div class="table-wrap compact-table-wrap">
							<table class="holdings-table compact-table">
								<thead><tr><th>Symbol</th><th>Quantity</th><th>Price</th><th>Weight</th></tr></thead>
								<tbody>{#each tradePlan.payload.account.holdings as holding}<tr><td class="font-mono font-bold">{holding.symbol}</td><td class="font-mono">{holding.quantity}</td><td class="font-mono">${holding.currentPrice.toFixed(2)}</td><td class="font-mono">{holding.portfolioWeightPercent.toFixed(1)}%</td></tr>{/each}</tbody>
							</table>
						</div>
					{/if}
				</div>

				<div class="plan-panel">
					<h3>Exact Proposed Orders</h3>
					{#if tradePlan.payload.orders.length === 0}
						<p class="text-muted">This snapshot contains no orders.</p>
					{:else}
						<div class="table-wrap compact-table-wrap">
							<table class="holdings-table compact-table">
								<thead><tr><th>Action</th><th>Symbol</th><th>Quantity</th><th>Limit</th><th>Notional</th></tr></thead>
								<tbody>{#each tradePlan.payload.orders as order}<tr><td><span class="badge {order.side === OrderSide.Buy ? 'badge-success' : 'badge-danger'}">{orderSideLabel(order.side)}</span><small class="order-type">{orderTypeLabel(order.type)}</small></td><td class="font-mono font-bold">{order.symbol}</td><td class="font-mono">{order.quantity}</td><td class="font-mono">{order.limitPrice == null ? '—' : `$${order.limitPrice.toFixed(2)}`}</td><td class="font-mono">${order.estimatedNotional.toFixed(2)}{#if order.isFullLiquidation}<small class="liquidation-flag">Full liquidation</small>{/if}</td></tr>{/each}</tbody>
							</table>
						</div>
					{/if}
				</div>
			</div>

			<div class="plan-review-grid">
				<div class="plan-panel">
					<h3>Target & Risk Evidence</h3>
					<div class="performance-grid">
						<span><strong>{tradePlan.payload.risk.estimatedTurnoverPercent.toFixed(2)}%</strong> turnover</span>
						<span><strong>{tradePlan.payload.risk.projectedAnnualizedVolatilityPercent.toFixed(2)}%</strong> annual volatility</span>
						<span><strong>{tradePlan.payload.risk.parametricDailyVaR95Percent.toFixed(2)}%</strong> one-day 95% VaR</span>
						<span><strong>{tradePlan.payload.risk.targetCashPercent.toFixed(2)}%</strong> target cash</span>
					</div>
					<p>{tradePlan.payload.risk.feedback}</p>
					<div class="allocation-chips">{#each tradePlan.payload.targetAllocations as allocation}<span><strong>{allocation.symbol}</strong> {allocation.targetWeightPercent.toFixed(1)}%</span>{/each}</div>
				</div>
				<div class="plan-panel">
					<h3>Provenance & Materiality</h3>
					<p>{tradePlan.payload.dataSourceSummary}</p>
					{#if tradePlan.requiresSecondaryConfirmation}
						<div class="material-warning"><strong>Second confirmation required</strong>{#each tradePlan.secondaryConfirmationReasons as reason}<span>{reason}</span>{/each}</div>
					{:else}
						<p class="text-muted">No material-plan trigger was detected.</p>
					{/if}
				</div>
			</div>

			{#if tradePlan.status === TradePlanStatus.Proposed}
				<div class="approval-boundary">
					<div class="approval-fields">
						<label>Exact approval phrase<input bind:value={approvalConfirmation} autocomplete="off" placeholder="APPROVE EXACT PLAN" /></label>
						{#if tradePlan.requiresSecondaryConfirmation}
							<label>Exact material-plan phrase<input bind:value={secondaryApprovalConfirmation} autocomplete="off" placeholder="CONFIRM MATERIAL TRADE PLAN" /></label>
						{/if}
						<button class="btn btn-primary" type="button" onclick={approveTradePlan} disabled={planActionPending || approvalConfirmation !== 'APPROVE EXACT PLAN' || (tradePlan.requiresSecondaryConfirmation && secondaryApprovalConfirmation !== 'CONFIRM MATERIAL TRADE PLAN')}>Approve Exact Plan</button>
					</div>
					<div class="rejection-fields">
						<label>Rejection reason<input bind:value={rejectionReason} maxlength="500" placeholder="Explain why this plan should be recalculated" /></label>
						<button class="btn btn-danger" type="button" onclick={rejectTradePlan} disabled={planActionPending || rejectionReason.trim().length < 5}>Reject Plan</button>
					</div>
				</div>
			{/if}
			{#if tradePlan.decisionReason}<p class="plan-decision"><strong>Decision record:</strong> {tradePlan.decisionReason}</p>{/if}
			{#if planActionError}<p class="policy-error">{planActionError}</p>{/if}

			{#if tradePlan.status === TradePlanStatus.Approved || executionBatch}
				<div class="execution-boundary">
					<div class="execution-heading">
						<div><span class="stat-lbl">MILESTONE 2 · DETERMINISTIC BROKER GATE</span><h3>Fresh Preflight & Idempotent Outbox</h3></div>
						{#if executionBatch}<span class="badge {executionStatusClass(executionBatch.status)}">{executionStatusLabel(executionBatch.status)}</span>{/if}
					</div>
					{#if executionBatch}
						<div class="execution-metrics">
							<span><small>Preflight</small><strong>{new Date(executionBatch.preflightAtUtc).toLocaleString()}</strong></span>
							<span><small>Account</small><strong>Agentic ••••{executionBatch.accountLastFour}</strong></span>
							<span><small>Reserved buys</small><strong>${executionBatch.reservedBuyingPower.toFixed(2)}</strong></span>
							<span><small>Sell notional</small><strong>${executionBatch.totalSellNotional.toFixed(2)}</strong></span>
						</div>
						<p class="execution-reason">{executionBatch.statusReason}</p>
						<div class="table-wrap compact-table-wrap">
							<table class="holdings-table compact-table execution-table">
								<thead><tr><th>Sequence</th><th>Order</th><th>Client order ID</th><th>Idempotency</th><th>Status</th><th>Broker order</th></tr></thead>
								<tbody>
									{#each executionBatch.attempts as attempt}
										<tr>
											<td class="font-mono">{attempt.sequence + 1}</td>
											<td><strong>{orderSideLabel(attempt.side)} {attempt.quantity} {attempt.symbol}</strong><small>@ ${attempt.limitPrice.toFixed(2)}</small></td>
											<td><code>{attempt.clientOrderId}</code></td>
											<td><code title={attempt.idempotencyKey}>{attempt.idempotencyKey.slice(0, 12)}…</code></td>
											<td><span class="badge {attemptStatusClass(attempt.status)}">{attemptStatusLabel(attempt.status)}</span>{#if attempt.failureReason}<small class="attempt-failure">{attempt.failureReason}</small>{/if}</td>
											<td><code>{attempt.brokerOrderId ?? '—'}</code></td>
										</tr>
									{/each}
								</tbody>
							</table>
						</div>
					{:else}
						<p>Refreshes account identity, holdings, open orders, buying power, broker quotes and eligibility; reruns policy and risk; obtains Robinhood pre-trade review; then persists sell-first outbox attempts before any possible order call.</p>
						<div class="execution-confirm-row">
							<label>Exact broker-gate phrase<input bind:value={executionConfirmation} autocomplete="off" placeholder="SUBMIT APPROVED PLAN" /></label>
							<button class="btn btn-primary" type="button" onclick={executeApprovedPlan} disabled={executionPending || executionConfirmation !== 'SUBMIT APPROVED PLAN'}>{executionPending ? 'Running fresh preflight…' : 'Run Fresh Broker Preflight'}</button>
						</div>
					{/if}
					{#if executionError}<p class="policy-error">{executionError}</p>{/if}
				</div>
			{/if}
			<p class="submission-lock"><strong>Authority lock:</strong> the preflight adapter and durable submission outbox are implemented, but persisted and application live authority both remain disabled. Current operation can validate and record a blocked batch; it cannot route a Robinhood order.</p>
		{:else}
			<p class="text-muted">A risk-approved live analysis will persist an exact account snapshot, allocation, order set, policy version, provenance, expiry, and SHA-256 hash here. Mock runs never create approval plans.</p>
		{/if}
	</section>

	<!-- ========================================================================= -->
	<!-- 1. HOW MUCH IS IN MY ACCOUNT & RE-EVALUATION COUNTDOWN                     -->
	<!-- ========================================================================= -->
	<section class="section-block">
		<div class="section-title-row">
			<h2>1. How Much Is In My Account</h2>
			<span class="section-sub">{rhAccount?.isConnected ? 'Live Robinhood balance & purchasing power' : 'Disconnected ($0.00) — use Connect Robinhood in the top right'}</span>
		</div>

		<div class="account-metrics-grid">
			<!-- Total Equity Card -->
			<div class="card stat-card highlight-card">
				<span class="stat-lbl">Total Portfolio Equity</span>
				<div class="stat-val font-mono text-primary">
					${(rhAccount?.totalEquity ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
				</div>
				<div class="stat-sub">
					{#if rhAccount?.isConnected}
						<span class="badge {totalUnrealizedPnL >= 0 ? 'badge-success' : 'badge-danger'}">
							{totalUnrealizedPnL >= 0 ? '+' : ''}${totalUnrealizedPnL.toFixed(2)}
						</span>
						<span class="stat-caption">Holdings Unrealized P&L</span>
					{:else}
						<span class="text-muted font-mono">Account Disconnected</span>
					{/if}
				</div>
			</div>

			<!-- Liquid Cash Available -->
			<div class="card stat-card">
				<span class="stat-lbl">Available Cash Reserves</span>
				<div class="stat-val font-mono">
					${(rhAccount?.cashAvailable ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
				</div>
				<div class="stat-sub">
					<span class="stat-caption font-mono">
						{(rhAccount?.totalEquity ?? 0) > 0 ? (((rhAccount?.cashAvailable ?? 0) / (rhAccount?.totalEquity ?? 1)) * 100).toFixed(1) : '0.0'}% Liquid Reserves
					</span>
				</div>
			</div>

			<!-- Robinhood Buying Power -->
			<div class="card stat-card">
				<span class="stat-lbl">Robinhood Buying Power</span>
				<div class="stat-val font-mono text-accent">
					${(rhAccount?.buyingPower ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
				</div>
				<div class="stat-sub">
					<span class="stat-caption">{rhAccount?.isConnected ? 'Robinhood Margin Purchasing Power' : 'Connect account to load'}</span>
				</div>
			</div>

			<!-- Time Until Re-Evaluation -->
			<div class="card stat-card cadence-card">
				<span class="stat-lbl">Next AI Re-Evaluation</span>
				<div class="stat-val font-mono countdown-val">
					⏳ {countdownText}
				</div>
				<div class="stat-sub">
					<span class="badge badge-primary">Bi-Weekly Cadence (14 Days)</span>
				</div>
			</div>
		</div>
	</section>

	<!-- ========================================================================= -->
	<!-- 2. WHAT I AM INVESTED IN (Holdings Blotter & Asset Switcher)              -->
	<!-- ========================================================================= -->
	<section class="section-block">
		<div class="section-title-row">
			<div>
				<h2>2. What I Am Invested In</h2>
				<span class="section-sub">Live positions from the funded Robinhood Agentic account.</span>
			</div>
			<div class="holding-count-badge font-mono">
				{holdings.length} Active Positions
			</div>
		</div>

		<div class="card table-card">
			{#if holdings.length === 0}
				<div class="empty-state">
					<p>No holdings found in Robinhood account. Use Sync MCP to refresh your positions.</p>
				</div>
			{:else}
				<div class="table-wrap">
					<table class="holdings-table">
						<thead>
							<tr>
								<th>Asset</th>
								<th>Shares Held</th>
								<th>Avg Entry Price</th>
								<th>Live Market Price</th>
								<th>Total Value</th>
								<th>Unrealized Return</th>
								<th>Portfolio Weight</th>
							</tr>
						</thead>
						<tbody>
							{#each holdings as item}
								<tr>
									<td>
										<div class="symbol-cell">
											<span class="symbol font-mono">{item.symbol}</span>
											<span class="name text-muted">{item.name}</span>
										</div>
									</td>
									<td class="font-mono font-bold">{item.quantity}</td>
									<td class="font-mono">${item.averageCostBasis.toFixed(2)}</td>
									<td class="font-mono text-primary font-bold">${item.currentPrice.toFixed(2)}</td>
									<td class="font-mono font-bold">${item.currentMarketValue.toLocaleString('en-US', { minimumFractionDigits: 2 })}</td>
									<td>
										<span class="badge {item.unrealizedPnL >= 0 ? 'badge-success' : 'badge-danger'}">
											{item.unrealizedPnL >= 0 ? '+' : ''}${item.unrealizedPnL.toFixed(2)} ({item.unrealizedPnLPercent >= 0 ? '+' : ''}{item.unrealizedPnLPercent.toFixed(2)}%)
										</span>
									</td>
									<td>
										<div class="weight-cell">
											<span class="weight-num font-mono">{item.portfolioWeightPercent.toFixed(1)}%</span>
											<div class="weight-bar-bg">
												<div class="weight-bar-fill" style="width: {Math.min(100, item.portfolioWeightPercent * 3.5)}%"></div>
											</div>
										</div>
									</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			{/if}
		</div>
	</section>

	<!-- ========================================================================= -->
	<!-- 3. MARKET-WIDE INTELLIGENCE PIPELINE                                      -->
	<!-- ========================================================================= -->
	<section class="section-block">
		<div class="section-title-row">
			<div>
				<h2>3. Market-Wide Intelligence & Allocation</h2>
				<span class="section-sub">A broad-market discovery pass feeds bounded deep research, mathematical allocation, and a hard risk gate.</span>
			</div>
			{#if marketRun}
				<div class="run-badges">
					{#if marketRun.isMockRun}<span class="badge badge-primary">MOCK RUN</span>{/if}
					<span class="badge {marketRun.isRiskApproved ? 'badge-success' : 'badge-danger'}">
						{marketRun.isRiskApproved ? 'RISK APPROVED' : 'RECALCULATION REQUIRED'}
					</span>
				</div>
			{/if}
		</div>

		<div class="pipeline-role-grid">
			<div class="card pipeline-layer"><span>INTELLIGENCE</span><strong>Macro Regime Observer</strong><small>Fundamental Researcher · Technical Strategist · Sentiment Scout</small></div>
			<div class="card pipeline-layer"><span>OPTIMIZATION</span><strong>Asset Selection & Candidate Screener</strong><small>Quantitative Allocator · covariance-aware hierarchical risk parity</small></div>
			<div class="card pipeline-layer"><span>GOVERNANCE</span><strong>Risk & Compliance Auditor</strong><small>Exposure caps · turnover · volatility · 95% VaR · ATR stops</small></div>
			<div class="card pipeline-layer"><span>MAINTENANCE</span><strong>Execution & Rebalancing Manager</strong><small>Paper-order proposals · Post-Mortem & Reflection Agent</small></div>
		</div>

		{#if isOptimizing}
			<div class="deliberation-live-card card">
				<div class="live-pulse"><span class="dot dot-online"></span><span class="live-title">Market intelligence pipeline is running</span></div>
				<p class="deliberation-step font-mono">{optimizationStep}</p>
			</div>
		{:else if marketScanError}
			<div class="card market-error"><strong>Scan failed:</strong> {marketScanError}</div>
		{:else if !marketRun}
			<div class="card empty-state">
				<p>Run live analysis against the connected account, or use Mock Analysis with a synthetic $10,000 all-cash portfolio and deterministic agent reasoning. Both modes remain paper-only.</p>
			</div>
		{/if}

		{#if marketRun}
			<div class="market-summary-grid">
				<div class="card stat-card highlight-card">
					<span class="stat-lbl">Macro Regime</span>
					<div class="stat-val">{marketRun.macroRegime.regime}</div>
					<div class="stat-sub font-mono">Equity {marketRun.macroRegime.targetEquityPercent.toFixed(1)}% · Cash {marketRun.macroRegime.targetCashPercent.toFixed(1)}%</div>
				</div>
				<div class="card stat-card">
					<span class="stat-lbl">Universe Coverage</span>
					<div class="stat-val font-mono">{marketRun.totalSecuritiesScanned.toLocaleString()}</div>
					<div class="stat-sub">{marketRun.eligibleSecurities.toLocaleString()} passed the broad screen</div>
				</div>
				<div class="card stat-card">
					<span class="stat-lbl">Market Stress</span>
					<div class="stat-val font-mono">VIX {marketRun.macroRegime.vix.toFixed(1)}</div>
					<div class="stat-sub">10Y Treasury {marketRun.macroRegime.tenYearYield.toFixed(2)}%</div>
				</div>
				<div class="card stat-card">
					<span class="stat-lbl">Proposed Turnover</span>
					<div class="stat-val font-mono">{marketRun.estimatedTurnoverPercent.toFixed(1)}%</div>
					<div class="stat-sub">{marketRun.proposedPaperOrders.length} paper orders · {marketRun.targetCashPercent.toFixed(1)}% target cash</div>
				</div>
			</div>

			<div class="card market-narrative">
				<h3>Macro Regime Observer</h3>
				<p>{marketRun.macroRegime.rationale}</p>
				{#if marketRun.macroRegime.keyRisks.length > 0}
					<div class="thought-chips">{#each marketRun.macroRegime.keyRisks as risk}<span class="chip">{risk}</span>{/each}</div>
				{/if}
			</div>

			<div class="card table-card">
				<div class="table-heading"><h3>Approved Candidate Research</h3><span class="badge badge-primary">Top {marketRun.candidates.length} deeply analyzed</span></div>
				<div class="table-wrap">
					<table class="holdings-table intelligence-table">
						<thead><tr><th>Ticker</th><th>Sector</th><th>Fundamental</th><th>Technical</th><th>Sentiment</th><th>Conviction</th><th>Volatility</th><th>Gate</th></tr></thead>
						<tbody>
							{#each marketRun.candidates as candidate}
								<tr title={candidate.rationale}>
									<td><strong class="font-mono">{candidate.symbol}</strong><small>{candidate.name}</small></td>
									<td>{candidate.sector}</td>
									<td class="font-mono" title={candidate.fundamentalDataQuality}>
										{candidate.fundamentalHealthScore.toFixed(0)}
										{#if candidate.hasVerifiedFundamentals && candidate.fundamentalSources?.[0]}
											<a class="verified-data source-link" href={candidate.fundamentalSources[0]} target="_blank" rel="noreferrer">SEC verified ↗</a>
										{:else}
											<small class="mock-data">Synthetic</small>
										{/if}
									</td>
									<td class="font-mono">{candidate.technicalMomentumScore.toFixed(0)}</td>
									<td class="font-mono">{candidate.sentimentScore.toFixed(0)}</td>
									<td class="font-mono font-bold">{candidate.compositeConvictionScore.toFixed(1)}</td>
									<td class="font-mono">{candidate.annualizedVolatilityPercent.toFixed(1)}%</td>
									<td>
										<span class="badge {candidate.isApproved ? 'badge-success' : 'badge-danger'}">{candidate.isApproved ? 'PASS' : 'BLOCK'}</span>
										<small class="gate-detail">{candidate.riskFlags[0] ?? 'All hard gates passed'}</small>
									</td>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>
			</div>

			<div class="card table-card">
				<div class="table-heading"><h3>Quantitative Target Allocation</h3><span class="badge {marketRun.isRiskApproved ? 'badge-success' : 'badge-danger'}">Risk & Compliance Auditor</span></div>
				<div class="table-wrap">
					<table class="holdings-table intelligence-table">
						<thead><tr><th>Ticker</th><th>Sector</th><th>Current</th><th>Target</th><th>Delta</th><th>Target Value</th><th>ATR Stop</th></tr></thead>
						<tbody>
							{#each marketRun.targetAllocations as allocation}
								<tr><td><strong class="font-mono">{allocation.symbol}</strong></td><td>{allocation.sector}</td><td class="font-mono">{allocation.currentWeightPercent.toFixed(1)}%</td><td class="font-mono font-bold">{allocation.targetWeightPercent.toFixed(1)}%</td><td class="font-mono">{allocation.weightDeltaPercent >= 0 ? '+' : ''}{allocation.weightDeltaPercent.toFixed(1)}%</td><td class="font-mono">${allocation.targetValue.toLocaleString('en-US', { maximumFractionDigits: 0 })}</td><td class="font-mono">${allocation.stopLossPrice.toFixed(2)}</td></tr>
							{/each}
						</tbody>
					</table>
				</div>
			</div>

			<div class="market-footer-grid">
				<div class="card market-narrative">
					<h3>Risk & Compliance Audit</h3>
					<p>{marketRun.riskAuditorFeedback}</p>
					<div class="performance-grid">
						<span><strong>{marketRun.projectedAnnualizedVolatilityPercent.toFixed(2)}%</strong> projected annual volatility</span>
						<span><strong>{marketRun.parametricDailyVaR95Percent.toFixed(2)}%</strong> one-day 95% VaR</span>
						<span><strong>{marketRun.estimatedTurnoverPercent.toFixed(2)}%</strong> phased turnover</span>
						<span><strong>{marketRun.targetCashPercent.toFixed(2)}%</strong> target cash</span>
					</div>
				</div>
				<div class="card market-narrative">
					<h3>Post-Mortem & Reflection</h3>
					<p>{marketRun.reflectionSummary}</p>
					<div class="performance-grid">
						<span><strong>{marketRun.performanceMetrics.observationCount}</strong> observations</span>
						<span><strong>{marketRun.performanceMetrics.annualizedSharpeRatio?.toFixed(2) ?? '—'}</strong> Sharpe</span>
						<span><strong>{marketRun.performanceMetrics.maxDrawdownPercent.toFixed(2)}%</strong> max drawdown</span>
						<span><strong>{marketRun.performanceMetrics.winRatePercent.toFixed(1)}%</strong> win rate</span>
					</div>
				</div>
			</div>
			<p class="data-source-note">{marketRun.dataSourceSummary}</p>
		{/if}
	</section>

</div>

<style>
	.command-center-page {
		display: flex;
		flex-direction: column;
		gap: 2rem;
	}

	.live-policy-card {
		padding: 1.25rem 1.5rem;
		border-left: 4px solid var(--success);
	}

	.live-policy-card.halted { border-left-color: var(--danger); }
	.trade-plan-card {
		padding: 1.25rem 1.5rem;
		border-left: 4px solid var(--primary);
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}
	.trade-plan-card.plan-approved { border-left-color: var(--success); }
	.plan-identity-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
		gap: 0.65rem;
	}
	.plan-identity-grid span,
	.plan-panel {
		padding: 0.75rem;
		background: var(--bg-canvas);
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-sm);
	}
	.plan-identity-grid span { display: flex; flex-direction: column; gap: 0.2rem; min-width: 0; }
	.plan-identity-grid small,
	.plan-hash-row small { color: var(--text-muted); }
	.plan-identity-grid strong { overflow-wrap: anywhere; }
	.plan-hash-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.75rem;
		padding: 0.75rem;
		background: rgba(56, 189, 248, 0.05);
		border: 1px solid rgba(56, 189, 248, 0.2);
		border-radius: var(--radius-sm);
	}
	.plan-hash-row div { display: flex; flex-direction: column; gap: 0.25rem; min-width: 0; }
	.plan-hash-row code { color: var(--primary); font-size: 0.72rem; overflow-wrap: anywhere; }
	.btn-compact { padding: 0.45rem 0.65rem; white-space: nowrap; }
	.plan-review-grid {
		display: grid;
		grid-template-columns: repeat(2, minmax(0, 1fr));
		gap: 0.85rem;
	}
	.plan-panel { display: flex; flex-direction: column; gap: 0.65rem; min-width: 0; }
	.plan-panel h3 { margin: 0; font-size: 0.95rem; }
	.plan-panel p { font-size: 0.8rem; line-height: 1.45; color: var(--text-secondary); }
	.plan-money-row { display: flex; flex-wrap: wrap; gap: 0.5rem 1rem; font-size: 0.75rem; color: var(--text-muted); }
	.plan-money-row span { display: flex; flex-direction: column; }
	.plan-money-row strong { color: var(--text-primary); }
	.compact-table-wrap { border: 1px solid var(--border-subtle); border-radius: var(--radius-sm); }
	.compact-table { font-size: 0.76rem; }
	.compact-table th,
	.compact-table td { padding: 0.5rem; }
	.order-type,
	.liquidation-flag { display: block; margin-top: 0.2rem; font-size: 0.62rem; color: var(--text-muted); }
	.liquidation-flag { color: var(--warning); }
	.allocation-chips { display: flex; flex-wrap: wrap; gap: 0.35rem; }
	.allocation-chips span { padding: 0.25rem 0.45rem; border: 1px solid var(--border-subtle); border-radius: var(--radius-sm); font-size: 0.7rem; }
	.material-warning { display: flex; flex-direction: column; gap: 0.25rem; padding: 0.65rem; border: 1px solid var(--warning); border-radius: var(--radius-sm); color: var(--warning); font-size: 0.75rem; }
	.approval-boundary {
		display: grid;
		grid-template-columns: 2fr 1fr;
		gap: 0.85rem;
		padding: 0.85rem;
		border: 1px solid rgba(56, 189, 248, 0.25);
		border-radius: var(--radius-sm);
	}
	.approval-fields,
	.rejection-fields { display: flex; align-items: end; gap: 0.65rem; flex-wrap: wrap; }
	.approval-fields label,
	.rejection-fields label { display: flex; flex: 1 1 220px; flex-direction: column; gap: 0.3rem; font-size: 0.72rem; color: var(--text-muted); }
	.approval-fields input,
	.rejection-fields input {
		width: 100%;
		padding: 0.65rem 0.75rem;
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-sm);
		background: var(--bg-surface-elevated);
		color: var(--text-primary);
	}
	.plan-decision { color: var(--text-secondary); }
	.execution-boundary {
		display: flex;
		flex-direction: column;
		gap: 0.8rem;
		padding: 0.9rem;
		border: 1px solid rgba(56, 189, 248, 0.3);
		border-radius: var(--radius-sm);
		background: rgba(56, 189, 248, 0.035);
	}
	.execution-heading,
	.execution-confirm-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 0.75rem;
	}
	.execution-heading h3 { margin: 0.2rem 0 0; font-size: 0.98rem; }
	.execution-metrics {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(145px, 1fr));
		gap: 0.55rem;
	}
	.execution-metrics span {
		display: flex;
		flex-direction: column;
		gap: 0.2rem;
		padding: 0.55rem;
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-sm);
		background: var(--bg-canvas);
	}
	.execution-metrics small,
	.execution-table td small { display: block; color: var(--text-muted); }
	.execution-reason { margin: 0; color: var(--text-secondary); }
	.execution-table code { font-size: 0.67rem; overflow-wrap: anywhere; }
	.attempt-failure { max-width: 18rem; margin-top: 0.25rem; color: var(--danger) !important; line-height: 1.3; }
	.execution-confirm-row label {
		display: flex;
		flex: 1;
		flex-direction: column;
		gap: 0.3rem;
		font-size: 0.72rem;
		color: var(--text-muted);
	}
	.execution-confirm-row input {
		width: 100%;
		padding: 0.65rem 0.75rem;
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-sm);
		background: var(--bg-surface-elevated);
		color: var(--text-primary);
	}
	.submission-lock { padding: 0.65rem 0.75rem; border-radius: var(--radius-sm); background: rgba(245, 158, 11, 0.08); border: 1px solid rgba(245, 158, 11, 0.3); color: var(--warning); font-size: 0.78rem; }
	.policy-heading,
	.policy-action-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
	}
	.policy-heading h2 { margin: 0.2rem 0 0; }
	.policy-metrics {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
		gap: 0.75rem;
		margin: 1rem 0;
	}
	.policy-metrics span {
		display: flex;
		flex-direction: column;
		gap: 0.2rem;
		padding: 0.65rem;
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-sm);
	}
	.policy-metrics small { color: var(--text-muted); }
	.policy-action-row input {
		flex: 1;
		min-width: 16rem;
		padding: 0.7rem 0.8rem;
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-sm);
		background: var(--bg-surface-elevated);
		color: var(--text-primary);
	}
	.halt-reason,
	.policy-error { color: var(--danger); }
	@media (max-width: 720px) {
		.policy-heading,
		.policy-action-row,
		.execution-heading,
		.execution-confirm-row { align-items: stretch; flex-direction: column; }
		.policy-action-row input { width: 100%; min-width: 0; }
		.plan-review-grid,
		.approval-boundary { grid-template-columns: 1fr; }
		.plan-hash-row { align-items: stretch; flex-direction: column; }
	}

	.app-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1.5rem 1.75rem;
		flex-wrap: wrap;
		gap: 1.25rem;
		border-left: 4px solid var(--primary);
	}

	.header-main {
		display: flex;
		flex-direction: column;
		gap: 0.4rem;
		max-width: 680px;
	}

	.header-badge-row {
		display: flex;
		align-items: center;
		gap: 0.6rem;
		flex-wrap: wrap;
	}

	.rh-live-pill {
		display: inline-flex;
		align-items: center;
		gap: 0.4rem;
		background: rgba(34, 197, 94, 0.12);
		border: 1px solid rgba(34, 197, 94, 0.3);
		color: #22c55e;
		font-size: 0.7rem;
		font-weight: 800;
		padding: 0.2rem 0.6rem;
		border-radius: var(--radius-sm);
		letter-spacing: 0.05em;
	}

	.rh-disconnected-pill {
		display: inline-flex;
		align-items: center;
		gap: 0.4rem;
		background: rgba(239, 68, 68, 0.12);
		border: 1px solid rgba(239, 68, 68, 0.3);
		color: var(--danger);
		font-size: 0.7rem;
		font-weight: 800;
		padding: 0.2rem 0.6rem;
		border-radius: var(--radius-sm);
		letter-spacing: 0.05em;
	}

	.account-id {
		font-size: 0.85rem;
		font-weight: 700;
		color: var(--primary);
	}

	.account-type-pill {
		font-size: 0.72rem;
		color: var(--text-muted);
		background: var(--bg-canvas);
		padding: 0.15rem 0.5rem;
		border-radius: var(--radius-sm);
		border: 1px solid var(--border-subtle);
	}

	.header-actions {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		flex-wrap: wrap;
	}

	.run-badges {
		display: flex;
		align-items: center;
		gap: 0.45rem;
		flex-wrap: wrap;
	}

	.section-block {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.section-title-row {
		display: flex;
		align-items: flex-end;
		justify-content: space-between;
		flex-wrap: wrap;
		gap: 0.75rem;
	}

	.section-title-row h2 {
		font-size: 1.25rem;
		font-weight: 700;
		color: var(--text-primary);
	}

	.section-sub {
		font-size: 0.82rem;
		color: var(--text-muted);
	}

	.account-metrics-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
		gap: 1.25rem;
	}

	.stat-card {
		display: flex;
		flex-direction: column;
		gap: 0.4rem;
		padding: 1.25rem;
	}

	.highlight-card {
		background: linear-gradient(135deg, var(--bg-surface-elevated) 0%, rgba(56, 189, 248, 0.04) 100%);
		border: 1px solid rgba(56, 189, 248, 0.25);
	}

	.cadence-card {
		background: linear-gradient(135deg, var(--bg-surface-elevated) 0%, rgba(168, 85, 247, 0.05) 100%);
		border: 1px solid rgba(168, 85, 247, 0.25);
	}

	.stat-lbl {
		font-size: 0.75rem;
		font-weight: 600;
		text-transform: uppercase;
		color: var(--text-muted);
		letter-spacing: 0.04em;
	}

	.stat-val {
		font-size: 1.6rem;
		font-weight: 700;
		color: var(--text-primary);
	}

	.countdown-val {
		color: #a855f7;
		font-size: 1.45rem;
	}

	.stat-sub {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		font-size: 0.8rem;
	}

	.stat-caption {
		color: var(--text-secondary);
	}

	.table-card {
		padding: 0;
		overflow: hidden;
	}

	.table-wrap {
		overflow-x: auto;
	}

	.holdings-table {
		width: 100%;
		border-collapse: collapse;
		text-align: left;
		font-size: 0.88rem;
	}

	.holdings-table th {
		padding: 0.8rem 1rem;
		background: var(--bg-surface);
		border-bottom: 1px solid var(--border-subtle);
		color: var(--text-muted);
		font-size: 0.72rem;
		text-transform: uppercase;
		font-weight: 600;
		letter-spacing: 0.04em;
	}

	.holdings-table td {
		padding: 0.85rem 1rem;
		border-bottom: 1px solid var(--border-subtle);
		color: var(--text-primary);
	}

	.symbol-cell {
		display: flex;
		flex-direction: column;
		gap: 0.15rem;
	}

	.symbol-cell .symbol {
		font-weight: 700;
		color: var(--primary);
	}

	.symbol-cell .name {
		font-size: 0.75rem;
	}

	.weight-cell {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.weight-num {
		min-width: 42px;
		font-weight: 600;
	}

	.weight-bar-bg {
		width: 60px;
		height: 6px;
		background: var(--bg-canvas);
		border-radius: var(--radius-full);
		overflow: hidden;
	}

	.weight-bar-fill {
		height: 100%;
		background: linear-gradient(90deg, var(--primary) 0%, #a855f7 100%);
		border-radius: var(--radius-full);
	}

	.holding-count-badge {
		font-size: 0.8rem;
		background: var(--bg-surface);
		border: 1px solid var(--border-subtle);
		padding: 0.25rem 0.65rem;
		border-radius: var(--radius-sm);
		color: var(--text-secondary);
	}

	.deliberation-live-card {
		background: linear-gradient(135deg, rgba(56, 189, 248, 0.08) 0%, rgba(99, 102, 241, 0.08) 100%);
		border-color: rgba(56, 189, 248, 0.3);
		display: flex;
		flex-direction: column;
		gap: 0.4rem;
		padding: 1.25rem;
	}

	.live-pulse {
		display: flex;
		align-items: center;
		gap: 0.6rem;
	}

	.live-title {
		font-weight: 700;
		color: var(--primary);
		font-size: 0.95rem;
	}

	.deliberation-step {
		font-size: 0.85rem;
		color: var(--text-primary);
	}

	.pipeline-role-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(230px, 1fr));
		gap: 0.85rem;
	}

	.pipeline-layer {
		display: flex;
		flex-direction: column;
		gap: 0.3rem;
		padding: 1rem;
		border-top: 3px solid var(--primary);
	}

	.pipeline-layer span {
		font-family: var(--font-mono);
		font-size: 0.68rem;
		font-weight: 800;
		color: var(--primary);
		letter-spacing: 0.08em;
	}

	.pipeline-layer strong { font-size: 0.86rem; }
	.pipeline-layer small { color: var(--text-muted); line-height: 1.35; }

	.market-summary-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
		gap: 1rem;
	}

	.market-narrative {
		padding: 1.1rem 1.25rem;
		display: flex;
		flex-direction: column;
		gap: 0.55rem;
	}

	.market-narrative p {
		font-size: 0.86rem;
		color: var(--text-secondary);
		line-height: 1.5;
	}

	.market-error {
		padding: 1rem;
		border-color: var(--danger);
		color: var(--danger);
	}

	.table-heading {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
		padding: 1rem 1.1rem;
	}

	.intelligence-table td small {
		display: block;
		max-width: 170px;
		white-space: nowrap;
		overflow: hidden;
		text-overflow: ellipsis;
		color: var(--text-muted);
		margin-top: 0.15rem;
	}

	.intelligence-table td small.mock-data { color: var(--warning); }
	.source-link,
	.gate-detail {
		display: block;
		margin-top: 0.2rem;
		font-size: 0.68rem;
		font-family: var(--font-sans);
	}
	.source-link { color: var(--success); text-decoration: none; }
	.source-link:hover { text-decoration: underline; }
	.gate-detail { max-width: 15rem; color: var(--text-muted); line-height: 1.25; }

	.performance-grid {
		display: grid;
		grid-template-columns: repeat(2, minmax(0, 1fr));
		gap: 0.45rem;
		font-size: 0.75rem;
		color: var(--text-muted);
	}

	.performance-grid span {
		display: flex;
		flex-direction: column;
		padding: 0.45rem;
		background: var(--bg-canvas);
		border-radius: var(--radius-sm);
	}

	.market-footer-grid {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
		gap: 1rem;
	}

	.data-source-note {
		font-size: 0.72rem;
		color: var(--text-muted);
		font-family: var(--font-mono);
	}

	.thought-chips {
		display: flex;
		gap: 0.35rem;
		flex-wrap: wrap;
		margin-top: auto;
	}

	.chip {
		font-size: 0.7rem;
		background: var(--bg-canvas);
		border: 1px solid var(--border-subtle);
		padding: 0.15rem 0.45rem;
		border-radius: var(--radius-sm);
		color: var(--text-muted);
	}

	.font-mono { font-family: var(--font-mono); }
	.font-bold { font-weight: 700; }
	.text-primary { color: var(--primary); }
	.text-accent { color: #a855f7; }
	.text-muted { color: var(--text-muted); }

	@media (max-width: 900px) {
		.account-metrics-grid {
			grid-template-columns: 1fr;
		}
	}
</style>
