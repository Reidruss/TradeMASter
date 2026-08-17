<script lang="ts">
	import { onMount } from 'svelte';
	import MarketTickerBar from '$lib/components/MarketTickerBar.svelte';
	import CandleChart from '$lib/components/CandleChart.svelte';
	import TradeWidget from '$lib/components/TradeWidget.svelte';
	import PortfolioSummaryCard from '$lib/components/PortfolioSummaryCard.svelte';
	import PositionGrid from '$lib/components/PositionGrid.svelte';
	import OrderBlotter from '$lib/components/OrderBlotter.svelte';
	import { portfolioService, orderService, type Portfolio, type Order } from '$lib/api';

	let activeSymbol = $state('NVDA');
	let portfolio = $state<Portfolio | null>(null);
	let orders = $state<Order[]>([]);
	let isLoading = $state(true);

	async function refreshData() {
		try {
			const [p, o] = await Promise.all([
				portfolioService.getPortfolio(),
				orderService.getOrders()
			]);
			portfolio = p;
			orders = o;
		} catch (err) {
			console.error('Failed to load portfolio/orders', err);
		} finally {
			isLoading = false;
		}
	}

	onMount(() => {
		refreshData();
		const interval = setInterval(refreshData, 10000);
		return () => clearInterval(interval);
	});

	function handleSelectSymbol(sym: string) {
		activeSymbol = sym;
	}
</script>

<svelte:head>
	<title>TradeMASter • Autonomous Multi-Agent Trading System</title>
</svelte:head>

<div class="dashboard-page">
	<!-- Top Watchlist Ticker Bar -->
	<MarketTickerBar onSelectSymbol={handleSelectSymbol} />

	<div class="dashboard-body">
		<!-- Hero / Header -->
		<div class="dashboard-header">
			<div>
				<h1>Autonomous Trading Command Center</h1>
				<p>Multi-agent deliberation, real-time market ingestion, risk governance & paper execution engine.</p>
			</div>
			<div class="header-actions">
				<button type="button" class="btn btn-secondary" onclick={refreshData}>
					<span>↻ Refresh State</span>
				</button>
				<a href="/agents" class="btn btn-primary">
					<span>Agent War Room →</span>
				</a>
			</div>
		</div>

		<!-- Portfolio Summary Metric Cards -->
		<PortfolioSummaryCard {portfolio} />

		<!-- Chart + Execution Grid -->
		<div class="trading-grid">
			<div class="chart-column">
				<CandleChart symbol={activeSymbol} onSymbolChange={handleSelectSymbol} />
			</div>
			<div class="order-column">
				<TradeWidget symbol={activeSymbol} onOrderPlaced={refreshData} />
			</div>
		</div>

		<!-- Positions and Order Blotter -->
		<div class="holdings-grid">
			<PositionGrid positions={portfolio?.positions ?? []} onTradeSymbol={handleSelectSymbol} />
			<OrderBlotter {orders} />
		</div>
	</div>
</div>

<style>
	.dashboard-page {
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
	}

	.dashboard-body {
		display: flex;
		flex-direction: column;
		gap: 1.75rem;
	}

	.dashboard-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		flex-wrap: wrap;
		gap: 1rem;
	}

	.header-actions {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.trading-grid {
		display: grid;
		grid-template-columns: 2fr 1fr;
		gap: 1.5rem;
		align-items: start;
	}

	.holdings-grid {
		display: grid;
		grid-template-columns: 1fr 1fr;
		gap: 1.5rem;
		align-items: start;
	}

	@media (max-width: 1024px) {
		.trading-grid, .holdings-grid {
			grid-template-columns: 1fr;
		}
	}
</style>
