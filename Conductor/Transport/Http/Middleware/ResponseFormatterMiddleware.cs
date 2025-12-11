using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using Conductor.Core;
using Conductor.Interfaces;
using ValidationExceptionAlias = Conductor.Core.ValidationException;

namespace Conductor.Transport.Http.Middleware;

public class ResponseFormatterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpResponseFormatter _responseFormatter;
    private readonly ILogger<ResponseFormatterMiddleware> _logger;
    private readonly ResponseFormattingOptions _options;

    public ResponseFormatterMiddleware(
        RequestDelegate next,
        HttpResponseFormatter responseFormatter,
        ILogger<ResponseFormatterMiddleware> logger,
        IOptions<ResponseFormattingOptions> options)
    {
        _next = next;
        _responseFormatter = responseFormatter;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip if formatting not enabled or should not format this request
        if (!_responseFormatter.ShouldFormat(context))
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;

        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);

            // Only wrap successful responses (2xx status codes)
            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                await WrapSuccessResponse(context, originalBodyStream);
            }
            else
            {
                // For non-success status codes, copy original response
                await CopyOriginalResponse(context, responseBody, originalBodyStream);
            }
        }
        catch (Exception)
        {
            // Exception handling is done in GlobalExceptionMiddleware
            // This middleware only handles response formatting
            _logger.LogDebug("Exception occurred during request processing, will be handled by GlobalExceptionMiddleware");
            throw;
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private async Task WrapSuccessResponse(HttpContext context, Stream originalBodyStream)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseContent = await new StreamReader(context.Response.Body).ReadToEndAsync();

        if (string.IsNullOrEmpty(responseContent))
        {
            // Empty response - create success response without data
            var emptyResponse = await _responseFormatter.FormatSuccessAsync<object>(null!, new ResponseMetadata());
            await WriteFormattedResponse(context, emptyResponse, originalBodyStream);
            return;
        }

        try
        {
            // Try to parse existing JSON and wrap it
            var existingData = JsonSerializer.Deserialize<object>(responseContent);
            var wrappedResponse = await _responseFormatter.FormatSuccessAsync(existingData, new ResponseMetadata());
            await WriteFormattedResponse(context, wrappedResponse, originalBodyStream);
        }
        catch (JsonException)
        {
            // Not JSON content - wrap as string
            var wrappedResponse = await _responseFormatter.FormatSuccessAsync(responseContent, new ResponseMetadata());
            await WriteFormattedResponse(context, wrappedResponse, originalBodyStream);
        }
    }

    private async Task CopyOriginalResponse(HttpContext context, MemoryStream responseBody, Stream originalBodyStream)
    {
        responseBody.Seek(0, SeekOrigin.Begin);
        await responseBody.CopyToAsync(originalBodyStream);
    }

    private async Task WriteFormattedResponse(HttpContext context, string formattedResponse, Stream originalBodyStream)
    {
        var responseBytes = Encoding.UTF8.GetBytes(formattedResponse);

        context.Response.ContentLength = responseBytes.Length;
        context.Response.ContentType = "application/json; charset=utf-8";

        await originalBodyStream.WriteAsync(responseBytes);
    }
}

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpResponseFormatter _responseFormatter;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly ResponseFormattingOptions _options;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        HttpResponseFormatter responseFormatter,
        ILogger<GlobalExceptionMiddleware> logger,
        IOptions<ResponseFormattingOptions> options)
    {
        _next = next;
        _responseFormatter = responseFormatter;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "Unhandled exception occurred");

        var statusCode = GetStatusCode(exception);
        var errorResponse = await _responseFormatter.FormatErrorAsync(exception, null);

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var responseBytes = Encoding.UTF8.GetBytes(errorResponse);
        await context.Response.Body.WriteAsync(responseBytes);
    }

    private static int GetStatusCode(Exception exception)
    {
        return exception switch
        {
            ValidationExceptionAlias => 400, // Bad Request
            ArgumentException => 400, // Bad Request
            UnauthorizedAccessException => 401, // Unauthorized
            System.Security.SecurityException => 403, // Forbidden
            KeyNotFoundException => 404, // Not Found
            NotImplementedException => 501, // Not Implemented
            TimeoutException => 408, // Request Timeout
            InvalidOperationException => 409, // Conflict
            _ => 500 // Internal Server Error
        };
    }
}

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrGenerateCorrelationId(context);

        // Add to response headers
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            return Task.CompletedTask;
        });

        // Add to logging scope
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestId"] = context.TraceIdentifier
        });

        await _next(context);
    }

    private static string GetOrGenerateCorrelationId(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ??
                          context.Request.Headers["Correlation-ID"].FirstOrDefault();

        if (string.IsNullOrEmpty(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
            context.Request.Headers["X-Correlation-ID"] = correlationId;
        }

        return correlationId;
    }
}