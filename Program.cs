using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskLifecycle.Api;

var builder = WebApplication.CreateBuilder(args);

#region Сервисы

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<TaskDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Tasks")));

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<ITaskEventPublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMqPublisher>());

builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));

#endregion

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapTaskEndpoints();

app.Run();

public partial class Program { }
