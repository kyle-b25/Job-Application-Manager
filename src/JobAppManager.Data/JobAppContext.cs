using JobAppManager.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobAppManager.Data;

public class JobAppContext : DbContext
{
    private readonly TimeProvider _timeProvider;

    /// <param name="timeProvider">Where the stamped timestamps come from. Optional so every
    /// existing call site - including EF's design-time factory - keeps working; tests pass a
    /// fixed clock so "did UpdatedUtc advance?" can be asserted exactly rather than with a
    /// tolerance band around the wall clock.</param>
    public JobAppContext(DbContextOptions<JobAppContext> options, TimeProvider? timeProvider = null)
        : base(options)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<SubmittedItem> SubmittedItems => Set<SubmittedItem>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<StatusChange> StatusChanges => Set<StatusChange>();

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
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // A status change is stamped when it is written and never rewritten - it is a log entry,
        // not a mutable row - so only the Added case exists here.
        foreach (var entry in ChangeTracker.Entries<StatusChange>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.ChangedUtc = now;
            }
        }

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
