using Conductor.Interfaces;

namespace Conductor.Core;

public class DefaultTransaction : ITransaction
{
	private bool _completed = false;
	private bool _disposed = false;

	public Task CommitAsync(CancellationToken cancellationToken = default)
	{
		_completed = true;
		return Task.CompletedTask;
	}

	public Task RollbackAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		if (!_disposed)
		{
			if (!_completed)
			{
				// Auto-rollback if not committed
			}
			_disposed = true;
		}
		return ValueTask.CompletedTask;
	}
}