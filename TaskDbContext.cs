using Microsoft.EntityFrameworkCore;

namespace TaskLifecycle.Api;

public sealed class TaskDbContext(DbContextOptions<TaskDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();

        task.HasKey(x => x.Id);

        task.Property(x => x.Title).HasMaxLength(200).IsRequired();
        task.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
        task.Property(x => x.Version).IsRowVersion();
    }
}
