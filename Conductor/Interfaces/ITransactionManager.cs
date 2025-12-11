using Conductor.Attributes;

namespace Conductor.Interfaces;

public interface ITransactionManager
{
	Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation,
		TransactionAttribute transactionConfig,
		CancellationToken cancellationToken = default);

	Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
		TransactionAttribute transactionConfig,
		CancellationToken cancellationToken = default);
}