using JobAppManager.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobAppManager.Data;

public class JobAppContext : DbContext
{
    public JobAppContext(DbContextOptions<JobAppContext> options) : base(options)
    {
    }

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<SubmittedItem> SubmittedItems => Set<SubmittedItem>();

    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobAppContext).Assembly);
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Keeps CreatedUtc/UpdatedUtc honest without callers having to remember them.</summary>
    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Application>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedUtc = now;
                    entry.Entity.UpdatedUtc = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedUtc = now;
                    entry.Property(a => a.CreatedUtc).IsModified = false;
                    break;
            }
        }
    }
}

/// <summary>Lets `dotnet ef migrations add` build a context without an app host.</summary>
public class JobAppContextFactory : IDesignTimeDbContextFactory<JobAppContext>
{
    public JobAppContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JobAppContext>()
            .UseSqlite(DbPathProvider.GetDefaultConnectionString())
            .Options;

        return new JobAppContext(options);
    }
}
