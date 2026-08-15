<script lang="ts">
	import { api } from '$lib/api';

	interface EndpointOption {
		name: string;
		method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
		url: string;
		body?: string;
	}

	const endpoints: EndpointOption[] = [
		{ name: 'Get Backend Health', method: 'GET', url: '/api/health' },
		{ name: 'Get Weather Forecast (5 days)', method: 'GET', url: '/api/weather/forecast?days=5' },
		{ name: 'Get All Todos', method: 'GET', url: '/api/todos' },
		{
			name: 'Create Todo Item',
			method: 'POST',
			url: '/api/todos',
			body: JSON.stringify({ title: 'New task via API Tester', description: 'Tested from frontend runner' }, null, 2)
		}
	];

	let selectedIndex = $state(0);
	let currentMethod = $state<'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'>('GET');
	let currentUrl = $state('/api/health');
	let requestBody = $state('');

	let executing = $state(false);
	let responseStatus = $state<number | null>(null);
	let responseTime = $state<number | null>(null);
	let responseData = $state<string | null>(null);
	let isError = $state(false);

	function selectEndpoint(index: number) {
		selectedIndex = index;
		const ep = endpoints[index];
		currentMethod = ep.method;
		currentUrl = ep.url;
		requestBody = ep.body || '';
	}

	async function executeRequest() {
		executing = true;
		responseStatus = null;
		responseTime = null;
		responseData = null;
		isError = false;

		const start = performance.now();
		try {
			let parsedBody = undefined;
			if (requestBody && (currentMethod === 'POST' || currentMethod === 'PUT' || currentMethod === 'PATCH')) {
				parsedBody = JSON.parse(requestBody);
			}

			let result: any;
			if (currentMethod === 'GET') {
				result = await api.get(currentUrl);
			} else if (currentMethod === 'POST') {
				result = await api.post(currentUrl, parsedBody);
			} else if (currentMethod === 'PUT') {
				result = await api.put(currentUrl, parsedBody);
			} else if (currentMethod === 'PATCH') {
				result = await api.patch(currentUrl, parsedBody);
			} else if (currentMethod === 'DELETE') {
				result = await api.delete(currentUrl);
			}

			responseTime = Math.round(performance.now() - start);
			responseStatus = 200;
			responseData = JSON.stringify(result, null, 2);
		} catch (err: any) {
			responseTime = Math.round(performance.now() - start);
			responseStatus = err.status || 500;
			isError = true;
			responseData = JSON.stringify(err.data || { error: err.message }, null, 2);
		} finally {
			executing = false;
		}
	}
</script>

<div class="api-tester card">
	<div class="header">
		<div>
			<h3>Interactive API Console</h3>
			<p>Directly test any ASP.NET Core endpoint with real-time response output</p>
		</div>
	</div>

	<div class="quick-presets">
		{#each endpoints as ep, i}
			<button
				class="preset-btn"
				class:active={selectedIndex === i}
				onclick={() => selectEndpoint(i)}
			>
				<span class="method-badge method-{ep.method.toLowerCase()}">{ep.method}</span>
				<span class="preset-name">{ep.name}</span>
			</button>
		{/each}
	</div>

	<div class="request-bar">
		<span class="method-tag method-{currentMethod.toLowerCase()}">{currentMethod}</span>
		<input type="text" class="input url-input" bind:value={currentUrl} />
		<button class="btn btn-primary" onclick={executeRequest} disabled={executing}>
			{#if executing}
				<span class="spinner"></span>
				<span>Sending...</span>
			{:else}
				<span>▶ Send Request</span>
			{/if}
		</button>
	</div>

	{#if currentMethod === 'POST' || currentMethod === 'PUT' || currentMethod === 'PATCH'}
		<div class="body-editor">
			<label for="body-input" class="editor-label">Request Body (JSON)</label>
			<textarea id="body-input" class="textarea font-mono" rows="3" bind:value={requestBody}></textarea>
		</div>
	{/if}

	{#if responseData !== null}
		<div class="response-section">
			<div class="response-meta">
				<div class="response-status">
					<span>Status:</span>
					<span class="badge {isError ? 'badge-danger' : 'badge-success'}">
						{responseStatus || (isError ? 'Error' : '200 OK')}
					</span>
				</div>
				{#if responseTime !== null}
					<div class="response-time">
						<span>Latency:</span>
						<span class="badge badge-primary">{responseTime} ms</span>
					</div>
				{/if}
			</div>

			<pre class="code-block response-body">{responseData}</pre>
		</div>
	{/if}
</div>

<style>
	.header {
		margin-bottom: 1.25rem;
	}

	.quick-presets {
		display: flex;
		gap: 0.5rem;
		flex-wrap: wrap;
		margin-bottom: 1.25rem;
	}

	.preset-btn {
		display: flex;
		align-items: center;
		gap: 0.4rem;
		padding: 0.4rem 0.75rem;
		background: var(--bg-canvas);
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-md);
		color: var(--text-secondary);
		cursor: pointer;
		font-size: 0.8rem;
		transition: var(--transition);
	}

	.preset-btn:hover {
		border-color: var(--border-strong);
		color: var(--text-primary);
	}

	.preset-btn.active {
		border-color: var(--primary);
		background: var(--primary-subtle);
		color: var(--text-primary);
	}

	.method-badge, .method-tag {
		font-family: var(--font-mono);
		font-size: 0.7rem;
		font-weight: 700;
		padding: 0.15rem 0.4rem;
		border-radius: 4px;
	}

	.method-get { background: rgba(56, 189, 248, 0.2); color: #38bdf8; }
	.method-post { background: rgba(52, 211, 153, 0.2); color: #34d399; }
	.method-put { background: rgba(251, 191, 36, 0.2); color: #fbbf24; }
	.method-patch { background: rgba(167, 139, 250, 0.2); color: #a78bfa; }
	.method-delete { background: rgba(248, 113, 113, 0.2); color: #f87171; }

	.request-bar {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		margin-bottom: 1rem;
	}

	.url-input {
		font-family: var(--font-mono);
		font-size: 0.85rem;
	}

	.body-editor {
		margin-bottom: 1rem;
		display: flex;
		flex-direction: column;
		gap: 0.35rem;
	}

	.editor-label {
		font-size: 0.75rem;
		color: var(--text-muted);
		text-transform: uppercase;
		letter-spacing: 0.04em;
	}

	.response-section {
		margin-top: 1.25rem;
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.response-meta {
		display: flex;
		align-items: center;
		gap: 1.5rem;
		font-size: 0.85rem;
		color: var(--text-secondary);
	}

	.response-status, .response-time {
		display: flex;
		align-items: center;
		gap: 0.4rem;
	}

	.response-body {
		max-height: 280px;
		overflow-y: auto;
	}

	@media (max-width: 768px) {
		.request-bar {
			flex-direction: column;
			align-items: stretch;
		}
	}
</style>
