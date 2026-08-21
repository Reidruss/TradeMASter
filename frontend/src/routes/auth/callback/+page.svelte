<script lang="ts">
	import { onMount } from 'svelte';
	import { robinhoodService } from '$lib/api';

	let status = $state<'processing' | 'success' | 'error'>('processing');
	let errorMessage = $state<string | null>(null);

	onMount(async () => {
		const params = new URLSearchParams(window.location.search);
		const code = params.get('code');
		const state = params.get('state');

		if (!code) {
			status = 'error';
			errorMessage = 'No authorization code returned from Robinhood.';
			return;
		}

		try {
			await robinhoodService.exchangeOAuthCode({
				code,
				state: state ?? ''
			});
			status = 'success';

			setTimeout(() => {
				// A full navigation remounts the shared layout so the Navbar reads
				// the newly persisted OAuth session instead of retaining stale state.
				window.location.replace('/');
			}, 1200);
		} catch (err: any) {
			status = 'error';
			errorMessage = err.message || 'Failed to exchange authorization code with Robinhood.';
		}
	});
</script>

<svelte:head>
	<title>Authenticating Robinhood Agent • TradeMASter</title>
</svelte:head>

<div class="callback-container">
	<div class="card callback-card">
		<div class="brand-badge">🪶 ROBINHOOD AGENTIC MCP</div>

		{#if status === 'processing'}
			<div class="spinner-wrap">
				<div class="spinner-large"></div>
			</div>
			<h3>Authenticating your Agentic Account...</h3>
			<p>Linking your Robinhood portfolio session with TradeMASter AI agents and verifying permissions.</p>
		{:else if status === 'success'}
			<div class="success-icon">✅</div>
			<h3 class="text-success">Agentic Account Connected!</h3>
			<p>Your Robinhood investments are now synchronized. Returning to the dashboard...</p>
		{:else}
			<div class="error-icon">⚠️</div>
			<h3 class="text-danger">Authentication Failed</h3>
			<p>{errorMessage}</p>
			<a href="/" class="btn btn-secondary mt-2">Return to TradeMASter</a>
		{/if}
	</div>
</div>

<style>
	.callback-container {
		min-height: 70vh;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 2rem;
	}

	.callback-card {
		max-width: 520px;
		width: 100%;
		text-align: center;
		padding: 2.5rem;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 1rem;
		background: var(--bg-surface);
		border: 1px solid var(--border-strong);
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4);
	}

	.brand-badge {
		font-size: 0.72rem;
		font-weight: 800;
		color: #22c55e;
		background: rgba(34, 197, 94, 0.12);
		border: 1px solid rgba(34, 197, 94, 0.3);
		padding: 0.25rem 0.75rem;
		border-radius: var(--radius-full);
		letter-spacing: 0.05em;
	}

	.spinner-wrap {
		padding: 1.5rem 0;
	}

	.spinner-large {
		width: 48px;
		height: 48px;
		border: 4px solid var(--border-subtle);
		border-top-color: #22c55e;
		border-radius: 50%;
		animation: spin 0.8s linear infinite;
	}

	@keyframes spin {
		to { transform: rotate(360deg); }
	}

	.success-icon, .error-icon {
		font-size: 3rem;
	}

	.text-success { color: var(--success); }
	.text-danger { color: var(--danger); }
	.mt-2 { margin-top: 0.5rem; }
</style>
