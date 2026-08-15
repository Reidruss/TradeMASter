<script lang="ts">
	import { onMount } from 'svelte';
	import { todoService } from '$lib/api';
	import type { TodoItem } from '$lib/api';

	let todos = $state<TodoItem[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);

	// Form inputs
	let newTitle = $state('');
	let newDescription = $state('');
	let submitting = $state(false);

	// Editing state
	let editingId = $state<string | null>(null);
	let editTitle = $state('');
	let editDescription = $state('');

	async function loadTodos() {
		loading = true;
		error = null;
		try {
			todos = await todoService.getAll();
		} catch (err: any) {
			error = err?.message || 'Failed to load todos';
		} finally {
			loading = false;
		}
	}

	async function handleCreate(e: SubmitEvent) {
		e.preventDefault();
		if (!newTitle.trim() || submitting) return;

		submitting = true;
		error = null;
		try {
			const created = await todoService.create({
				title: newTitle.trim(),
				description: newDescription.trim() || undefined
			});
			todos = [created, ...todos];
			newTitle = '';
			newDescription = '';
		} catch (err: any) {
			error = err?.message || 'Failed to create todo';
		} finally {
			submitting = false;
		}
	}

	async function handleToggle(id: string) {
		try {
			// Optimistic UI update
			todos = todos.map((t) => (t.id === id ? { ...t, isCompleted: !t.isCompleted } : t));
			const updated = await todoService.toggleComplete(id);
			todos = todos.map((t) => (t.id === id ? updated : t));
		} catch (err: any) {
			error = err?.message || 'Failed to toggle status';
			loadTodos(); // Revert on failure
		}
	}

	async function handleDelete(id: string) {
		try {
			// Optimistic UI update
			const previous = todos;
			todos = todos.filter((t) => t.id !== id);
			await todoService.delete(id);
		} catch (err: any) {
			error = err?.message || 'Failed to delete todo';
			loadTodos();
		}
	}

	function startEdit(item: TodoItem) {
		editingId = item.id;
		editTitle = item.title;
		editDescription = item.description || '';
	}

	function cancelEdit() {
		editingId = null;
		editTitle = '';
		editDescription = '';
	}

	async function saveEdit(id: string, isCompleted: boolean) {
		if (!editTitle.trim()) return;
		try {
			const updated = await todoService.update(id, {
				title: editTitle.trim(),
				description: editDescription.trim() || null,
				isCompleted
			});
			todos = todos.map((t) => (t.id === id ? updated : t));
			cancelEdit();
		} catch (err: any) {
			error = err?.message || 'Failed to update todo';
		}
	}

	let completedCount = $derived(todos.filter((t) => t.isCompleted).length);

	onMount(() => {
		loadTodos();
	});
</script>

