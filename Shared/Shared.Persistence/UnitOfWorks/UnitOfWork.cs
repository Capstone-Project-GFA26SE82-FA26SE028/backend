using Microsoft.EntityFrameworkCore;
using Shared.Persistence.Interfaces;

namespace Shared.Persistence.UnitOfWorks;

public class UnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;

    public UnitOfWork(TContext context)
    {
        _context = context;
    }

    public Task<int> SaveAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SaveChangeAsync()
    {
        return await SaveAsync() > 0;
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        return ExecuteTransactionAsync(action, null, cancellationToken);
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, System.Data.IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        return ExecuteTransactionAsync(action, isolationLevel, cancellationToken);
    }

    private async Task<T> ExecuteTransactionAsync<T>(Func<Task<T>> action, System.Data.IsolationLevel? isolationLevel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var transaction = isolationLevel.HasValue
            ? await _context.Database.BeginTransactionAsync(isolationLevel.Value, cancellationToken)
            : await _context.Database.BeginTransactionAsync(cancellationToken);
        var result = await action();
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public void Dispose()
    {
        // The DI scope owns the injected DbContext and disposes it.
    }
}
