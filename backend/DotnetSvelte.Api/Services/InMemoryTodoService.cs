using System.Collections.Concurrent;
using DotnetSvelte.Api.Models;

namespace DotnetSvelte.Api.Services;

public class InMemoryTodoService : ITodoService
{
    private readonly ConcurrentDictionary<Guid, TodoItem> _todos = new();

    public InMemoryTodoService()
    {
        // Seed initial items to demonstrate immediate communication
        var sample1 = new TodoItem(
            Guid.NewGuid(),
            "Explore SvelteKit runes & components",
            "Check out src/lib/components and reactive $state in Svelte 5",
            true,
            DateTime.UtcNow.AddHours(-3),
            null
        );

        var sample2 = new TodoItem(
            Guid.NewGuid(),
            "Inspect .NET 10 Minimal APIs",
            "Review backend/DotnetSvelte.Api/Endpoints and Program.cs",
            true,
            DateTime.UtcNow.AddHours(-2),
            null
        );

        var sample3 = new TodoItem(
            Guid.NewGuid(),
            "Build your full-stack application",
            "Add your own entities, database context (e.g. EF Core / Dapper), and SvelteKit routes",
            false,
            DateTime.UtcNow.AddMinutes(-30),
            null
        );

        _todos[sample1.Id] = sample1;
        _todos[sample2.Id] = sample2;
        _todos[sample3.Id] = sample3;
    }

    public Task<IReadOnlyList<TodoItem>> GetAllAsync()
    {
        var items = _todos.Values
            .OrderByDescending(t => t.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<TodoItem>>(items);
    }

    public Task<TodoItem?> GetByIdAsync(Guid id)
    {
        _todos.TryGetValue(id, out var item);
        return Task.FromResult(item);
    }

    public Task<TodoItem> CreateAsync(CreateTodoRequest request)
    {
        var id = Guid.NewGuid();
        var item = new TodoItem(
            id,
            request.Title.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            false,
            DateTime.UtcNow,
            null
        );

        _todos[id] = item;
        return Task.FromResult(item);
    }

    public Task<TodoItem?> UpdateAsync(Guid id, UpdateTodoRequest request)
    {
        if (!_todos.TryGetValue(id, out var existing))
        {
            return Task.FromResult<TodoItem?>(null);
        }

        var updated = existing with
        {
            Title = request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsCompleted = request.IsCompleted,
            UpdatedAt = DateTime.UtcNow
        };

        _todos[id] = updated;
        return Task.FromResult<TodoItem?>(updated);
    }

    public Task<bool> DeleteAsync(Guid id)
    {
        var removed = _todos.TryRemove(id, out _);
        return Task.FromResult(removed);
    }

    public Task<TodoItem?> ToggleCompleteAsync(Guid id)
    {
        if (!_todos.TryGetValue(id, out var existing))
        {
            return Task.FromResult<TodoItem?>(null);
        }

        var updated = existing with
        {
            IsCompleted = !existing.IsCompleted,
            UpdatedAt = DateTime.UtcNow
        };

        _todos[id] = updated;
        return Task.FromResult<TodoItem?>(updated);
    }
}
