using Conductor.Core;
using Conductor.Extensions;
using Conductor.Interfaces;
using Conductor.Pipeline;

namespace Conductor.Services;

public class DefaultTransactionService : ITransactionService
{
    public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ITransaction>(new DefaultTransaction());
    }
}