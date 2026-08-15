<script lang="ts">
	let iframeError = $state(false);
</script>

<svelte:head>
	<title>Scalar API Reference — TradeMASter</title>
	<meta name="description" content="Interactive OpenAPI & Scalar API reference for TradeMASter backend" />
</svelte:head>

<div class="docs-container">
	<div class="docs-header">
		<div>
			<h1>Interactive API Documentation</h1>
			<p>Live Scalar OpenAPI documentation rendered from ASP.NET Core endpoint metadata.</p>
		</div>
		<div class="docs-actions">
			<a href="/scalar/v1" target="_blank" rel="noreferrer" class="btn btn-primary btn-sm">
				Open in Full Window ↗
			</a>
		</div>
	</div>

	<div class="card iframe-wrapper">
		<iframe
			src="/scalar/v1"
			title="Scalar OpenAPI Documentation"
			class="scalar-frame"
			onerror={() => (iframeError = true)}
		></iframe>
	</div>
</div>

<style>
	.docs-container {
		display: flex;
		flex-direction: column;
		gap: 1.5rem;
	}

	.docs-header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		flex-wrap: wrap;
		gap: 1rem;
	}

	.iframe-wrapper {
		padding: 0;
		overflow: hidden;
		height: 75vh;
		min-height: 600px;
		background: #0d1117;
	}

	.scalar-frame {
		width: 100%;
		height: 100%;
		border: none;
	}
</style>
