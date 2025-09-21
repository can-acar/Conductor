using System.Text.Json.Serialization;

namespace Conductor.Transport.Http;

public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("metadata")]
    public ResponseMetadata? Metadata { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    public static ApiResponse<T> CreateSuccess(T data, string? message = null, ResponseMetadata? metadata = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message,
            Metadata = metadata,
            Timestamp = metadata?.Timestamp,
            CorrelationId = metadata?.CorrelationId,
            RequestId = metadata?.RequestId
        };
    }

    public static ApiResponse<T> CreateError(string message, List<string>? errors = null, ResponseMetadata? metadata = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Errors = errors ?? new List<string>(),
            Metadata = metadata,
            Timestamp = metadata?.Timestamp,
            CorrelationId = metadata?.CorrelationId,
            RequestId = metadata?.RequestId
        };
    }

    public static ApiResponse<T> CreateError(Exception exception, bool includeStackTrace = false, ResponseMetadata? metadata = null)
    {
        var errors = new List<string> { exception.Message };

        if (includeStackTrace && !string.IsNullOrEmpty(exception.StackTrace))
        {
            errors.Add($"Stack Trace: {exception.StackTrace}");
        }

        var innerException = exception.InnerException;
        while (innerException != null)
        {
            errors.Add($"Inner Exception: {innerException.Message}");
            innerException = innerException.InnerException;
        }

        return new ApiResponse<T>
        {
            Success = false,
            Message = "An error occurred",
            Errors = errors,
            Metadata = metadata,
            Timestamp = metadata?.Timestamp,
            CorrelationId = metadata?.CorrelationId,
            RequestId = metadata?.RequestId
        };
    }
}

// Non-generic version for void responses
public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse CreateSuccess(string? message = null, ResponseMetadata? metadata = null)
    {
        return new ApiResponse
        {
            Success = true,
            Message = message,
            Metadata = metadata,
            Timestamp = metadata?.Timestamp,
            CorrelationId = metadata?.CorrelationId,
            RequestId = metadata?.RequestId
        };
    }

    public new static ApiResponse CreateError(string message, List<string>? errors = null, ResponseMetadata? metadata = null)
    {
        return new ApiResponse
        {
            Success = false,
            Message = message,
            Errors = errors ?? new List<string>(),
            Metadata = metadata,
            Timestamp = metadata?.Timestamp,
            CorrelationId = metadata?.CorrelationId,
            RequestId = metadata?.RequestId
        };
    }

    public new static ApiResponse CreateError(Exception exception, bool includeStackTrace = false, ResponseMetadata? metadata = null)
    {
        var errors = new List<string> { exception.Message };

        if (includeStackTrace && !string.IsNullOrEmpty(exception.StackTrace))
        {
            errors.Add($"Stack Trace: {exception.StackTrace}");
        }

        return new ApiResponse
        {
            Success = false,
            Message = "An error occurred",
            Errors = errors,
            Metadata = metadata,
            Timestamp = metadata?.Timestamp,
            CorrelationId = metadata?.CorrelationId,
            RequestId = metadata?.RequestId
        };
    }
}