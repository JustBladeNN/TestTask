using System.Text.Json;

namespace TaskLifecycle.Api;

public sealed record CreateTaskRequest(string? Title, Priority? Priority);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    Priority Priority)
{
    public static TaskResponse From(TaskItem task) =>
        new(task.Id, task.Title, task.IsCompleted, task.CreatedAt, task.CompletedAt, task.Priority);
}

public sealed record TaskCompletedEvent(Guid TaskId, string Title, DateTimeOffset CompletedAt, Priority Priority);

public sealed record TaskCompletedMessage(Guid TaskId, string Title, DateTime CompletedAt, string Priority)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static TaskCompletedMessage From(TaskCompletedEvent source) =>
        new(source.TaskId, source.Title, source.CompletedAt.UtcDateTime, source.Priority.ToString());

    public byte[] ToJsonUtf8() => JsonSerializer.SerializeToUtf8Bytes(this, SerializerOptions);
}
