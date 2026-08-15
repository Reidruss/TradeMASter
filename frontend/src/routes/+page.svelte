<script lang="ts">
	import StatusBanner from '$lib/components/StatusBanner.svelte';
	import ArchitectureCard from '$lib/components/ArchitectureCard.svelte';
	import WeatherWidget from '$lib/components/WeatherWidget.svelte';
	import TodoManager from '$lib/components/TodoManager.svelte';
	import ApiTester from '$lib/components/ApiTester.svelte';
</script>

<svelte:head>
	<title>TradeMASter — ASP.NET Core & SvelteKit</title>
	<meta
		name="description"
		content="Full-stack application combining ASP.NET Core Minimal APIs with SvelteKit and Svelte 5."
	/>
</svelte:head>

<section class="hero-section">
	<div class="hero-badge">
		<span class="badge badge-primary">Ready-to-Use Template</span>
		<span>Full-Stack Monorepo</span>
	</div>

	<h1 class="hero-title">
		ASP.NET Core <span class="highlight-dotnet">.NET</span> + SvelteKit <span class="highlight-svelte">Svelte 5</span>
	</h1>

	<p class="hero-description">
		A production-ready starter template designed for seamless, friction-free communication between an ASP.NET Core Minimal API backend and a reactive SvelteKit frontend using Vite proxying and strongly typed services.
	</p>
</section>

<!-- Real-time Backend Health Connection Status -->
<StatusBanner />

<!-- Architecture & Flow Explanation -->
<div class="section-spacing">
	<ArchitectureCard />
</div>

<!-- Interactive Live Demos Grid -->
<div class="grid-2 section-spacing">
	<WeatherWidget />
	<TodoManager />
</div>

<!-- Interactive API Tester -->
<div class="section-spacing">
	<ApiTester />
</div>

<style>
	.hero-section {
		text-align: center;
		max-width: 820px;
		margin: 1.5rem auto 2.5rem auto;
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 1rem;
	}

	.hero-badge {
		display: flex;
		align-items: center;
		gap: 0.75rem;
		font-size: 0.85rem;
		color: var(--text-secondary);
	}

	.hero-title {
		font-size: 2.75rem;
		line-height: 1.15;
		font-weight: 800;
	}

	.highlight-dotnet {
		color: var(--accent-dotnet);
	}

	.highlight-svelte {
		color: var(--accent-svelte);
	}

	.hero-description {
		font-size: 1.05rem;
		line-height: 1.6;
		color: var(--text-secondary);
	}

	.section-spacing {
		margin-bottom: 2rem;
	}

	@media (max-width: 768px) {
		.hero-title {
			font-size: 2rem;
		}
	}
</style>
