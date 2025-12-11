using Microsoft.Extensions.Logging;
using Conductor.Transport;
using ValidationExceptionAlias = Conductor.Core.ValidationException;
using System.Text.Json;
using Conductor.Core;
using Conductor.Interfaces;

namespace Conductor.Transport.MessageQueue;

public class MessageQueueFormatter : BaseResponseFormatter<MessageQueueResponse>
{
    private readonly JsonSerializerOptions _jsonOptions;

    public MessageQueueFormatter(
        ResponseFormattingOptions options,
        IResponseMetadataProvider metadataProvider,
        ILogger<MessageQueueFormatter> logger,
        JsonSerializerOptions? jsonOptions = null)
        : base(options, metadataProvider, logger)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public override async Task<MessageQueueResponse> FormatSuccessAsync<T>(T data, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default)
    {
        var responseMetadata = GetOrCreateMetadata(metadata);

        var response = new MessageQueueResponse
        {
            Success = true,
            Data = data,
            Message = Options.SuccessMessage,
            MessageType = typeof(T).Name,
            RoutingKey = GenerateRoutingKey(typeof(T), true),
            Metadata = Options.IncludeTimestamp || Options.IncludeCorrelationId || Options.IncludeRequestId ? responseMetadata : null
        };

        // Add metadata to headers
        if (Options.IncludeCorrelationId && !string.IsNullOrEmpty(responseMetadata.CorrelationId))
        {
            response.Headers["correlation-id"] = responseMetadata.CorrelationId;
        }

        if (Options.IncludeRequestId && !string.IsNullOrEmpty(responseMetadata.RequestId))
        {
            response.Headers["request-id"] = responseMetadata.RequestId;
        }

        if (Options.IncludeTimestamp)
        {
            response.Headers["timestamp"] = responseMetadata.Timestamp.ToString("O");
        }

        if (Options.IncludeUserId && !string.IsNullOrEmpty(responseMetadata.UserId))
        {
            response.Headers["user-id"] = responseMetadata.UserId;
        }

        // Add custom properties
        foreach (var kvp in responseMetadata.CustomProperties)
        {
            response.Headers[$"custom-{kvp.Key}"] = kvp.Value;
        }

        return response;
    }

    public override async Task<MessageQueueResponse> FormatErrorAsync(Exception exception, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default)
    {
        var responseMetadata = GetOrCreateMetadata(metadata);

        LogException(exception, responseMetadata);

        var response = new MessageQueueResponse
        {
            Success = false,
            Message = GetErrorMessage(exception),
            Errors = GetErrorMessages(exception),
            MessageType = "Error",
            RoutingKey = GenerateRoutingKey(exception.GetType(), false),
            Metadata = Options.IncludeTimestamp || Options.IncludeCorrelationId || Options.IncludeRequestId ? responseMetadata : null
        };

        // Add metadata to headers
        if (Options.IncludeCorrelationId && !string.IsNullOrEmpty(responseMetadata.CorrelationId))
        {
            response.Headers["correlation-id"] = responseMetadata.CorrelationId;
        }

        if (Options.IncludeRequestId && !string.IsNullOrEmpty(responseMetadata.RequestId))
        {
            response.Headers["request-id"] = responseMetadata.RequestId;
        }

        response.Headers["error-type"] = exception.GetType().Name;

        return response;
    }

    public override bool ShouldFormat(object? context = null)
    {
        // Message queue always formats responses
        return true;
    }

