<script lang="ts">
	import '../app.css';
	import { onMount } from 'svelte';
	import Navbar from '$lib/components/Navbar.svelte';
	import RobinhoodSplashScreen from '$lib/components/RobinhoodSplashScreen.svelte';
	import { robinhoodService, type RobinhoodAccountInfo } from '$lib/api';
	import { robinhoodAccount } from '$lib/stores/robinhood';

	let { children } = $props();

	let rhAccount = $derived($robinhoodAccount);
	let showSplashScreen = $state(false);

	onMount(() => {
		(async () => {
			try {
				const status = await robinhoodService.getStatus();
				robinhoodAccount.set(status);
			} catch {
				robinhoodAccount.set(null);
			} finally {
				// Connection is controlled exclusively by the top-right Navbar button.
				showSplashScreen = false;
			}
		})();
	});

	function handleConnected(account: RobinhoodAccountInfo) {
		robinhoodAccount.set(account);
		showSplashScreen = false;
	}

	function handleOpenConnectModal() {
		showSplashScreen = true;
	}

	async function handleDisconnect() {
		try {
			await robinhoodService.disconnect();
			robinhoodAccount.set(null);
			showSplashScreen = false;
		} catch (err) {
			console.error('Failed to disconnect', err);
		}
	}
</script>

<div class="app-layout">
	<Navbar
		rhAccount={rhAccount}
		onOpenConnect={handleOpenConnectModal}
		onDisconnect={handleDisconnect}
	/>

	{#if showSplashScreen}
		<RobinhoodSplashScreen onConnected={handleConnected} />
	{/if}

	<main class="main-content">
		<div class="container">
			{@render children()}
		</div>
	</main>

	<footer class="app-footer">
		<div class="container footer-content">
			<div class="footer-left">
				<span class="brand-foot">TradeMASter</span>
				<span class="sep">•</span>
				<span class="tech-stack">Robinhood Autonomous Multi-Agent Trading System</span>
			</div>
			<div class="footer-right">
				<span class="mcp-indicator font-mono">MCP: https://agent.robinhood.com/mcp/trading</span>
				<span class="sep">•</span>
				<a href="/scalar/v1" target="_blank" rel="noreferrer">API Docs</a>
			</div>
		</div>
	</footer>
</div>

<style>
	.app-layout {
		min-height: 100vh;
		display: flex;
		flex-direction: column;
	}

	.main-content {
		flex: 1;
		padding: 1.5rem 0 3.5rem 0;
	}

	.app-footer {
		border-top: 1px solid var(--border-subtle);
		background: var(--bg-surface);
		padding: 1.25rem 0;
		font-size: 0.82rem;
		color: var(--text-muted);
	}

	.footer-content {
		display: flex;
		align-items: center;
		justify-content: space-between;
		flex-wrap: wrap;
		gap: 1rem;
	}

	.footer-left, .footer-right {
		display: flex;
		align-items: center;
		gap: 0.6rem;
		flex-wrap: wrap;
	}

	.brand-foot {
		font-weight: 700;
		color: var(--text-primary);
	}

	.mcp-indicator {
		color: var(--text-muted);
		font-size: 0.75rem;
	}

	.sep {
		color: var(--border-strong);
	}

	.font-mono {
		font-family: var(--font-mono);
	}
</style>
