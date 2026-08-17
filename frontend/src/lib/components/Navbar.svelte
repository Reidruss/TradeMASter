<script lang="ts">
	import { onMount } from 'svelte';
	import { healthService } from '$lib/api';
	import type { HealthInfo } from '$lib/api';

	let health = $state<HealthInfo | null>(null);
	let isLoading = $state(true);

	onMount(() => {
		let isMounted = true;

		async function checkHealth() {
			try {
				const res = await healthService.getHealth();
				if (isMounted) health = res;
			} catch {
				// keep state
			} finally {
				if (isMounted) isLoading = false;
			}
		}

		checkHealth();
		const interval = setInterval(checkHealth, 15000);

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
					<span class="brand-tag">Phase 1 Foundation</span>
				</div>
			</a>

			<nav class="nav-links">
				<a href="/" class="nav-link">Dashboard</a>
				<a href="/market" class="nav-link">Market & Charts</a>
				<a href="/portfolio" class="nav-link">Portfolio & Blotter</a>
				<a href="/agents" class="nav-link">Agent Committee</a>
				<a href="/docs" class="nav-link">API & Docs</a>
			</nav>
		</div>

		<div class="nav-right">
			<div class="health-indicator">
				<span class="dot {isLoading ? 'dot-loading' : health?.status === 'Healthy' ? 'dot-online' : 'dot-offline'}"></span>
				<span class="health-text">
					{#if isLoading}
						Connecting...
					{:else if health?.status === 'Healthy'}
						System Live
					{:else}
						Degraded
					{/if}
				</span>
			</div>

			<a href="/scalar/v1" target="_blank" rel="noreferrer" class="btn btn-secondary btn-sm scalar-btn">
				<span>Scalar Docs</span>
				<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
					<path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"></path>
					<polyline points="15 3 21 3 21 9"></polyline>
					<line x1="10" y1="14" x2="21" y2="3"></line>
				</svg>
			</a>
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
		gap: 2.5rem;
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
		color: var(--primary);
		text-transform: uppercase;
		letter-spacing: 0.05em;
	}

	.nav-links {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.nav-link {
		color: var(--text-secondary);
		padding: 0.45rem 0.8rem;
		border-radius: var(--radius-sm);
		font-size: 0.88rem;
		font-weight: 500;
		transition: var(--transition);
	}

	.nav-link:hover {
		color: var(--text-primary);
		background: var(--bg-surface-elevated);
	}

	.nav-right {
		display: flex;
		align-items: center;
		gap: 1.25rem;
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

	.scalar-btn {
		display: inline-flex;
		align-items: center;
		gap: 0.35rem;
	}

	@media (max-width: 840px) {
		.nav-links {
			display: none;
		}
	}
</style>
