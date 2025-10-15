namespace Conductor.Interfaces;

public interface ITransactionService
{
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}