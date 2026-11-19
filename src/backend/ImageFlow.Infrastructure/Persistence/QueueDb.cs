using Microsoft.EntityFrameworkCore;
namespace ImageFlow;

public class QueueDb(DbContextOptions<QueueDb> options) : DbContext(options)
{
    public DbSet<Plan> Plans => Set<Plan>(); public DbSet<Prompt> Prompts => Set<Prompt>();
    public DbSet<Job> Jobs => Set<Job>(); public DbSet<GenerationTask> Tasks => Set<GenerationTask>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<RuntimeSettings> RuntimeSettings => Set<RuntimeSettings>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Plan>().HasMany(x => x.Prompts).WithOne().HasForeignKey(x => x.PlanId);
        b.Entity<Job>().HasIndex(x => x.PlanId).IsUnique();
        b.Entity<Job>().HasMany(x => x.Tasks).WithOne().HasForeignKey(x => x.JobId);
        b.Entity<GenerationTask>().HasMany(x => x.Attempts).WithOne().HasForeignKey(x => x.GenerationTaskId);
        b.Entity<GenerationTask>().HasIndex(x => new { x.Status, x.NextAttemptAt });
    }
}
