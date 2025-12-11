using Conductor.Interfaces;
using Microsoft.Extensions.Logging;

namespace Conductor.Core;

public abstract class BaseResponseFormatter<TTransport> : IResponseFormatter<TTransport>
{
	protected readonly ResponseFormattingOptions Options;
	private readonly IResponseMetadataProvider _metadataProvider;
	private readonly Microsoft.Extensions.Logging.ILogger _logger;

	protected BaseResponseFormatter(ResponseFormattingOptions options,
		IResponseMetadataProvider metadataProvider,
		Microsoft.Extensions.Logging.ILogger logger)
	{
		Options = options;
		_metadataProvider = metadataProvider;
		_logger = logger;
	}

	public abstract Task<TTransport> FormatSuccessAsync<T>(T data, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default);
	public abstract Task<TTransport> FormatErrorAsync(Exception exception, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default);
	public abstract bool ShouldFormat(object? context = null);

	protected virtual ResponseMetadata GetOrCreateMetadata(ResponseMetadata? provided = null, object? context = null)
	{
		var metadata = provided ?? _metadataProvider.CreateMetadata(context);

		// Apply global metadata
		foreach (var kvp in Options.GlobalMetadata)
		{
			metadata.CustomProperties.TryAdd(kvp.Key, kvp.Value);
		}
		return metadata;
	}

	protected virtual void LogException(Exception exception, ResponseMetadata metadata)
	{
		if (Options.LogExceptions)
		{
			_logger.LogError(exception, "Request failed - CorrelationId: {CorrelationId}, RequestId: {RequestId}",
				metadata.CorrelationId, metadata.RequestId);
		}
	}
}