<div class="arch-card card">
	<h3>Template Communication Architecture</h3>
	<p class="arch-sub">How SvelteKit & ASP.NET Core communicate in development & production</p>

	<div class="flow-diagram">
		<div class="flow-step">
			<div class="step-header">
				<span class="step-num">1</span>
				<span class="step-title">SvelteKit Frontend</span>
			</div>
			<p class="step-desc">Svelte 5 runes (<code>$state</code>), typed services in <code>$lib/api</code> calling <code>/api/*</code>.</p>
		</div>

		<div class="flow-arrow">➔</div>

		<div class="flow-step">
			<div class="step-header">
				<span class="step-num">2</span>
				<span class="step-title">Vite Dev Proxy</span>
			</div>
			<p class="step-desc">Intercepts <code>/api</code> requests & forwards to <code>localhost:5126</code> (zero CORS friction).</p>
		</div>

		<div class="flow-arrow">➔</div>

		<div class="flow-step">
			<div class="step-header">
				<span class="step-num">3</span>
				<span class="step-title">ASP.NET Core API</span>
			</div>
			<p class="step-desc">Minimal API endpoints, Scalar/OpenAPI docs, Dependency Injection, JSON serialization.</p>
		</div>
	</div>

	<div class="quick-commands">
		<div class="command-row">
			<span class="cmd-label">Start full-stack dev:</span>
			<code class="cmd-code">npm run dev</code>
		</div>
		<div class="command-row">
			<span class="cmd-label">OpenAPI Documentation:</span>
			<code class="cmd-code">http://localhost:5173/scalar/v1</code>
		</div>
	</div>
</div>

<style>
	.arch-card {
		background: var(--bg-surface);
	}

	.arch-sub {
		margin-top: 0.25rem;
		margin-bottom: 1.5rem;
		font-size: 0.9rem;
	}

	.flow-diagram {
		display: grid;
		grid-template-columns: 1fr auto 1fr auto 1fr;
		align-items: center;
		gap: 1rem;
		margin-bottom: 1.5rem;
	}

	.flow-step {
		background: var(--bg-canvas);
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-md);
		padding: 1rem;
		height: 100%;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.step-header {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.step-num {
		width: 1.4rem;
		height: 1.4rem;
		border-radius: 50%;
		background: var(--primary-subtle);
		color: var(--primary);
		font-size: 0.75rem;
		font-weight: 700;
		display: flex;
		align-items: center;
		justify-content: center;
	}

	.step-title {
		font-weight: 600;
		font-size: 0.9rem;
		color: var(--text-primary);
	}

	.step-desc {
		font-size: 0.8rem;
		color: var(--text-secondary);
		line-height: 1.4;
	}

	.step-desc code {
		background: rgba(0, 0, 0, 0.4);
		padding: 0.1rem 0.3rem;
		border-radius: 4px;
		color: var(--primary);
	}

	.flow-arrow {
		color: var(--text-muted);
		font-size: 1.25rem;
		font-weight: bold;
	}

	.quick-commands {
		display: flex;
		flex-direction: column;
		gap: 0.6rem;
		background: #080b11;
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-md);
		padding: 0.85rem 1.1rem;
	}

	.command-row {
		display: flex;
		align-items: center;
		justify-content: space-between;
		font-size: 0.85rem;
		flex-wrap: wrap;
		gap: 0.5rem;
	}

	.cmd-label {
		color: var(--text-secondary);
	}

	.cmd-code {
		font-family: var(--font-mono);
		color: var(--primary);
		background: rgba(56, 189, 248, 0.1);
		padding: 0.2rem 0.5rem;
		border-radius: 4px;
	}

	@media (max-width: 768px) {
		.flow-diagram {
			grid-template-columns: 1fr;
		}

		.flow-arrow {
			display: none;
		}
	}
</style>
