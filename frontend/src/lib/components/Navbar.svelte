<script lang="ts">
	import { onMount } from 'svelte';
	import { healthService } from '$lib/api';
	import type { HealthInfo, RobinhoodAccountInfo } from '$lib/api';

	let {
		rhAccount = null,
		onOpenConnect,
		onDisconnect
	}: {
		rhAccount?: RobinhoodAccountInfo | null;
		onOpenConnect?: () => void;
		onDisconnect?: () => void;
	} = $props();

	let health = $state<HealthInfo | null>(null);
	let isLoading = $state(true);
	let showAccountMenu = $state(false);

	onMount(() => {
		let isMounted = true;

		async function checkStatus() {
			try {
				const h = await healthService.getHealth();
				if (isMounted) health = h;
			} catch {
				// keep state
			} finally {
				if (isMounted) isLoading = false;
			}
		}

		checkStatus();
		const interval = setInterval(checkStatus, 20000);

		return () => {
			isMounted = false;
			clearInterval(interval);
		};
	});
</script>

<header class="navbar">
	<div class="container nav-content">
		<div class="nav-left">
			<a href="/" class="brand">
				<div class="logo-icon">⚡</div>
				<div class="brand-text">
					<span class="brand-name">TradeMASter</span>
					<span class="brand-tag">Robinhood Multi-Agent Engine</span>
				</div>
			</a>
		</div>

		<div class="nav-right">
			<!-- Robinhood Account Pill & Switch Menu -->
			{#if rhAccount && rhAccount.isConnected}
				<div class="rh-dropdown-wrap">
					<button
						type="button"
						class="rh-indicator"
						onclick={() => showAccountMenu = !showAccountMenu}
					>
						<span class="rh-dot"></span>
						<span class="rh-text font-mono">{rhAccount.accountNumber}</span>
						<span class="chevron">▾</span>
					</button>

					{#if showAccountMenu}
						<div class="rh-menu-dropdown">
							<div class="rh-menu-header">
								<strong>{rhAccount.username ?? 'Robinhood Account'}</strong>
								<span class="text-muted font-mono">{rhAccount.accountNumber}</span>
								<span class="badge badge-success mt-1">
									Equity: ${rhAccount.totalEquity.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
								</span>
							</div>
							<div class="rh-menu-actions">
								<button
									type="button"
									class="dropdown-item"
									onclick={() => { showAccountMenu = false; onOpenConnect?.(); }}
								>
									🔄 Switch Account / Token
								</button>
								<button
									type="button"
									class="dropdown-item text-danger"
									onclick={() => { showAccountMenu = false; onDisconnect?.(); }}
								>
									🚪 Disconnect
								</button>
							</div>
						</div>
					{/if}
				</div>
			{:else}
				<button
					type="button"
					class="btn btn-primary btn-sm rh-connect-btn"
					onclick={() => onOpenConnect?.()}
				>
					<span>🪶 Connect Robinhood</span>
				</button>
			{/if}

			<div class="health-indicator">
				<span class="dot {isLoading ? 'dot-loading' : health?.status === 'Healthy' ? 'dot-online' : 'dot-offline'}"></span>
				<span class="health-text">
					{#if isLoading}
						Connecting...
					{:else if health?.status === 'Healthy'}
						MCP Live
					{:else}
						Degraded
					{/if}
				</span>
			</div>
		</div>
	</div>
</header>

<style>
	.navbar {
		background: var(--bg-surface);
		border-bottom: 1px solid var(--border-subtle);
		position: sticky;
		top: 0;
		z-index: 100;
		backdrop-filter: blur(12px);
	}

	.nav-content {
		display: flex;
		align-items: center;
		justify-content: space-between;
		height: 64px;
	}

	.nav-left {
		display: flex;
		align-items: center;
		gap: 1.75rem;
	}

	.brand {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		text-decoration: none;
	}

	.logo-icon {
		width: 34px;
		height: 34px;
		background: linear-gradient(135deg, var(--primary) 0%, #0284c7 100%);
		border-radius: var(--radius-md);
		display: flex;
		align-items: center;
		justify-content: center;
		font-size: 1.1rem;
		box-shadow: 0 0 14px var(--primary-subtle);
	}

	.brand-text {
		display: flex;
		flex-direction: column;
	}

	.brand-name {
		font-size: 1.1rem;
		font-weight: 700;
		color: var(--text-primary);
		line-height: 1.1;
	}

	.brand-tag {
		font-size: 0.68rem;
		font-family: var(--font-mono);
		color: #22c55e;
		text-transform: uppercase;
		letter-spacing: 0.05em;
		font-weight: 700;
	}

	.nav-right {
		display: flex;
		align-items: center;
		gap: 1rem;
	}

	.rh-dropdown-wrap {
		position: relative;
	}

	.rh-indicator {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		background: rgba(34, 197, 94, 0.1);
		border: 1px solid rgba(34, 197, 94, 0.3);
		padding: 0.35rem 0.75rem;
		border-radius: var(--radius-full);
		font-size: 0.78rem;
		font-weight: 700;
		color: #22c55e;
		cursor: pointer;
		transition: var(--transition);
	}

	.rh-indicator:hover {
		background: rgba(34, 197, 94, 0.2);
	}

	.rh-dot {
		width: 7px;
		height: 7px;
		border-radius: 50%;
		background: #22c55e;
		box-shadow: 0 0 8px #22c55e;
	}

	.chevron {
		font-size: 0.7rem;
		opacity: 0.8;
	}

	.rh-menu-dropdown {
		position: absolute;
		top: calc(100% + 8px);
		right: 0;
		background: var(--bg-surface-elevated);
		border: 1px solid var(--border-strong);
		border-radius: var(--radius-md);
		min-width: 220px;
		box-shadow: 0 10px 25px rgba(0, 0, 0, 0.4);
		display: flex;
		flex-direction: column;
		z-index: 1000;
		overflow: hidden;
	}

	.rh-menu-header {
		padding: 0.85rem 1rem;
		border-bottom: 1px solid var(--border-subtle);
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
		font-size: 0.82rem;
	}

	.rh-menu-actions {
		display: flex;
		flex-direction: column;
	}

	.dropdown-item {
		background: transparent;
		border: none;
		padding: 0.65rem 1rem;
		text-align: left;
		font-size: 0.82rem;
		color: var(--text-primary);
		cursor: pointer;
		transition: var(--transition);
	}

	.dropdown-item:hover {
		background: var(--bg-surface);
	}

	.rh-connect-btn {
		font-weight: 700;
		background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%);
		border-color: #22c55e;
	}

	.health-indicator {
		display: flex;
		align-items: center;
		gap: 0.5rem;
		background: var(--bg-canvas);
		padding: 0.35rem 0.75rem;
		border-radius: var(--radius-full);
		border: 1px solid var(--border-subtle);
		font-size: 0.8rem;
		font-weight: 500;
		color: var(--text-secondary);
	}

	.font-mono { font-family: var(--font-mono); }
	.mt-1 { margin-top: 0.25rem; }
	.text-danger { color: var(--danger); }
	.text-muted { color: var(--text-muted); }
</style>
