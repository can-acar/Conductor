using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Conductor.Core;
using Conductor.Interfaces;
using Conductor.Transport;
using ValidationExceptionAlias = Conductor.Core.ValidationException;

namespace Conductor.Transport.Http;

public class HttpResponseFormatter : BaseResponseFormatter<string>
{
    private readonly JsonSerializerOptions _jsonOptions;

    public HttpResponseFormatter(
        ResponseFormattingOptions options,
        IResponseMetadataProvider metadataProvider,
        ILogger<HttpResponseFormatter> logger,
        JsonSerializerOptions? jsonOptions = null)
        : base(options, metadataProvider, logger)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public override async Task<string> FormatSuccessAsync<T>(T data, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default)
    {
        var responseMetadata = GetOrCreateMetadata(metadata);

        var apiResponse = ApiResponse<T>.CreateSuccess(
            data,
            Options.SuccessMessage,
            Options.IncludeTimestamp || Options.IncludeCorrelationId || Options.IncludeRequestId ? responseMetadata : null);

        return JsonSerializer.Serialize(apiResponse, _jsonOptions);
    }

    public override async Task<string> FormatErrorAsync(Exception exception, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default)
    {
        var responseMetadata = GetOrCreateMetadata(metadata);

        LogException(exception, responseMetadata);

        var apiResponse = ApiResponse<object>.CreateError(
            exception,
            Options.IncludeStackTrace,
            Options.IncludeTimestamp || Options.IncludeCorrelationId || Options.IncludeRequestId ? responseMetadata : null);

        // Override message based on exception type
        apiResponse.Message = GetErrorMessage(exception);

        return JsonSerializer.Serialize(apiResponse, _jsonOptions);
    }

    public override bool ShouldFormat(object? context = null)
    {
        if (!Options.WrapAllResponses)
            return false;

        if (context is HttpContext httpContext)
        {
            var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? "";
            var contentType = httpContext.Response.ContentType?.ToLowerInvariant() ?? "";

            // Check excluded paths
            if (Options.ExcludedPaths.Any(excludedPath =>
                path.StartsWith(excludedPath.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // Check excluded content types
            if (Options.ExcludedContentTypes.Any(excludedType =>
            {
                if (excludedType.EndsWith("*"))
                {
                    var prefix = excludedType.TrimEnd('*');
                    return contentType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }
                return contentType.Equals(excludedType, StringComparison.OrdinalIgnoreCase);
            }))
            {
                return false;
            }

            // Only format JSON responses by default
            if (string.IsNullOrEmpty(contentType) || contentType.Contains("application/json"))
            {
                return true;
            }
        }

        return false;
    }

    private string GetErrorMessage(Exception exception)
    {
        return exception switch
        {
            ValidationExceptionAlias => "Validation failed",
            UnauthorizedAccessException => "Access denied",
            ArgumentException => "Invalid request",
            InvalidOperationException => "Invalid operation",
            TimeoutException => "Request timeout",
            _ => Options.DefaultErrorMessage
        };
    }
}

public class HttpResponseMetadataProvider : IResponseMetadataProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpResponseMetadataProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ResponseMetadata CreateMetadata(object? context = null)
    {
        var httpContext = context as HttpContext ?? _httpContextAccessor.HttpContext;

        var metadata = new ResponseMetadata();

        if (httpContext != null)
        {
            // Extract correlation ID from headers or generate new one
            metadata.CorrelationId = httpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault() ??
                                   httpContext.Request.Headers["Correlation-ID"].FirstOrDefault() ??
                                   httpContext.TraceIdentifier;

            // Extract request ID
            metadata.RequestId = httpContext.Request.Headers["X-Request-ID"].FirstOrDefault() ??
                               httpContext.TraceIdentifier;

            // Extract user ID from claims
            metadata.UserId = httpContext.User?.Identity?.Name ??
                            httpContext.User?.FindFirst("sub")?.Value ??
                            httpContext.User?.FindFirst("user_id")?.Value;

            // Add custom properties from headers
            foreach (var header in httpContext.Request.Headers.Where(h => h.Key.StartsWith("X-Custom-")))
            {
                var key = header.Key.Substring("X-Custom-".Length);
                metadata.CustomProperties[key] = header.Value.ToString();
            }
        }
        else
        {
            // Fallback when no HTTP context available
            metadata.CorrelationId = Guid.NewGuid().ToString();
            metadata.RequestId = Guid.NewGuid().ToString();
        }

        return metadata;
    }
}