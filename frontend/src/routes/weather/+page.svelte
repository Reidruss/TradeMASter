<script lang="ts">
	import WeatherWidget from '$lib/components/WeatherWidget.svelte';
</script>

<svelte:head>
	<title>Weather Forecast — TradeMASter</title>
	<meta name="description" content="Weather forecast API example querying .NET Minimal API endpoint" />
</svelte:head>

<div class="page-container">
	<div class="page-header">
		<h1>Weather Forecast Endpoint</h1>
		<p>Demonstrates querying parameterized GET endpoints from the ASP.NET Core backend.</p>
	</div>

	<WeatherWidget />

	<div class="card doc-callout">
		<h3>Endpoint Details:</h3>
		<p class="mb-2">
			The endpoint is defined in <code>backend/TradeMASter.Api/Endpoints/WeatherEndpoints.cs</code> and supports a <code>days</code> query parameter:
		</p>
		<pre class="code-block"><code>GET /api/weather/forecast?days=7</code></pre>
	</div>
</div>

<style>
	.page-container {
		display: flex;
		flex-direction: column;
		gap: 2rem;
		max-width: 800px;
		margin: 0 auto;
	}

	.page-header {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
	}

	.doc-callout {
		background: var(--bg-surface);
	}

	.doc-callout h3 {
		margin-bottom: 0.75rem;
		font-size: 1.1rem;
	}

	.mb-2 {
		margin-bottom: 0.75rem;
	}

	.doc-callout code {
		font-family: var(--font-mono);
		color: var(--primary);
	}
</style>
