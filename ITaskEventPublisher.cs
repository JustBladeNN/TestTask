namespace TaskLifecycle.Api;

public interface ITaskEventPublisher
{
    ValueTask PublishAsync(TaskCompletedEvent message, CancellationToken cancellationToken = default);
}
