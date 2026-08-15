<script lang="ts">
	import { onMount } from 'svelte';
	import { weatherService } from '$lib/api';
	import type { WeatherForecast } from '$lib/api';

	let forecasts = $state<WeatherForecast[]>([]);
	let days = $state(5);
	let unit = $state<'C' | 'F'>('C');
	let loading = $state(true);
	let error = $state<string | null>(null);

	async function loadWeather() {
		loading = true;
		error = null;
		try {
			forecasts = await weatherService.getForecast(days);
		} catch (err: any) {
			error = err?.message || 'Failed to load weather forecast';
		} finally {
			loading = false;
		}
	}

	function getWeatherEmoji(summary: string | null): string {
		if (!summary) return '🌤️';
		const s = summary.toLowerCase();
		if (s.includes('freez') || s.includes('snow')) return '❄️';
		if (s.includes('rain') || s.includes('drizzle')) return '🌧️';
		if (s.includes('chilly') || s.includes('bracing') || s.includes('cool')) return '🌬️';
		if (s.includes('mild') || s.includes('balmy')) return '⛅';
		if (s.includes('warm') || s.includes('hot') || s.includes('scorch') || s.includes('swelter')) return '☀️';
		return '🌤️';
	}

	onMount(() => {
		loadWeather();
	});
</script>

<div class="weather-widget card">
	<div class="widget-header">
		<div>
			<h3>Weather Forecast Stream</h3>
			<p>Testing HTTP GET communication from <code>/api/weather/forecast</code></p>
		</div>

		<div class="controls">
			<div class="unit-toggle">
				<button
					class="btn-toggle"
					class:active={unit === 'C'}
					onclick={() => (unit = 'C')}
				>
					°C
				</button>
				<button
					class="btn-toggle"
					class:active={unit === 'F'}
					onclick={() => (unit = 'F')}
				>
					°F
				</button>
			</div>

			<select
				class="select days-select"
				bind:value={days}
				onchange={loadWeather}
				aria-label="Select forecast days"
			>
				<option value={3}>3 Days</option>
				<option value={5}>5 Days</option>
				<option value={7}>7 Days</option>
				<option value={10}>10 Days</option>
			</select>

			<button
				class="btn btn-secondary btn-sm"
				onclick={loadWeather}
				disabled={loading}
				aria-label="Refresh weather forecast"
			>
				{#if loading}
					<span class="spinner"></span>
				{:else}
					<span>↻ Refresh</span>
				{/if}
			</button>
		</div>
	</div>

	{#if loading && forecasts.length === 0}
		<div class="loading-state">
			<span class="spinner"></span>
			<p>Fetching forecast from .NET API...</p>
		</div>
	{:else if error}
		<div class="error-box">
			<p>⚠️ {error}</p>
			<button class="btn btn-primary btn-sm" onclick={loadWeather}>Retry</button>
		</div>
	{:else}
		<div class="forecast-grid">
			{#each forecasts as item}
				<div class="forecast-card card-hover">
					<div class="forecast-icon">{getWeatherEmoji(item.summary)}</div>
					<div class="forecast-date">
						{new Date(item.date).toLocaleDateString(undefined, {
							weekday: 'short',
							month: 'short',
							day: 'numeric'
						})}
					</div>
					<div class="forecast-temp">
						{unit === 'C' ? `${item.temperatureC}°C` : `${item.temperatureF}°F`}
					</div>
					<div class="forecast-summary">{item.summary}</div>
				</div>
			{/each}
		</div>
	{/if}
</div>

<style>
	.widget-header {
		display: flex;
		align-items: flex-start;
		justify-content: space-between;
		gap: 1rem;
		margin-bottom: 1.5rem;
		flex-wrap: wrap;
	}

	.controls {
		display: flex;
		align-items: center;
		gap: 0.5rem;
	}

	.unit-toggle {
		display: flex;
		background: var(--bg-surface-elevated);
		border-radius: var(--radius-sm);
		padding: 2px;
		border: 1px solid var(--border-subtle);
	}

	.btn-toggle {
		background: transparent;
		border: none;
		color: var(--text-secondary);
		padding: 0.25rem 0.6rem;
		font-size: 0.8rem;
		font-weight: 600;
		border-radius: 4px;
		cursor: pointer;
		transition: var(--transition);
	}

	.btn-toggle.active {
		background: var(--primary);
		color: #04131f;
	}

	.days-select {
		width: auto;
		padding: 0.35rem 0.65rem;
		font-size: 0.8rem;
	}

	.forecast-grid {
		display: grid;
		grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
		gap: 1rem;
	}

	.forecast-card {
		background: var(--bg-canvas);
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-md);
		padding: 1.25rem 1rem;
		text-align: center;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.4rem;
	}

	.forecast-icon {
		font-size: 2rem;
		line-height: 1;
		margin-bottom: 0.25rem;
	}

	.forecast-date {
		font-size: 0.8rem;
		color: var(--text-secondary);
		font-weight: 500;
	}

	.forecast-temp {
		font-size: 1.4rem;
		font-weight: 700;
		color: var(--text-primary);
		font-family: var(--font-mono);
	}

	.forecast-summary {
		font-size: 0.75rem;
		color: var(--text-muted);
		font-weight: 500;
	}

	.loading-state {
		display: flex;
		flex-direction: column;
		align-items: center;
		justify-content: center;
		padding: 3rem;
		gap: 1rem;
		color: var(--text-secondary);
	}

	.error-box {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1rem;
		background: var(--danger-subtle);
		border: 1px solid rgba(248, 113, 113, 0.3);
		border-radius: var(--radius-md);
		color: var(--danger);
	}
</style>
