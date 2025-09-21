using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Conductor.Transport.Http;
using Conductor.Transport;

namespace Conductor.Core;

[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected IActionResult Success<T>(T data, ResponseMetadata? metadata = null)
    {
        var response = ApiResponse<T>.CreateSuccess(data, metadata: metadata);
        return Ok(response);
    }

    protected IActionResult Success(object? data = null, ResponseMetadata? metadata = null)
    {
        var response = ApiResponse.CreateSuccess(metadata: metadata);
        response.Data = data;
        return Ok(response);
    }

    protected IActionResult ValidationFailure(IEnumerable<string> errors, string? message = null)
    {
        var response = ApiResponse.CreateError(message ?? "Validation failed", errors.ToList());
        return BadRequest(response);
    }

    protected IActionResult Failure(string code, string message, string type = "Error", string? details = null)
    {
        // ApiResponse doesn't carry structured error codes; include them in the message
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(details)) errors.Add(details);
        var response = ApiResponse.CreateError($"{code}: {message} ({type})", errors);
        return BadRequest(response);
    }

    protected IActionResult NotFoundResult(string? message = null)
    {
        var response = ApiResponse.CreateError($"NOT_FOUND: {message ?? "Resource not found"}");
        return NotFound(response);
    }

    protected IActionResult UnauthorizedResult(string? message = null)
    {
        var response = ApiResponse.CreateError($"UNAUTHORIZED: {message ?? "Authentication required"}");
        return Unauthorized(response);
    }

    protected IActionResult ForbiddenResult(string? message = null)
    {
        var response = ApiResponse.CreateError($"FORBIDDEN: {message ?? "Access denied"}");
        return StatusCode(403, response);
    }

    protected ResponseMetadata CreateMetadata(long? executionTimeMs = null, PaginationInfo? pagination = null)
    {
        var metadata = new ResponseMetadata
        {
            RequestId = HttpContext.TraceIdentifier,
            Timestamp = DateTime.UtcNow,
            CorrelationId = HttpContext.TraceIdentifier
        };

        if (executionTimeMs.HasValue)
        {
            metadata.CustomProperties["ExecutionTimeMs"] = executionTimeMs.Value;
        }
        if (pagination is not null)
        {
            metadata.CustomProperties["Pagination"] = pagination;
        }
        metadata.CustomProperties["Version"] = "1.0";

        return metadata;
    }

    protected PaginationInfo CreatePaginationInfo(int page, int pageSize, long totalCount)
    {
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new PaginationInfo
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasNextPage = page < totalPages,
            HasPreviousPage = page > 1
        };
    }

    protected async Task<IActionResult> ExecuteWithTimingAsync<T>(Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await operation();
            stopwatch.Stop();

            var metadata = CreateMetadata(stopwatch.ElapsedMilliseconds);
            return Success(result, metadata);
        }
        catch (Exception)
        {
            stopwatch.Stop();
            throw; // Let global exception handler deal with it
        }
    }

    protected async Task<IActionResult> ExecuteWithTimingAsync(Func<Task> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await operation();
            stopwatch.Stop();

            var metadata = CreateMetadata(stopwatch.ElapsedMilliseconds);
            return Success(null, metadata);
        }
        catch (Exception)
        {
            stopwatch.Stop();
            throw; // Let global exception handler deal with it
        }
    }
}