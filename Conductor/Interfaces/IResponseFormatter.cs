using Conductor.Core;
using Conductor.Transport;

namespace Conductor.Interfaces;

public interface IResponseFormatter<TTransport>
{
    Task<TTransport> FormatSuccessAsync<T>(T data, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default);
    Task<TTransport> FormatErrorAsync(Exception exception, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default);
    bool ShouldFormat(object? context = null);
}