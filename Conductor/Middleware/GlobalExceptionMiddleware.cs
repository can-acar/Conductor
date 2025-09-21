using System.Net;
using System.Text.Json;
using Conductor.Attributes;
using Conductor.Transport;
using Conductor.Transport.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace Conductor.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var (message, errors) = exception switch
        {
            ValidationException validationEx => HandleValidationException(validationEx, ref response),
            ArgumentException argEx => HandleArgumentException(argEx, ref response),
            UnauthorizedAccessException => HandleUnauthorizedException(ref response),
            KeyNotFoundException => HandleNotFoundException(ref response),
            TimeoutException => HandleTimeoutException(ref response),
            InvalidOperationException invalidOpEx => HandleInvalidOperationException(invalidOpEx, ref response),
            _ => HandleGenericException(exception, ref response)
        };

        var metadata = new ResponseMetadata
        {
            RequestId = context.TraceIdentifier,
            CorrelationId = context.TraceIdentifier,
            Timestamp = DateTime.UtcNow
        };

        var apiResponse = ApiResponse<object>.CreateError(message, errors, metadata);
        var jsonResponse = JsonSerializer.Serialize(apiResponse, _jsonOptions);
        await response.WriteAsync(jsonResponse);
    }

    private static (string message, List<string> errors) HandleValidationException(ValidationException validationEx, ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.BadRequest;

        var errors = validationEx.ValidationResult.Errors
            .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
            .ToList();

        return ("Input validation failed", errors);
    }

    private static (string message, List<string> errors) HandleArgumentException(ArgumentException argEx, ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.BadRequest;
        return ("INVALID_ARGUMENT: Invalid argument provided", new List<string> { argEx.Message });
    }

    private static (string message, List<string> errors) HandleUnauthorizedException(ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.Unauthorized;
        return ("UNAUTHORIZED: Authentication required", new List<string>());
    }

    private static (string message, List<string> errors) HandleNotFoundException(ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.NotFound;
        return ("NOT_FOUND: The requested resource was not found", new List<string>());
    }

    private static (string message, List<string> errors) HandleTimeoutException(ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.RequestTimeout;
        return ("TIMEOUT: The request timed out", new List<string>());
    }

    private static (string message, List<string> errors) HandleInvalidOperationException(InvalidOperationException invalidOpEx, ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.BadRequest;
        return ("INVALID_OPERATION: Invalid operation", new List<string> { invalidOpEx.Message });
    }

    private static (string message, List<string> errors) HandleGenericException(Exception exception, ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.InternalServerError;
        var errors = new List<string> { exception.Message };
#if DEBUG
        if (!string.IsNullOrEmpty(exception.StackTrace)) errors.Add($"StackTrace: {exception.StackTrace}");
#endif
        return ("INTERNAL_ERROR: An internal server error occurred", errors);
    }
}

public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<GlobalExceptionMiddleware>();
    }
}