    private string GenerateRoutingKey(Type type, bool isSuccess)
    {
        var prefix = isSuccess ? "response.success" : "response.error";
        var typeName = type.Name.ToLowerInvariant();
        return $"{prefix}.{typeName}";
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

    private List<string> GetErrorMessages(Exception exception)
    {
        var errors = new List<string> { exception.Message };

        if (Options.IncludeStackTrace && !string.IsNullOrEmpty(exception.StackTrace))
        {
            errors.Add($"Stack Trace: {exception.StackTrace}");
        }

        var innerException = exception.InnerException;
        while (innerException != null)
        {
            errors.Add($"Inner Exception: {innerException.Message}");
            innerException = innerException.InnerException;
        }

        return errors;
    }
}

public class MessageQueueMetadataProvider : IResponseMetadataProvider
{
    public ResponseMetadata CreateMetadata(object? context = null)
    {
        var metadata = new ResponseMetadata
        {
            CorrelationId = Guid.NewGuid().ToString(),
            RequestId = Guid.NewGuid().ToString()
        };

        if (context is Dictionary<string, object> messageHeaders)
        {
            if (messageHeaders.TryGetValue("correlation-id", out var correlationId))
            {
                metadata.CorrelationId = correlationId.ToString();
            }

            if (messageHeaders.TryGetValue("request-id", out var requestId))
            {
                metadata.RequestId = requestId.ToString();
            }

            if (messageHeaders.TryGetValue("user-id", out var userId))
            {
                metadata.UserId = userId.ToString();
            }

            // Add custom properties
            foreach (var kvp in messageHeaders.Where(h => h.Key.StartsWith("custom-")))
            {
                var key = kvp.Key.Substring("custom-".Length);
                metadata.CustomProperties[key] = kvp.Value;
            }
        }

        return metadata;
    }
}

// Example message queue service integration
public interface IMessageQueuePublisher
{
    Task PublishAsync<T>(T message, string? routingKey = null, Dictionary<string, object>? headers = null, CancellationToken cancellationToken = default);
    Task PublishResponseAsync<T>(T data, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default);
    Task PublishErrorAsync(Exception exception, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default);
}

public class MessageQueueConductorService
{
    private readonly IConductor _conductor;
    private readonly MessageQueueFormatter _responseFormatter;
    private readonly IMessageQueuePublisher _publisher;
    private readonly ILogger<MessageQueueConductorService> _logger;

    public MessageQueueConductorService(
        IConductor conductor,
        MessageQueueFormatter responseFormatter,
        IMessageQueuePublisher publisher,
        ILogger<MessageQueueConductorService> logger)
    {
        _conductor = conductor;
        _responseFormatter = responseFormatter;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task HandleMessageAsync<TRequest, TResponse>(
        TRequest request,
        Dictionary<string, object>? messageHeaders = null,
        CancellationToken cancellationToken = default)
        where TRequest : Core.BaseRequest
    {
        try
        {
            _logger.LogInformation("Processing message request {RequestType}", typeof(TRequest).Name);

            var response = await _conductor.Send<TResponse>(request, cancellationToken);

            var formattedResponse = await _responseFormatter.FormatSuccessAsync(response, null, cancellationToken);

            await _publisher.PublishAsync(formattedResponse, formattedResponse.RoutingKey, formattedResponse.Headers, cancellationToken);

            _logger.LogInformation("Successfully processed and published response for {RequestType}", typeof(TRequest).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message request {RequestType}", typeof(TRequest).Name);

            var errorResponse = await _responseFormatter.FormatErrorAsync(ex, null, cancellationToken);

            await _publisher.PublishAsync(errorResponse, errorResponse.RoutingKey, errorResponse.Headers, cancellationToken);
        }
    }
}

// Example RabbitMQ implementation
public class RabbitMqPublisher : IMessageQueuePublisher
{
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync<T>(T message, string? routingKey = null, Dictionary<string, object>? headers = null, CancellationToken cancellationToken = default)
    {
        // Implementation would use RabbitMQ client
        _logger.LogInformation("Publishing message to RabbitMQ with routing key {RoutingKey}", routingKey);

        // Placeholder for actual RabbitMQ publishing logic
        await Task.CompletedTask;
    }

    public async Task PublishResponseAsync<T>(T data, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default)
    {
        var routingKey = $"response.success.{typeof(T).Name.ToLowerInvariant()}";
        var headers = new Dictionary<string, object>();

        if (metadata != null)
        {
            if (!string.IsNullOrEmpty(metadata.CorrelationId))
                headers["correlation-id"] = metadata.CorrelationId;

            if (!string.IsNullOrEmpty(metadata.RequestId))
                headers["request-id"] = metadata.RequestId;
        }

        await PublishAsync(data, routingKey, headers, cancellationToken);
    }

    public async Task PublishErrorAsync(Exception exception, ResponseMetadata? metadata = null, CancellationToken cancellationToken = default)
    {
        var errorData = new
        {
            Error = exception.Message,
            Type = exception.GetType().Name,
            Timestamp = DateTime.UtcNow
        };

        var routingKey = $"response.error.{exception.GetType().Name.ToLowerInvariant()}";
        var headers = new Dictionary<string, object>
        {
            ["error-type"] = exception.GetType().Name
        };

        if (metadata != null)
        {
            if (!string.IsNullOrEmpty(metadata.CorrelationId))
                headers["correlation-id"] = metadata.CorrelationId;

            if (!string.IsNullOrEmpty(metadata.RequestId))
                headers["request-id"] = metadata.RequestId;
        }

        await PublishAsync(errorData, routingKey, headers, cancellationToken);
    }
}