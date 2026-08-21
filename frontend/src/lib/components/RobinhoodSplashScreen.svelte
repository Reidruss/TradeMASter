<script lang="ts">
	import { onMount } from 'svelte';
	import { robinhoodService, type RobinhoodAccountInfo, type SavedRobinhoodSessionDto } from '$lib/api';

	let { onConnected }: { onConnected: (account: RobinhoodAccountInfo) => void } = $props();

	let activeTab = $state<'oauth' | 'token' | 'sandbox'>('oauth');
	let bearerToken = $state('');

	let isConnecting = $state(false);
	let errorMessage = $state<string | null>(null);
	let savedSession = $state<SavedRobinhoodSessionDto | null>(null);

	onMount(async () => {
		try {
			savedSession = await robinhoodService.getSavedSession();
		} catch (err) {
			console.debug('No saved session found', err);
		}
	});

	async function handleLaunchOAuth() {
		isConnecting = true;
		errorMessage = null;
		try {
			const redirectUri = `${window.location.origin}/auth/callback`;
			const res = await robinhoodService.getOAuthUrl(redirectUri);

			// Redirect user to Robinhood authorization portal
			window.location.href = res.authorizationUrl;
		} catch (err: any) {
			errorMessage = err.message || 'Failed to start Robinhood OAuth flow.';
			isConnecting = false;
		}
	}

	async function handleQuickReconnect() {
		if (!savedSession?.hasSavedSession) return;
		isConnecting = true;
		errorMessage = null;
		try {
			const res = await robinhoodService.getStatus();
			if (!res.isConnected) throw new Error(res.statusMessage);
			onConnected(res);
		} catch (err: any) {
			errorMessage = err.message || 'Failed to reconnect saved account';
		} finally {
			isConnecting = false;
		}
	}

	async function handleConnect(e: Event) {
		e.preventDefault();
		isConnecting = true;
		errorMessage = null;

		try {
			let req;
			if (activeTab === 'token') {
				if (!bearerToken.trim()) {
					errorMessage = 'Please enter your Robinhood API / Bearer token.';
					isConnecting = false;
					return;
				}
				req = {
					bearerToken: bearerToken.trim(),
					rememberMe: true,
					useDemoMode: false
				};
			} else {
				req = {
					rememberMe: true,
					useDemoMode: true
				};
			}

			const res = await robinhoodService.connect(req);
			onConnected(res);
		} catch (err: any) {
			errorMessage = err.message || 'Failed to connect to Robinhood. Please verify your credentials.';
		} finally {
			isConnecting = false;
		}
	}
</script>

