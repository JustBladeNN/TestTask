using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TaskLifecycle.Api;

public sealed class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
}

public sealed class RabbitMqPublisher : BackgroundService, ITaskEventPublisher
{
    private const string ExchangeName = "task.events";
    private const string RoutingKey = "task.completed";

    private readonly Channel<TaskCompletedEvent> _pending = Channel.CreateUnbounded<TaskCompletedEvent>();
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;

    private volatile bool _brokerUnavailable;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ValueTask PublishAsync(TaskCompletedEvent message, CancellationToken cancellationToken = default)
    {
        if (_brokerUnavailable || !_pending.Writer.TryWrite(message))
            LogDropped(message);

        return ValueTask.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IConnection? connection = null;
        IChannel? channel = null;

        try
        {
            connection = await CreateConnectionAsync(stoppingToken);
            channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
            await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);

            // На shutdown writer закрывается, а reader дочитывает уже принятые события.
            await foreach (var message in _pending.Reader.ReadAllAsync())
                await PublishToBrokerAsync(channel, message);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _brokerUnavailable = true;
            _logger.LogError(ex, "RabbitMQ is unavailable, completion events will be dropped");

            while (_pending.Reader.TryRead(out var message))
                LogDropped(message);
        }
        finally
        {
            if (channel is not null)
                await channel.DisposeAsync();

            if (connection is not null)
                await connection.DisposeAsync();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pending.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        return factory.CreateConnectionAsync(cancellationToken);
    }

    private async Task PublishToBrokerAsync(IChannel channel, TaskCompletedEvent message)
    {
        try
        {
            await channel.BasicPublishAsync(ExchangeName, RoutingKey, TaskCompletedMessage.From(message).ToJsonUtf8());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish completion event for task {TaskId}", message.TaskId);
        }
    }

    private void LogDropped(TaskCompletedEvent message) =>
        _logger.LogWarning("Dropped completion event for task {TaskId}: RabbitMQ is not available", message.TaskId);
}
