using DotnetSvelte.Api.Models;

namespace DotnetSvelte.Api.Services;

public interface ITodoService
{
    Task<IReadOnlyList<TodoItem>> GetAllAsync();
    Task<TodoItem?> GetByIdAsync(Guid id);
    Task<TodoItem> CreateAsync(CreateTodoRequest request);
    Task<TodoItem?> UpdateAsync(Guid id, UpdateTodoRequest request);
    Task<bool> DeleteAsync(Guid id);
    Task<TodoItem?> ToggleCompleteAsync(Guid id);
}