<div class="todo-manager card">
	<div class="header">
		<div>
			<h3>RESTful CRUD Tasks</h3>
			<p>Live synchronized state with ASP.NET Core <code>/api/todos</code></p>
		</div>
		<div class="stats">
			<span class="badge badge-primary">
				{completedCount} / {todos.length} Done
			</span>
			<button
				class="btn btn-secondary btn-sm"
				onclick={loadTodos}
				disabled={loading}
				aria-label="Refresh tasks"
			>
				{#if loading}
					<span class="spinner"></span>
				{:else}
					<span>↻</span>
				{/if}
			</button>
		</div>
	</div>

	<!-- Create Form -->
	<form class="create-form" onsubmit={handleCreate}>
		<div class="inputs-row">
			<input
				type="text"
				class="input"
				placeholder="What needs to be done? (e.g. Add authentication)"
				bind:value={newTitle}
				required
			/>
			<input
				type="text"
				class="input"
				placeholder="Optional description / notes"
				bind:value={newDescription}
			/>
			<button type="submit" class="btn btn-primary" disabled={submitting || !newTitle.trim()}>
				{#if submitting}
					<span class="spinner"></span>
				{:else}
					<span>+ Add Item</span>
				{/if}
			</button>
		</div>
	</form>

	{#if error}
		<div class="error-banner">
			<span>⚠️ {error}</span>
			<button class="btn-clear" onclick={() => (error = null)}>✕</button>
		</div>
	{/if}

	<!-- Todo List -->
	{#if loading && todos.length === 0}
		<div class="loading-state">
			<span class="spinner"></span>
			<p>Loading items from backend...</p>
		</div>
	{:else if todos.length === 0}
		<div class="empty-state">
			<p>No tasks yet. Create one above to test POST communication!</p>
		</div>
	{:else}
		<div class="todo-list">
			{#each todos as item (item.id)}
				<div class="todo-item card-hover" class:completed={item.isCompleted}>
					{#if editingId === item.id}
						<!-- Edit Mode -->
						<div class="edit-mode">
							<input type="text" class="input" bind:value={editTitle} />
							<input
								type="text"
								class="input"
								placeholder="Optional description"
								bind:value={editDescription}
							/>
							<div class="edit-actions">
								<button
									class="btn btn-primary btn-sm"
									onclick={() => saveEdit(item.id, item.isCompleted)}
								>
									Save
								</button>
								<button class="btn btn-secondary btn-sm" onclick={cancelEdit}>Cancel</button>
							</div>
						</div>
					{:else}
						<!-- View Mode -->
						<div class="item-main">
							<button
								type="button"
								class="checkbox-btn"
								class:checked={item.isCompleted}
								onclick={() => handleToggle(item.id)}
								aria-label="Toggle completed"
							>
								{item.isCompleted ? '✓' : ''}
							</button>

							<div class="item-details">
								<span class="item-title">{item.title}</span>
								{#if item.description}
									<span class="item-desc">{item.description}</span>
								{/if}
								<span class="item-meta">
									ID: <code>{item.id.slice(0, 8)}...</code> • Created: {new Date(
										item.createdAt
									).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
								</span>
							</div>
						</div>

						<div class="item-actions">
							<button
								class="btn-icon"
								title="Edit item"
								onclick={() => startEdit(item)}
								aria-label="Edit item"
							>
								✎
							</button>
							<button
								class="btn-icon btn-icon-delete"
								title="Delete item"
								onclick={() => handleDelete(item.id)}
								aria-label="Delete item"
							>
								✕
							</button>
						</div>
					{/if}
				</div>
			{/each}
		</div>
	{/if}
</div>

<style>
	.header {
		display: flex;
		align-items: center;
		justify-content: space-between;
		margin-bottom: 1.25rem;
		flex-wrap: wrap;
		gap: 0.75rem;
	}

	.stats {
		display: flex;
		align-items: center;
		gap: 0.75rem;
	}

	.create-form {
		margin-bottom: 1.5rem;
	}

	.inputs-row {
		display: grid;
		grid-template-columns: 2fr 2fr auto;
		gap: 0.75rem;
	}

	.error-banner {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 0.75rem 1rem;
		background: var(--danger-subtle);
		color: var(--danger);
		border-radius: var(--radius-md);
		margin-bottom: 1rem;
		font-size: 0.85rem;
	}

	.btn-clear {
		background: none;
		border: none;
		color: var(--danger);
		cursor: pointer;
		font-size: 1rem;
	}

	.todo-list {
		display: flex;
		flex-direction: column;
		gap: 0.75rem;
	}

	.todo-item {
		display: flex;
		align-items: center;
		justify-content: space-between;
		padding: 1rem;
		background: var(--bg-canvas);
		border: 1px solid var(--border-subtle);
		border-radius: var(--radius-md);
		gap: 1rem;
		transition: var(--transition);
	}

	.todo-item.completed {
		opacity: 0.7;
	}

	.todo-item.completed .item-title {
		text-decoration: line-through;
		color: var(--text-secondary);
	}

	.item-main {
		display: flex;
		align-items: flex-start;
		gap: 0.85rem;
		flex: 1;
	}

	.checkbox-btn {
		width: 1.4rem;
		height: 1.4rem;
		border-radius: 4px;
		border: 2px solid var(--border-strong);
		background: transparent;
		color: #04131f;
		display: flex;
		align-items: center;
		justify-content: center;
		font-weight: 700;
		font-size: 0.85rem;
		cursor: pointer;
		margin-top: 0.15rem;
		transition: var(--transition);
	}

	.checkbox-btn.checked {
		background: var(--success);
		border-color: var(--success);
		color: #04131f;
	}

	.item-details {
		display: flex;
		flex-direction: column;
		gap: 0.2rem;
	}

	.item-title {
		font-weight: 600;
		font-size: 0.95rem;
		color: var(--text-primary);
	}

	.item-desc {
		font-size: 0.85rem;
		color: var(--text-secondary);
	}

	.item-meta {
		font-size: 0.75rem;
		color: var(--text-muted);
		margin-top: 0.15rem;
	}

	.item-meta code {
		font-family: var(--font-mono);
	}

	.item-actions {
		display: flex;
		align-items: center;
		gap: 0.4rem;
	}

	.btn-icon {
		background: transparent;
		border: 1px solid transparent;
		color: var(--text-secondary);
		width: 2rem;
		height: 2rem;
		border-radius: var(--radius-sm);
		cursor: pointer;
		display: flex;
		align-items: center;
		justify-content: center;
		transition: var(--transition);
	}

	.btn-icon:hover {
		background: var(--bg-surface-elevated);
		color: var(--text-primary);
	}

	.btn-icon-delete:hover {
		background: var(--danger-subtle);
		color: var(--danger);
	}

	.edit-mode {
		display: flex;
		flex-direction: column;
		gap: 0.5rem;
		width: 100%;
	}

	.edit-actions {
		display: flex;
		gap: 0.5rem;
	}

	.empty-state, .loading-state {
		text-align: center;
		padding: 2.5rem 1rem;
		color: var(--text-secondary);
		display: flex;
		flex-direction: column;
		align-items: center;
		gap: 0.75rem;
	}

	@media (max-width: 768px) {
		.inputs-row {
			grid-template-columns: 1fr;
		}
	}
</style>
