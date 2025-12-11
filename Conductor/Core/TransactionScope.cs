using System.Transactions;
using Conductor.Attributes;

namespace Conductor.Core;

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