using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;

namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class TransactionAttribute : Attribute
{
    public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;
    public int TimeoutSeconds { get; set; } = 30;
    public bool RequiresNew { get; set; } = false;
    public string? ConnectionStringName { get; set; }
    public int Priority { get; set; } = 100;

    public TransactionAttribute()
    {
    }

    public TransactionAttribute(IsolationLevel isolationLevel)
    {
        IsolationLevel = isolationLevel;
    }

    public TransactionAttribute(IsolationLevel isolationLevel, int timeoutSeconds)
    {
        IsolationLevel = isolationLevel;
        TimeoutSeconds = timeoutSeconds;
    }
}

public interface ITransactionManager
{
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TransactionAttribute transactionConfig,
        CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        TransactionAttribute transactionConfig,
        CancellationToken cancellationToken = default);
}

public class TransactionScope : IDisposable, IAsyncDisposable
{
    private readonly System.Transactions.TransactionScope _scope;
    private bool _disposed = false;
    private bool _completed = false;

    public TransactionScope(TransactionAttribute config)
    {
        var options = new TransactionOptions
        {
            IsolationLevel = (System.Transactions.IsolationLevel)config.IsolationLevel,
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
        };

        var scopeOption = config.RequiresNew
            ? TransactionScopeOption.RequiresNew
            : TransactionScopeOption.Required;

        _scope = new System.Transactions.TransactionScope(
            scopeOption,
            options,
            TransactionScopeAsyncFlowOption.Enabled);
    }

    public void Complete()
    {
        if (!_completed)
        {
            _scope.Complete();
            _completed = true;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _scope?.Dispose();
            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _scope?.Dispose();
            _disposed = true;
        }
        await Task.CompletedTask;
    }
}