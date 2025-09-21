using Microsoft.Extensions.Logging;

namespace Conductor.Transport;

public interface IResponseFormatter<TTransport>
{
    Task<TTransport> FormatSuccessAsync<T>(T data, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default);
    Task<TTransport> FormatErrorAsync(Exception exception, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default);
    bool ShouldFormat(object? context = null);
}

public interface IResponseMetadataProvider
{
    ResponseMetadata CreateMetadata(object? context = null);
}

public class ResponseMetadata
{
    public string? CorrelationId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? RequestId { get; set; }
    public string? UserId { get; set; }
    public Dictionary<string, object> CustomProperties { get; set; } = new();
}

public class ResponseFormattingOptions
{
    public bool WrapAllResponses { get; set; } = true;
    public bool IncludeTimestamp { get; set; } = true;
    public bool IncludeCorrelationId { get; set; } = true;
    public bool IncludeRequestId { get; set; } = true;
    public bool IncludeUserId { get; set; } = false;
    public string SuccessMessage { get; set; } = "Success";
    public string DefaultErrorMessage { get; set; } = "An error occurred";
    public List<string> ExcludedPaths { get; set; } = new() { "/health", "/metrics", "/swagger" };
    public List<string> ExcludedContentTypes { get; set; } = new() { "text/html", "image/*", "application/octet-stream" };
    public bool LogExceptions { get; set; } = true;
    public bool IncludeStackTrace { get; set; } = false;
    public Dictionary<string, object> GlobalMetadata { get; set; } = new();
}

public abstract class BaseResponseFormatter<TTransport> : IResponseFormatter<TTransport>
{
    protected readonly ResponseFormattingOptions _options;
    protected readonly IResponseMetadataProvider _metadataProvider;
    protected readonly Microsoft.Extensions.Logging.ILogger _logger;

    protected BaseResponseFormatter(
        ResponseFormattingOptions options,
        IResponseMetadataProvider metadataProvider,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        _options = options;
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
        foreach (var kvp in _options.GlobalMetadata)
        {
            metadata.CustomProperties.TryAdd(kvp.Key, kvp.Value);
        }

        return metadata;
    }

    protected virtual void LogException(Exception exception, ResponseMetadata metadata)
    {
        if (_options.LogExceptions)
        {
            _logger.LogError(exception, "Request failed - CorrelationId: {CorrelationId}, RequestId: {RequestId}",
                metadata.CorrelationId, metadata.RequestId);
        }
    }
}