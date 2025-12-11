using Conductor.Core;
using Conductor.Interfaces;
using Microsoft.Extensions.Logging;

namespace Conductor.Pipeline;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
where TRequest : BaseRequest, ITransactionalRequest
{
	private readonly ITransactionService _transactionService;
	private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

	public TransactionBehavior(ITransactionService transactionService, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
	{
		_transactionService = transactionService;
		_logger = logger;
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		if (request.RequiresTransaction)
		{
			_logger.LogDebug("Starting transaction for {RequestName}", typeof(TRequest).Name);
			await using var transaction = await _transactionService.BeginTransactionAsync(cancellationToken);
			try
			{
				var response = await next();
				await transaction.CommitAsync(cancellationToken);
				_logger.LogDebug("Transaction committed for {RequestName}", typeof(TRequest).Name);
				return response;
			}
			catch(Exception ex)
			{
				await transaction.RollbackAsync(cancellationToken);
				_logger.LogWarning(ex, "Transaction rolled back for {RequestName}", typeof(TRequest).Name);
				throw;
			}
		}
		return await next();
	}
}