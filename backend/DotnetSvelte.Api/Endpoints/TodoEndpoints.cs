using DotnetSvelte.Api.Models;
using DotnetSvelte.Api.Services;

namespace DotnetSvelte.Api.Endpoints;

public static class TodoEndpoints
{
    public static RouteGroupBuilder MapTodoEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/todos")
            .WithTags("Todos");

        group.MapGet("/", async (ITodoService todoService) =>
        {
            var todos = await todoService.GetAllAsync();
            return Results.Ok(todos);
        })
        .WithName("GetTodos")
        .WithSummary("Get all todo items")
        .Produces<IReadOnlyList<TodoItem>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (Guid id, ITodoService todoService) =>
        {
            var item = await todoService.GetByIdAsync(id);
            return item is not null ? Results.Ok(item) : Results.NotFound(new { message = $"Todo with id {id} not found" });
        })
        .WithName("GetTodoById")
        .WithSummary("Get a specific todo item by id")
        .Produces<TodoItem>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateTodoRequest request, ITodoService todoService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.BadRequest(new { error = "Title cannot be empty" });
            }

            var created = await todoService.CreateAsync(request);
            return Results.Created($"/api/todos/{created.Id}", created);
        })
        .WithName("CreateTodo")
        .WithSummary("Create a new todo item")
        .Produces<TodoItem>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", async (Guid id, UpdateTodoRequest request, ITodoService todoService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.BadRequest(new { error = "Title cannot be empty" });
            }

            var updated = await todoService.UpdateAsync(id, request);
            return updated is not null ? Results.Ok(updated) : Results.NotFound(new { message = $"Todo with id {id} not found" });
        })
        .WithName("UpdateTodo")
        .WithSummary("Update an existing todo item")
        .Produces<TodoItem>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPatch("/{id:guid}/toggle", async (Guid id, ITodoService todoService) =>
        {
            var toggled = await todoService.ToggleCompleteAsync(id);
            return toggled is not null ? Results.Ok(toggled) : Results.NotFound(new { message = $"Todo with id {id} not found" });
        })
        .WithName("ToggleTodo")
        .WithSummary("Toggle the completion status of a todo item")
        .Produces<TodoItem>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (Guid id, ITodoService todoService) =>
        {
            var success = await todoService.DeleteAsync(id);
            return success ? Results.NoContent() : Results.NotFound(new { message = $"Todo with id {id} not found" });
        })
        .WithName("DeleteTodo")
        .WithSummary("Delete a todo item")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        return group;
    }
}
