<script lang="ts">
	import TodoManager from '$lib/components/TodoManager.svelte';
</script>

<svelte:head>
	<title>Todos CRUD — DotnetSvelte Template</title>
	<meta name="description" content="Full RESTful CRUD operations with .NET Minimal APIs and SvelteKit" />
</svelte:head>

<div class="page-container">
	<div class="page-header">
		<h1>RESTful CRUD Example</h1>
		<p>Demonstrates full Create, Read, Update, and Delete operations using typed client services.</p>
	</div>

	<TodoManager />

	<div class="card doc-callout">
		<h3>How this communicates:</h3>
		<ul class="steps-list">
			<li>
				<strong>Client:</strong> <code>frontend/src/lib/api/services/todos.ts</code> defines <code>todoService.getAll()</code>, <code>create()</code>, <code>update()</code>, <code>delete()</code>.
			</li>
			<li>
				<strong>Vite Proxy:</strong> In dev mode, requests to <code>/api/todos</code> are automatically proxied to <code>http://localhost:5126/api/todos</code>.
			</li>
			<li>
				<strong>Backend:</strong> <code>backend/DotnetSvelte.Api/Endpoints/TodoEndpoints.cs</code> maps Minimal API route handlers.
			</li>
		</ul>
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

	.steps-list {
		display: flex;
		flex-direction: column;
		gap: 0.6rem;
		padding-left: 1.25rem;
		color: var(--text-secondary);
		font-size: 0.9rem;
	}

	.steps-list code {
		font-family: var(--font-mono);
		color: var(--primary);
		background: rgba(56, 189, 248, 0.1);
		padding: 0.1rem 0.35rem;
		border-radius: 4px;
	}
</style>
