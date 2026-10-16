using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace TaskLifecycle.Api.Tests;

public sealed class CompleteTaskIntegrationTest : IClassFixture<TaskLifecycleApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TaskLifecycleApiFactory _factory;

    public CompleteTaskIntegrationTest(TaskLifecycleApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateAndCompleteTask_PublishesEvent()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/tasks", new { title = "Buy milk", priority = "High" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.False(created.IsCompleted);
        Assert.Null(created.CompletedAt);

        var completeResponse = await client.PutAsync($"/tasks/{created.Id}/complete", content: null);
        Assert.Equal(HttpStatusCode.NoContent, completeResponse.StatusCode);

        var published = Assert.Single(_factory.Publisher.Published);
        Assert.Equal(created.Id, published.TaskId);
        Assert.Equal("Buy milk", published.Title);
        Assert.Equal(Priority.High, published.Priority);
        Assert.NotEqual(default(DateTimeOffset), published.CompletedAt);

        var repeatedResponse = await client.PutAsync($"/tasks/{created.Id}/complete", content: null);
        Assert.Equal(HttpStatusCode.Conflict, repeatedResponse.StatusCode);
    }
}

public sealed class TaskLifecycleApiFactory : WebApplicationFactory<Program>
{
    public FakeTaskEventPublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<TaskDbContext>>();
            services.RemoveAll<DbContextOptions<TaskDbContext>>();
            services.RemoveAll<TaskDbContext>();
            var databaseName = $"tasks-{Guid.NewGuid()}";
            services.AddDbContext<TaskDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            // Убираем только RabbitMQ worker, не служебные hosted services тестового хоста.
            var rabbitWorker = services.Single(x =>
                x.ServiceType == typeof(IHostedService) && x.ImplementationFactory is not null);
            services.Remove(rabbitWorker);
            services.RemoveAll<RabbitMqPublisher>();
            services.RemoveAll<ITaskEventPublisher>();
            services.AddSingleton(Publisher);
            services.AddSingleton<ITaskEventPublisher>(Publisher);
        });
    }
}

public sealed class FakeTaskEventPublisher : ITaskEventPublisher
{
    private readonly List<TaskCompletedEvent> _published = [];

    public IReadOnlyList<TaskCompletedEvent> Published => _published;

    public ValueTask PublishAsync(TaskCompletedEvent message, CancellationToken cancellationToken = default)
    {
        lock (_published)
            _published.Add(message);

        return ValueTask.CompletedTask;
    }
}
