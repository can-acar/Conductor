using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Conductor.Attributes;
using Conductor.Core;
using Conductor.Validation;

namespace Conductor.Pipeline;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : BaseRequest
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var requestId = request.CorrelationId ?? Guid.NewGuid().ToString();

        _logger.LogInformation("Handling {RequestName} with ID {RequestId} for User {UserId}",
            requestName, requestId, request.UserId);

        try
        {
            var response = await next();

            _logger.LogInformation("Successfully handled {RequestName} with ID {RequestId}",
                requestName, requestId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {RequestName} with ID {RequestId}: {ErrorMessage}",
                requestName, requestId, ex.Message);
            throw;
        }
    }
}

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : BaseRequest
{
    private readonly IEnumerable<Conductor.Validation.IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(IEnumerable<Conductor.Validation.IValidator<TRequest>> validators, ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_validators.Any())
        {
            var validationResults = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(request, cancellationToken)));
            var failures = validationResults.SelectMany(r => r.Errors).Where(f => f != null).ToList();

            if (failures.Any())
            {
                _logger.LogWarning("Validation failed for {RequestName}: {ValidationErrors}",
                    typeof(TRequest).Name, string.Join(", ", failures.Select(f => f.ErrorMessage)));

                throw new Conductor.Validation.ValidationException(Conductor.Validation.ValidationResult.Failure(failures.ToArray()));
            }
        }

        return await next();
    }
}

public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : BaseRequest
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly TimeSpan _warningThreshold;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger, TimeSpan? warningThreshold = null)
    {
        _logger = logger;
        _warningThreshold = warningThreshold ?? TimeSpan.FromMilliseconds(500);
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestName = typeof(TRequest).Name;

        try
        {
            var response = await next();
            stopwatch.Stop();

            if (stopwatch.Elapsed > _warningThreshold)
            {
                _logger.LogWarning("Slow request detected: {RequestName} took {ElapsedMs}ms (Threshold: {ThresholdMs}ms)",
                    requestName, stopwatch.ElapsedMilliseconds, _warningThreshold.TotalMilliseconds);
            }
            else
            {
                _logger.LogDebug("Request {RequestName} completed in {ElapsedMs}ms",
                    requestName, stopwatch.ElapsedMilliseconds);
            }

            // Store performance metrics in context
            if (PipelineContextExtensions.Current != null)
            {
                PipelineContextExtensions.Current.SetItem("ExecutionTime", stopwatch.Elapsed);
                PipelineContextExtensions.Current.SetItem("RequestName", requestName);
            }

            return response;
        }
        catch (Exception)
        {
            stopwatch.Stop();
            _logger.LogWarning("Failed request {RequestName} took {ElapsedMs}ms before failure",
                requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}

public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : BaseRequest, ICacheableRequest
{
    private readonly ICacheService _cacheService;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(ICacheService cacheService, ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var cacheKey = request.GetCacheKey();

        if (!string.IsNullOrEmpty(cacheKey))
        {
            var cachedResponse = await _cacheService.GetAsync<TResponse>(cacheKey, cancellationToken);
            if (cachedResponse != null)
            {
                _logger.LogDebug("Cache hit for {RequestName} with key {CacheKey}",
                    typeof(TRequest).Name, cacheKey);
                return cachedResponse;
            }
        }

        var response = await next();

        if (!string.IsNullOrEmpty(cacheKey) && response != null)
        {
            await _cacheService.SetAsync(cacheKey, response, request.GetCacheDuration(), cancellationToken);
            _logger.LogDebug("Cached response for {RequestName} with key {CacheKey} for {Duration}",
                typeof(TRequest).Name, cacheKey, request.GetCacheDuration());
        }

        return response;
    }
}

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : BaseRequest, ITransactionalRequest
{
    private readonly ITransactionService _transactionService;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(ITransactionService transactionService, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _transactionService = transactionService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request.RequiresTransaction)
        {
            _logger.LogDebug("Starting transaction for {RequestName}", typeof(TRequest).Name);

            await using var transaction = await _transactionService.BeginTransactionAsync(cancellationToken);
            try
            {
                var response = await next();
                await transaction.CommitAsync(cancellationToken);

                _logger.LogDebug("Transaction committed for {RequestName}", typeof(TRequest).Name);
                return response;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogWarning(ex, "Transaction rolled back for {RequestName}", typeof(TRequest).Name);
                throw;
            }
        }

        return await next();
    }
}

public class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : BaseRequest, IAuthorizedRequest
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ILogger<AuthorizationBehavior<TRequest, TResponse>> _logger;

    public AuthorizationBehavior(IAuthorizationService authorizationService, ILogger<AuthorizationBehavior<TRequest, TResponse>> logger)
    {
        _authorizationService = authorizationService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requiredPermissions = request.GetRequiredPermissions();

        if (requiredPermissions.Any())
        {
            var isAuthorized = await _authorizationService.IsAuthorizedAsync(
                request.UserId,
                requiredPermissions,
                cancellationToken);

            if (!isAuthorized)
            {
                _logger.LogWarning("Authorization failed for user {UserId} on {RequestName}. Required permissions: {Permissions}",
                    request.UserId, typeof(TRequest).Name, string.Join(", ", requiredPermissions));

                throw new UnauthorizedAccessException($"Insufficient permissions for {typeof(TRequest).Name}");
            }

            _logger.LogDebug("Authorization successful for user {UserId} on {RequestName}",
                request.UserId, typeof(TRequest).Name);
        }

        return await next();
    }
}

public class AuditingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : BaseRequest, IAuditableRequest
{
    private readonly IAuditService _auditService;
    private readonly ILogger<AuditingBehavior<TRequest, TResponse>> _logger;

    public AuditingBehavior(IAuditService auditService, ILogger<AuditingBehavior<TRequest, TResponse>> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var auditRecord = new AuditRecord
        {
            UserId = request.UserId,
            Action = typeof(TRequest).Name,
            Timestamp = DateTime.UtcNow,
            CorrelationId = request.CorrelationId,
            Details = request.GetAuditDetails()
        };

        try
        {
            var response = await next();

            auditRecord.Status = AuditStatus.Success;
            auditRecord.Response = response?.ToString();

            await _auditService.LogAsync(auditRecord, cancellationToken);

            _logger.LogDebug("Audit logged for successful {RequestName} by user {UserId}",
                typeof(TRequest).Name, request.UserId);

            return response;
        }
        catch (Exception ex)
        {
            auditRecord.Status = AuditStatus.Failed;
            auditRecord.ErrorMessage = ex.Message;

            await _auditService.LogAsync(auditRecord, cancellationToken);

            _logger.LogWarning("Audit logged for failed {RequestName} by user {UserId}: {Error}",
                typeof(TRequest).Name, request.UserId, ex.Message);

            throw;
        }
    }
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan duration, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public interface ITransactionService
{
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

public interface IAuthorizationService
{
    Task<bool> IsAuthorizedAsync(string userId, IEnumerable<string> permissions, CancellationToken cancellationToken = default);
}

public interface IAuditService
{
    Task LogAsync(AuditRecord record, CancellationToken cancellationToken = default);
}

public class AuditRecord
{
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? CorrelationId { get; set; }
    public string? Details { get; set; }
    public AuditStatus Status { get; set; }
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum AuditStatus
{
    Success,
    Failed
}