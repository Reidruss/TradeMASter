<script lang="ts">
	import { onMount } from 'svelte';
	import { healthService } from '$lib/api';
	import type { HealthInfo } from '$lib/api';

	let health = $state<HealthInfo | null>(null);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let latency = $state<number | null>(null);
	let lastChecked = $state<Date | null>(null);

	async function checkHealth() {
		loading = true;
		error = null;
		const start = performance.now();
		try {
			const res = await healthService.getHealth();
			health = res;
			latency = Math.round(performance.now() - start);
			lastChecked = new Date();
		} catch (err: any) {
			error = err?.message || 'Failed to connect to backend';
			health = null;
			latency = null;
		} finally {
			loading = false;
		}
	}

	onMount(() => {
		checkHealth();
	});
</script>

<div class="status-card card">
	<div class="status-header">
		<div class="status-indicator">
			{#if loading}
				<span class="dot dot-loading"></span>
				<span class="status-label">Checking Backend Connection...</span>
			{:else if error}
				<span class="dot dot-offline"></span>
				<span class="status-label status-error">Backend Offline / Unreachable</span>
			{:else}
				<span class="dot dot-online"></span>
				<span class="status-label status-healthy">ASP.NET Core API Connected</span>
			{/if}
		</div>

		<div class="status-actions">
			<button
				class="btn btn-secondary btn-sm"
				onclick={checkHealth}
				disabled={loading}
				aria-label="Ping backend health endpoint"
			>
				{#if loading}
					<span class="spinner"></span>
					<span>Pinging...</span>
				{:else}
					<span>↻ Ping Backend</span>
				{/if}
			</button>
		</div>
	</div>

	{#if health}
		<div class="status-metrics">
			<div class="metric">
				<span class="metric-title">Framework</span>
				<span class="metric-value">{health.frameworkVersion}</span>
			</div>
			<div class="metric">
				<span class="metric-title">Environment</span>
				<span class="badge badge-primary">{health.environment}</span>
			</div>
			<div class="metric">
				<span class="metric-title">Latency</span>
				<span class="metric-value font-mono">{latency}ms</span>
			</div>
			<div class="metric">
				<span class="metric-title">Server Uptime</span>
				<span class="metric-value font-mono">{health.uptime.split('.')[0]}</span>
			</div>
		</div>
	{:else if error}
		<div class="error-box">
			<p class="error-text">⚠️ <strong>Error:</strong> {error}</p>
			<p class="error-hint">
				Make sure the .NET backend is running on <code>http://localhost:5126</code> (or run <code>npm run dev</code> from the project root).
			</p>
		</div>
	{/if}
</div>

<style>
	.status-card {
		background: linear-gradient(180deg, var(--bg-surface) 0%, rgba(18, 24, 38, 0.6) 100%);
		border: 1px solid var(--border-subtle);
		margin-bottom: 2rem;
	}

	.status-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
		flex-wrap: wrap;
	}

	.status-indicator {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.status-label {
		font-weight: 600;
		font-size: 0.95rem;
	}

	.status-healthy {
		color: var(--success);
	}

	.status-error {
		color: var(--danger);
	}

	.status-metrics {
		display: grid;
		grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
		gap: 1.25rem;
		margin-top: 1.25rem;
		padding-top: 1.25rem;
		border-top: 1px solid var(--border-subtle);
	}

	.metric {
		display: flex;
		flex-direction: column;
		gap: 0.25rem;
	}

	.metric-title {
		font-size: 0.75rem;
		text-transform: uppercase;
		letter-spacing: 0.05em;
		color: var(--text-muted);
		font-weight: 600;
	}

	.metric-value {
		font-size: 0.9rem;
		color: var(--text-primary);
		font-weight: 500;
	}

	.font-mono {
		font-family: var(--font-mono);
	}

	.error-box {
		margin-top: 1rem;
		padding: 1rem;
		background: var(--danger-subtle);
		border: 1px solid rgba(248, 113, 113, 0.3);
		border-radius: var(--radius-md);
	}

	.error-text {
		color: var(--danger);
		font-size: 0.9rem;
		margin-bottom: 0.35rem;
	}

	.error-hint {
		color: var(--text-secondary);
		font-size: 0.85rem;
	}

	.error-hint code {
		background: rgba(0, 0, 0, 0.3);
		padding: 0.15rem 0.35rem;
		border-radius: 4px;
		color: var(--text-primary);
	}
</style>