<div class="splash-overlay">
	<div class="splash-container">
		<!-- Brand & Header -->
		<div class="splash-brand">
			<div class="feather-logo">🪶</div>
			<div class="brand-text">
				<span class="sub-pill">ROBINHOOD AGENTIC TRADING MCP</span>
				<h2>Connect your Robinhood Agentic Account</h2>
				<p>Read your holdings through Robinhood MCP and generate human-reviewed, risk-checked portfolio research and paper rebalance plans.</p>
			</div>
		</div>

		<!-- Saved Session Quick-Reconnect Banner -->
		{#if savedSession?.hasSavedSession}
			<div class="saved-session-banner">
				<div class="saved-info">
					<span class="dot dot-online"></span>
					<div class="saved-text">
						<strong>Saved Account: {savedSession.accountNumber ?? 'Robinhood Main'}</strong>
						<span class="text-muted">Last active: {savedSession.lastConnectedAtUtc ? new Date(savedSession.lastConnectedAtUtc).toLocaleDateString() : 'Recent'}</span>
					</div>
				</div>
				<button
					type="button"
					class="btn btn-primary btn-sm quick-btn"
					onclick={handleQuickReconnect}
					disabled={isConnecting}
				>
					{#if isConnecting}
						<span class="spinner"></span>
						<span>Reconnecting...</span>
					{:else}
						<span>⚡ Quick Connect</span>
					{/if}
				</button>
			</div>
		{/if}

		<!-- Connection Pad Card -->
		<div class="card connect-card">
			<div class="tab-headers">
				<button
					type="button"
					class="tab-btn {activeTab === 'oauth' ? 'active' : ''}"
					onclick={() => { activeTab = 'oauth'; errorMessage = null; }}
				>
					🪶 Official Robinhood OAuth
				</button>
				<button
					type="button"
					class="tab-btn {activeTab === 'token' ? 'active' : ''}"
					onclick={() => { activeTab = 'token'; errorMessage = null; }}
				>
					🔑 API Token
				</button>
				<button
					type="button"
					class="tab-btn {activeTab === 'sandbox' ? 'active' : ''}"
					onclick={() => { activeTab = 'sandbox'; errorMessage = null; }}
				>
					🧪 Demo Sandbox
				</button>
			</div>

			{#if activeTab === 'oauth'}
				<div class="oauth-section">
					<div class="oauth-badge-row">
						<span class="oauth-endpoint-tag font-mono">MCP: agent.robinhood.com/mcp/trading</span>
					</div>
					<h3>Official Agentic Account Sign In</h3>
					<p class="oauth-desc">Authenticates through Robinhood's PKCE OAuth protocol. TradeMASter reads your Agentic account holdings through MCP; order execution remains paper-only unless live trading is explicitly implemented and enabled.</p>

					<button
						type="button"
						class="btn btn-primary oauth-primary-btn"
						onclick={handleLaunchOAuth}
						disabled={isConnecting}
					>
						{#if isConnecting}
							<span class="spinner"></span>
							<span>Opening Robinhood Authorization Portal...</span>
						{:else}
							<span>🪶 Sign In with Robinhood</span>
						{/if}
					</button>
				</div>
			{:else}
				<form class="auth-form" onsubmit={handleConnect}>
					{#if activeTab === 'token'}
						<div class="form-group">
							<label for="rh-token">Robinhood Bearer / API Access Token</label>
							<input
								id="rh-token"
								type="password"
								class="input font-mono"
								bind:value={bearerToken}
								placeholder="e.g. bearer_oauth2_..."
								required
							/>
							<span class="input-hint">Your token is encrypted and stored locally on your server for fast login.</span>
						</div>
					{:else}
						<div class="sandbox-info">
							<h4>Live Market Sandbox Mode</h4>
							<p>Connects an authentic Robinhood portfolio simulation pre-populated with live stocks & crypto (NVDA, AAPL, MSFT, TSLA, BTC). Evaluates against live Yahoo Finance & real-time market prices.</p>
						</div>
					{/if}

					{#if errorMessage}
						<div class="error-banner">
							⚠️ {errorMessage}
						</div>
					{/if}

					<button type="submit" class="btn btn-primary connect-submit-btn" disabled={isConnecting}>
						{#if isConnecting}
							<span class="spinner"></span>
							<span>Authenticating & Synchronizing Portfolio...</span>
						{:else}
							<span>⚡ Connect & Launch TradeMASter</span>
						{/if}
					</button>
				</form>
			{/if}
		</div>

		<!-- Feature Highlights Strip -->
		<div class="feature-strip">
			<div class="feature-item">
				<span class="feat-icon">🤖</span>
				<div class="feat-text">
					<strong>5-Agent Committee</strong>
					<span>Technical, Fundamental, Sentiment, Risk & Arbiter.</span>
				</div>
			</div>
			<div class="feature-item">
				<span class="feat-icon">🗓️</span>
				<div class="feat-text">
					<strong>Bi-Weekly Rebalancer</strong>
					<span>Automated 14-day risk-parity weight adjustments.</span>
				</div>
			</div>
			<div class="feature-item">
				<span class="feat-icon">🛡️</span>
				<div class="feat-text">
					<strong>Risk Guard Firewall</strong>
					<span>Hard position caps, 1.5x ATR stops & veto protection.</span>
				</div>
			</div>
		</div>
	</div>
</div>

<style>
	.splash-overlay {
		position: fixed;
		top: 0;
		left: 0;
		width: 100vw;
		height: 100vh;
		background: radial-gradient(circle at 50% 30%, #111827 0%, #030712 100%);
		z-index: 9999;
		display: flex;
		align-items: center;
		justify-content: center;
		padding: 1.5rem;
		overflow-y: auto;
	}

	.splash-container {
		max-width: 680px;
		width: 100%;
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
		animation: fadeIn 0.3s ease-out;
	}

	@keyframes fadeIn {
		from { opacity: 0; transform: translateY(12px); }
		to { opacity: 1; transform: translateY(0); }
	}

	.splash-brand {
		text-align: center;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.75rem;
	}

	.feather-logo {
		width: 58px;
		height: 58px;
		background: linear-gradient(135deg, #22c55e 0%, #15803d 100%);
		border-radius: var(--radius-lg);
		display: flex;
		align-items: center;
		justify-content: center;
		font-size: 1.8rem;
		box-shadow: 0 0 24px rgba(34, 197, 94, 0.4);
	}

	.sub-pill {
		font-size: 0.68rem;
		font-family: var(--font-mono);
		font-weight: 800;
		color: #22c55e;
		background: rgba(34, 197, 94, 0.12);
		border: 1px solid rgba(34, 197, 94, 0.3);
		padding: 0.2rem 0.6rem;
		border-radius: var(--radius-full);
		letter-spacing: 0.06em;
	}

	.brand-text h2 {
		font-size: 1.75rem;
		font-weight: 800;
		color: var(--text-primary);
		margin-top: 0.3rem;
	}

	.brand-text p {
		font-size: 0.9rem;
		color: var(--text-secondary);
		max-width: 540px;
		margin: 0 auto;
		line-height: 1.45;
	}

	.saved-session-banner {
		background: var(--bg-surface-elevated);
		border: 1px solid var(--primary-subtle);
		border-left: 4px solid var(--primary);
		padding: 0.85rem 1.25rem;
		border-radius: var(--radius-md);
		display: flex;
		align-items: center;
		justify-content: space-between;
		gap: 1rem;
	}

	.saved-info {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.saved-text {
		display: flex;
		flex-direction: column;
		font-size: 0.85rem;
	}

	.quick-btn {
		white-space: nowrap;
	}

	.connect-card {
		background: var(--bg-surface);
		border: 1px solid var(--border-strong);
		padding: 1.75rem;
		display: flex;
		flex-direction: column;
		gap: 1.25rem;
		box-shadow: 0 20px 40px rgba(0, 0, 0, 0.4);
	}

	.tab-headers {
		display: flex;
		background: var(--bg-canvas);
		padding: 0.25rem;
		border-radius: var(--radius-md);
		border: 1px solid var(--border-subtle);
		gap: 0.25rem;
		overflow-x: auto;
	}

	.tab-btn {
		flex: 1;
		white-space: nowrap;
		background: transparent;
		border: none;
		color: var(--text-secondary);
		padding: 0.5rem 0.65rem;
		font-size: 0.8rem;
		font-weight: 600;
		border-radius: var(--radius-sm);
		cursor: pointer;
		transition: var(--transition);
	}

	.tab-btn:hover {
		color: var(--text-primary);
	}

	.tab-btn.active {
		background: var(--bg-surface-elevated);
		color: #22c55e;
		font-weight: 700;
		box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
	}

	.oauth-section {
		display: flex;
		flex-direction: column;
		align-items: center;
		text-align: center;
		gap: 0.85rem;
		padding: 1rem 0;
	}

	.oauth-endpoint-tag {
		background: var(--bg-canvas);
		border: 1px solid var(--border-subtle);
		padding: 0.25rem 0.65rem;
		border-radius: var(--radius-sm);
		font-size: 0.72rem;
		color: var(--text-secondary);
	}

	.oauth-section h3 {
		font-size: 1.25rem;
		font-weight: 700;
		color: var(--text-primary);
	}

	.oauth-desc {
		font-size: 0.88rem;
		color: var(--text-secondary);
		max-width: 480px;
		line-height: 1.45;
	}

	.oauth-primary-btn {
		width: 100%;
		max-width: 440px;
		padding: 0.95rem 1.5rem;
		font-size: 1rem;
		font-weight: 700;
		background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%);
		border-color: #22c55e;
		margin-top: 0.5rem;
		box-shadow: 0 0 20px rgba(34, 197, 94, 0.3);
	}

	.auth-form {
		display: flex;
		flex-direction: column;
		gap: 1rem;
	}

	.form-group {
		display: flex;
		flex-direction: column;
		gap: 0.4rem;
	}

	label {
		font-size: 0.8rem;
		font-weight: 600;
		color: var(--text-secondary);
	}

	.input-hint {
		font-size: 0.72rem;
		color: var(--text-muted);
	}

	.sandbox-info {
		background: var(--bg-canvas);
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-md);
		padding: 1rem;
		display: flex;
		flex-direction: column;
		gap: 0.35rem;
	}

	.sandbox-info h4 {
		color: #22c55e;
		font-size: 0.95rem;
	}

	.sandbox-info p {
		font-size: 0.85rem;
		color: var(--text-secondary);
		line-height: 1.45;
	}

	.error-banner {
		background: var(--danger-subtle);
		border: 1px solid rgba(248, 113, 113, 0.3);
		color: var(--danger);
		padding: 0.65rem 0.85rem;
		border-radius: var(--radius-md);
		font-size: 0.82rem;
	}

	.connect-submit-btn {
		padding: 0.85rem;
		font-size: 0.95rem;
		font-weight: 700;
		background: linear-gradient(135deg, #22c55e 0%, #16a34a 100%);
		border-color: #22c55e;
	}

	.connect-submit-btn:hover {
		background: linear-gradient(135deg, #16a34a 0%, #15803d 100%);
	}

	.feature-strip {
		display: grid;
		grid-template-columns: repeat(3, 1fr);
		gap: 0.75rem;
	}

	.feature-item {
		background: var(--bg-surface);
		border: 1px solid var(--border-subtle);
		padding: 0.75rem 0.85rem;
		border-radius: var(--radius-md);
		display: flex;
		align-items: center;
		gap: 0.6rem;
	}

	.feat-icon {
		font-size: 1.3rem;
	}

	.feat-text {
		display: flex;
		flex-direction: column;
		font-size: 0.75rem;
	}

	.feat-text strong {
		color: var(--text-primary);
	}

	.feat-text span {
		color: var(--text-muted);
		font-size: 0.68rem;
	}

	.font-mono { font-family: var(--font-mono); }
	.text-muted { color: var(--text-muted); }
</style>
