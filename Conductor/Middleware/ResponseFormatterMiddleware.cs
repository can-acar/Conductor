using System.Diagnostics;
using System.Text.Json;
using Conductor.Core;
using Conductor.Interfaces;
using Conductor.Transport;
using Conductor.Transport.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace Conductor.Middleware;

public class ResponseFormatterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ResponseFormatterMiddleware> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public ResponseFormatterMiddleware(RequestDelegate next, ILogger<ResponseFormatterMiddleware> logger)
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
        var stopwatch = Stopwatch.StartNew();
        var originalBodyStream = context.Response.Body;

        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
            stopwatch.Stop();

            // Reset position to read response
            responseBody.Seek(0, SeekOrigin.Begin);

            // Check if response should be formatted
            if (ShouldFormatResponse(context, responseBody))
            {
                await FormatResponse(context, responseBody, stopwatch.ElapsedMilliseconds, originalBodyStream);
            }
            else
            {
                // Copy the original response back
                await responseBody.CopyToAsync(originalBodyStream);
            }
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private bool ShouldFormatResponse(HttpContext context, MemoryStream responseBody)
    {
        // Only format API responses (JSON)
        if (!context.Response.ContentType?.Contains("application/json") == true)
            return false;

        // Only format successful responses and let GlobalExceptionMiddleware handle errors
        if (context.Response.StatusCode < 200 || context.Response.StatusCode >= 300)
            return false;

        // Skip if no content
        if (responseBody.Length == 0)
            return false;

        // Check if already wrapped in ApiResponse
        responseBody.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(responseBody, leaveOpen: true);
        var content = reader.ReadToEnd();

        // Check if already wrapped in ApiResponse
        if (content.Contains("\"success\"") && (content.Contains("\"data\"") || content.Contains("\"error\"")))
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            return false;
        }

        responseBody.Seek(0, SeekOrigin.Begin);
        return true;
    }

    private async Task FormatResponse(HttpContext context, MemoryStream responseBody, long executionTimeMs, Stream originalBodyStream)
    {
        // Read the response content
        responseBody.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(responseBody, leaveOpen: true);
        var responseContent = await reader.ReadToEndAsync();

        object? data = null;

        // Parse existing JSON response
        if (!string.IsNullOrEmpty(responseContent))
        {
            try
            {
                data = JsonSerializer.Deserialize<object>(responseContent, _jsonOptions);
            }
            catch
            {
                // If parsing fails, treat as string
                data = responseContent;
            }
        }

        // Create standardized response
        var apiResponse = CreateStandardResponse(context, data, executionTimeMs);
        var formattedJson = JsonSerializer.Serialize(apiResponse, _jsonOptions);

        // Write formatted response to original stream
        context.Response.ContentLength = null;
        await originalBodyStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(formattedJson));
    }

    private ApiResponse<object> CreateStandardResponse(HttpContext context, object? data, long executionTimeMs)
    {
        var metadata = new ResponseMetadata
        {
            RequestId = context.TraceIdentifier,
            CorrelationId = context.TraceIdentifier,
            Timestamp = DateTime.UtcNow
        };
        metadata.CustomProperties["ExecutionTimeMs"] = executionTimeMs;

        // Add pagination info if present in headers
        if (context.Response.Headers.ContainsKey("X-Pagination"))
        {
            try
            {
                var paginationJson = context.Response.Headers["X-Pagination"].FirstOrDefault();
                if (!string.IsNullOrEmpty(paginationJson))
                {
                    metadata.CustomProperties["Pagination"] = JsonSerializer.Deserialize<PaginationInfo>(paginationJson, _jsonOptions)!;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse pagination header");
            }
        }

        return ApiResponse<object>.CreateSuccess(data ?? new object(), metadata: metadata);
    }
}

public static class ResponseFormatterMiddlewareExtensions
{
    public static IApplicationBuilder UseResponseFormatter(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ResponseFormatterMiddleware>();
    }
}

// Extension methods for controllers to add pagination info
public static class HttpContextExtensions
{
    public static void SetPaginationHeader(this HttpContext context, PaginationInfo pagination)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var paginationJson = JsonSerializer.Serialize(pagination, jsonOptions);
        context.Response.Headers["X-Pagination"] = paginationJson;
    }

    public static void SetExecutionTime(this HttpContext context, long milliseconds)
    {
        context.Response.Headers["X-Execution-Time"] = milliseconds.ToString();
    }
}