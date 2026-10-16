using Microsoft.EntityFrameworkCore;

namespace TaskLifecycle.Api;

public static class TaskEndpoints
{
    private const int TitleMaxLength = 200;

    public static void MapTaskEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/tasks", CreateAsync);
        routes.MapGet("/tasks", GetAllAsync);
        routes.MapPut("/tasks/{id:guid}/complete", CompleteAsync);
        routes.MapDelete("/tasks/{id:guid}", DeleteAsync);
    }

    private static async Task<IResult> CreateAsync(CreateTaskRequest request, TaskDbContext db, CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > TitleMaxLength)
            return Results.BadRequest(new { error = $"Title must not be empty and must be at most {TitleMaxLength} characters." });

        if (request.Priority.HasValue && !Enum.IsDefined(request.Priority.Value))
            return Results.BadRequest(new { error = "Priority value is not supported." });

        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow,
            Priority = request.Priority ?? Priority.Medium
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/tasks/{task.Id}", TaskResponse.From(task));
    }

    private static async Task<IResult> GetAllAsync(TaskDbContext db, CancellationToken cancellationToken)
    {
        var tasks = await db.Tasks
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .Select(x => new TaskResponse(x.Id, x.Title, x.IsCompleted, x.CreatedAt, x.CompletedAt, x.Priority))
            .ToListAsync(cancellationToken);

        return Results.Ok(tasks);
    }

    private static async Task<IResult> CompleteAsync(
        Guid id,
        TaskDbContext db,
        ITaskEventPublisher publisher,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (task is null)
            return Results.NotFound();

        if (task.IsCompleted)
            return Results.Conflict(new { error = "Task is already completed." });

        var completedAt = DateTimeOffset.UtcNow;
        task.IsCompleted = true;
        task.CompletedAt = completedAt;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Оба запроса могли пройти проверку выше, но xmin позволит сохраниться только первому.
            return Results.Conflict(new { error = "Task was completed by a concurrent request." });
        }

        // Событие публикуется после commit, иначе оно может описывать несохранённое состояние.
        try
        {
            await publisher.PublishAsync(new TaskCompletedEvent(task.Id, task.Title, completedAt, task.Priority), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue completion event for task {TaskId}", task.Id);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, TaskDbContext db, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.FindAsync([id], cancellationToken);
        if (task is null)
            return Results.NotFound();

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }
}
