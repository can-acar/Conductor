using System.Net;
using System.Text.Json;
using Conductor.Attributes;
using Conductor.Core;
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

        var apiResponse = exception switch
        {
            ValidationException validationEx => HandleValidationException(validationEx, ref response),
            ArgumentException argEx => HandleArgumentException(argEx, ref response),
            UnauthorizedAccessException => HandleUnauthorizedException(ref response),
            KeyNotFoundException => HandleNotFoundException(ref response),
            TimeoutException => HandleTimeoutException(ref response),
            InvalidOperationException invalidOpEx => HandleInvalidOperationException(invalidOpEx, ref response),
            _ => HandleGenericException(exception, ref response)
        };

        // Add request metadata
        apiResponse.Metadata = new ApiMetadata
        {
            RequestId = context.TraceIdentifier,
            Timestamp = DateTime.UtcNow,
            Version = "1.0"
        };

        var jsonResponse = JsonSerializer.Serialize(apiResponse, _jsonOptions);
        await response.WriteAsync(jsonResponse);
    }

    private static ApiResponse HandleValidationException(ValidationException validationEx, ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.BadRequest;

        var validationErrors = validationEx.ValidationResult.Errors
            .Select(ValidationErrorDetail.FromValidationError)
            .ToList();

        return ApiResponse.ValidationFailureResult(validationErrors, "Input validation failed");
    }

    private static ApiResponse HandleArgumentException(ArgumentException argEx, ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.BadRequest;

        return ApiResponse.FailureResult(new ApiError
        {
            Code = "INVALID_ARGUMENT",
            Message = "Invalid argument provided",
            Type = "ArgumentError",
            Details = argEx.Message
        });
    }

    private static ApiResponse HandleUnauthorizedException(ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.Unauthorized;

        return ApiResponse.FailureResult(new ApiError
        {
            Code = "UNAUTHORIZED",
            Message = "Authentication required",
            Type = "AuthenticationError"
        });
    }

    private static ApiResponse HandleNotFoundException(ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.NotFound;

        return ApiResponse.FailureResult(new ApiError
        {
            Code = "NOT_FOUND",
            Message = "The requested resource was not found",
            Type = "NotFoundError"
        });
    }

    private static ApiResponse HandleTimeoutException(ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.RequestTimeout;

        return ApiResponse.FailureResult(new ApiError
        {
            Code = "TIMEOUT",
            Message = "The request timed out",
            Type = "TimeoutError"
        });
    }

    private static ApiResponse HandleInvalidOperationException(InvalidOperationException invalidOpEx, ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.BadRequest;

        return ApiResponse.FailureResult(new ApiError
        {
            Code = "INVALID_OPERATION",
            Message = "Invalid operation",
            Type = "InvalidOperationError",
            Details = invalidOpEx.Message
        });
    }

    private static ApiResponse HandleGenericException(Exception exception, ref HttpResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.InternalServerError;

        return ApiResponse.FailureResult(new ApiError
        {
            Code = "INTERNAL_ERROR",
            Message = "An internal server error occurred",
            Type = "InternalServerError",
            Details = exception.Message,
#if DEBUG
            StackTrace = exception.StackTrace
#endif
        });
    }
}

public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<GlobalExceptionMiddleware>();
    }
}