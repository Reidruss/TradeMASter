namespace DotnetSvelte.Api.Models;

public record TodoItem(
    Guid Id,
    string Title,
    string? Description,
    bool IsCompleted,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record CreateTodoRequest(
    string Title,
    string? Description
);

public record UpdateTodoRequest(
    string Title,
    string? Description,
    bool IsCompleted
);
