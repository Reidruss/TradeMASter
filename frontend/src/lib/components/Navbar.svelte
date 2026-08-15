<script lang="ts">
	import { page } from '$app/stores';

	let currentPath = $derived($page.url.pathname);
</script>

<header class="nav-header">
	<div class="container nav-content">
		<a href="/" class="brand">
			<div class="brand-icons">
				<span class="badge-dot-net">.NET</span>
				<span class="plus">+</span>
				<span class="badge-svelte">Svelte</span>
			</div>
			<div class="brand-text">
				<span class="brand-title">DotnetSvelte</span>
				<span class="brand-sub">Full-Stack Template</span>
			</div>
		</a>

		<nav class="nav-links">
			<a href="/" class="nav-link" class:active={currentPath === '/'}>
				Overview
			</a>
			<a href="/todos" class="nav-link" class:active={currentPath === '/todos'}>
				Todo CRUD
			</a>
			<a href="/weather" class="nav-link" class:active={currentPath === '/weather'}>
				Weather API
			</a>
			<a href="/docs" class="nav-link" class:active={currentPath === '/docs'}>
				Scalar Docs
			</a>
		</nav>

		<div class="nav-actions">
			<a
				href="/scalar/v1"
				target="_blank"
				rel="noreferrer"
				class="btn btn-secondary btn-sm"
			>
				<span>OpenAPI Spec</span>
				<span class="external-icon">↗</span>
			</a>
		</div>
	</div>
</header>

<style>
	.nav-header {
		background: rgba(18, 24, 38, 0.85);
		backdrop-filter: blur(12px);
		border-bottom: 1px solid var(--border-subtle);
		position: sticky;
		top: 0;
		z-index: 50;
	}

	.nav-content {
		display: flex;
		align-items: center;
		justify-content: space-between;
		height: 4.25rem;
	}

	.brand {
		display: flex;
		align-items: center;
		gap: 0.85rem;
		text-decoration: none;
		color: var(--text-primary);
	}

	.brand-icons {
		display: flex;
		align-items: center;
		gap: 0.3rem;
		font-family: var(--font-mono);
		font-size: 0.8rem;
		font-weight: 700;
	}

	.badge-dot-net {
		background: #512bd4;
		color: #ffffff;
		padding: 0.2rem 0.5rem;
		border-radius: var(--radius-sm);
	}

	.badge-svelte {
		background: #ff3e00;
		color: #ffffff;
		padding: 0.2rem 0.5rem;
		border-radius: var(--radius-sm);
	}

	.plus {
		color: var(--text-muted);
		font-weight: 400;
	}

	.brand-text {
		display: flex;
		flex-direction: column;
	}

	.brand-title {
		font-weight: 700;
		font-size: 1.05rem;
		letter-spacing: -0.01em;
	}

	.brand-sub {
		font-size: 0.72rem;
		color: var(--text-muted);
	}

	.nav-links {
		display: flex;
		align-items: center;
		gap: 0.35rem;
	}

	.nav-link {
		color: var(--text-secondary);
		padding: 0.45rem 0.85rem;
		border-radius: var(--radius-md);
		font-size: 0.9rem;
		font-weight: 500;
		transition: var(--transition);
	}

	.nav-link:hover {
		color: var(--text-primary);
		background: var(--bg-surface-elevated);
	}

	.nav-link.active {
		color: var(--primary);
		background: var(--primary-subtle);
		font-weight: 600;
	}

	.nav-actions {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.external-icon {
		font-size: 0.75rem;
		opacity: 0.7;
	}

	@media (max-width: 768px) {
		.nav-content {
			flex-direction: column;
			height: auto;
			padding: 0.75rem 1rem;
			gap: 0.75rem;
		}

		.nav-links {
			width: 100%;
			justify-content: center;
			flex-wrap: wrap;
		}
	}
</style>
