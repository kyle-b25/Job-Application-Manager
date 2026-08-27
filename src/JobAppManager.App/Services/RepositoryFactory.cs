using JobAppManager.Core.Abstractions;
using JobAppManager.Data;
using JobAppManager.Data.Repositories;

namespace JobAppManager.App.Services;

/// <summary>One unit of work: a fresh <see cref="JobAppContext"/> and a repository over it,
/// disposed together.</summary>
public sealed class RepositoryScope : IAsyncDisposable
{
    private readonly JobAppContext _context;

    internal RepositoryScope(JobAppContext context, TimeProvider timeProvider)
    {
        _context = context;
        Repository = new ApplicationRepository(context, timeProvider);
    }

    public IApplicationRepository Repository { get; }

    public ValueTask DisposeAsync() => _context.DisposeAsync();
}

/// <summary>Hands out a short-lived repository per operation.
///
/// A desktop app that keeps one DbContext for its whole lifetime accumulates tracked entities
/// forever and starts serving stale reads: an edit saved through one screen is invisible to
/// another that already has the row cached. Scoping to the operation is the cheap fix, and
/// SQLite makes opening a connection nearly free.</summary>
public sealed class RepositoryFactory
{
    private readonly Func<JobAppContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    public RepositoryFactory(Func<JobAppContext> contextFactory, TimeProvider? timeProvider = null)
    {
        _contextFactory = contextFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RepositoryScope Create() => new(_contextFactory(), _timeProvider);
}
