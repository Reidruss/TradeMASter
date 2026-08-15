<script lang="ts">
	import '../app.css';
	import Navbar from '$lib/components/Navbar.svelte';

	let { children } = $props();
</script>

<div class="app-layout">
	<Navbar />

	<main class="main-content">
		<div class="container">
			{@render children()}
		</div>
	</main>

	<footer class="app-footer">
		<div class="container footer-content">
			<div class="footer-left">
				<span>DotnetSvelte Full-Stack Template</span>
				<span class="sep">•</span>
				<span class="tech-stack">ASP.NET Core Minimal APIs + SvelteKit</span>
			</div>
			<div class="footer-right">
				<a href="/scalar/v1" target="_blank" rel="noreferrer">Scalar OpenAPI Docs</a>
				<span class="sep">•</span>
				<a href="https://svelte.dev/docs" target="_blank" rel="noreferrer">Svelte 5 Docs</a>
			</div>
		</div>
	</footer>
</div>

<style>
	.app-layout {
		min-height: 100vh;
		display: flex;
		flex-direction: column;
	}

	.main-content {
		flex: 1;
		padding: 2rem 0 4rem 0;
	}

	.app-footer {
		border-top: 1px solid var(--border-subtle);
		background: var(--bg-surface);
		padding: 1.5rem 0;
		font-size: 0.85rem;
		color: var(--text-muted);
	}

	.footer-content {
		display: flex;
		align-items: center;
		justify-content: space-between;
		flex-wrap: wrap;
		gap: 1rem;
	}

	.footer-left, .footer-right {
		display: flex;
		align-items: center;
		gap: 0.6rem;
		flex-wrap: wrap;
	}

	.sep {
		color: var(--border-strong);
	}

	.tech-stack {
		color: var(--text-secondary);
	}
</style>
