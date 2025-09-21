using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace Conductor.Saga;

public interface ISagaTimeoutManager
{
    Task CheckTimeoutsAsync(CancellationToken cancellationToken = default);
    Task ScheduleTimeoutAsync<TSagaState>(TSagaState sagaState, CancellationToken cancellationToken = default) where TSagaState : ISagaState;
    Task CancelTimeoutAsync(Guid sagaId, CancellationToken cancellationToken = default);
}

public class SagaTimeoutManager : BackgroundService, ISagaTimeoutManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SagaTimeoutManager> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly ConcurrentDictionary<Guid, SagaTimeout> _timeouts = new();

    public SagaTimeoutManager(
        IServiceProvider serviceProvider,
        ILogger<SagaTimeoutManager> logger,
        TimeSpan? checkInterval = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromMinutes(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Saga timeout manager started with check interval {Interval}", _checkInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckTimeoutsAsync(stoppingToken);
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking saga timeouts");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("Saga timeout manager stopped");
    }

    public async Task CheckTimeoutsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var timedOutSagas = new List<SagaTimeout>();

        // Check in-memory timeouts
        foreach (var kvp in _timeouts)
        {
            if (kvp.Value.TimeoutAt <= now)
            {
                timedOutSagas.Add(kvp.Value);
            }
        }

        // Check persisted sagas for timeouts
        using var scope = _serviceProvider.CreateScope();
        await CheckPersistedSagaTimeouts(scope.ServiceProvider, now, cancellationToken);

        // Process timed out sagas
        foreach (var timeout in timedOutSagas)
        {
            try
            {
                await ProcessTimeoutAsync(timeout, cancellationToken);
                _timeouts.TryRemove(timeout.SagaId, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process timeout for saga {SagaId}", timeout.SagaId);
            }
        }

        if (timedOutSagas.Any())
        {
            _logger.LogInformation("Processed {Count} saga timeouts", timedOutSagas.Count);
        }
    }

    public Task ScheduleTimeoutAsync<TSagaState>(TSagaState sagaState, CancellationToken cancellationToken = default) where TSagaState : ISagaState
    {
        ArgumentNullException.ThrowIfNull(sagaState);

        if (!sagaState.Metadata.Timeout.HasValue)
        {
            return Task.CompletedTask;
        }

        var timeoutAt = sagaState.CreatedAt.Add(sagaState.Metadata.Timeout.Value);
        var timeout = new SagaTimeout
        {
            SagaId = sagaState.SagaId,
            SagaType = sagaState.SagaType,
            TimeoutAt = timeoutAt,
            TimeoutAction = sagaState.Metadata.TimeoutAction ?? "Abort"
        };

        _timeouts.AddOrUpdate(sagaState.SagaId, timeout, (key, existing) => timeout);

        _logger.LogDebug("Scheduled timeout for saga {SagaId} at {TimeoutAt}",
            sagaState.SagaId, timeoutAt);

        return Task.CompletedTask;
    }

    public Task CancelTimeoutAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        if (_timeouts.TryRemove(sagaId, out var timeout))
        {
            _logger.LogDebug("Cancelled timeout for saga {SagaId}", sagaId);
        }

        return Task.CompletedTask;
    }

    private async Task CheckPersistedSagaTimeouts(IServiceProvider serviceProvider, DateTime now, CancellationToken cancellationToken)
    {
        // This is a generic approach - in practice you'd register specific persistence types
        var persistenceTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType &&
                                                  i.GetGenericTypeDefinition() == typeof(ISagaPersistence<>)))
            .ToList();

        foreach (var persistenceType in persistenceTypes)
        {
            try
            {
                var persistence = serviceProvider.GetService(persistenceType);
                if (persistence != null)
                {
                    await CheckPersistenceForTimeouts(persistence, now, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking timeouts for persistence type {Type}", persistenceType.Name);
            }
        }
    }

    private async Task CheckPersistenceForTimeouts(object persistence, DateTime now, CancellationToken cancellationToken)
    {
        // Use reflection to call GetTimeoutedSagasAsync
        var method = persistence.GetType().GetMethod("GetTimeoutedSagasAsync");
        if (method != null)
        {
            var task = method.Invoke(persistence, new object[] { now, cancellationToken }) as Task;
            if (task != null)
            {
                await task;

                // Get the result
                var resultProperty = task.GetType().GetProperty("Result");
                if (resultProperty?.GetValue(task) is IEnumerable<ISagaState> timedOutSagas)
                {
                    foreach (var sagaState in timedOutSagas)
                    {
                        await ProcessSagaTimeoutAsync(sagaState, cancellationToken);
                    }
                }
            }
        }
    }

    private async Task ProcessTimeoutAsync(SagaTimeout timeout, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Processing timeout for saga {SagaId} with action {Action}",
            timeout.SagaId, timeout.TimeoutAction);

        // Find the appropriate orchestrator for this saga type
        var orchestratorType = typeof(ISagaOrchestrator<>);
        var sagaStateType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name.Contains(timeout.SagaType) && typeof(ISagaState).IsAssignableFrom(t));

        if (sagaStateType != null)
        {
            var genericOrchestratorType = orchestratorType.MakeGenericType(sagaStateType);
            using var scope = _serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetService(genericOrchestratorType);

            if (orchestrator != null)
            {
                // Get the saga state first
                var persistenceType = typeof(ISagaPersistence<>).MakeGenericType(sagaStateType);
                var persistence = scope.ServiceProvider.GetService(persistenceType);

                if (persistence != null)
                {
                    var getAsyncMethod = persistenceType.GetMethod("GetAsync");
                    var getSagaTask = getAsyncMethod?.Invoke(persistence, new object[] { timeout.SagaId, cancellationToken }) as Task;

                    if (getSagaTask != null)
                    {
                        await getSagaTask;
                        var sagaState = getSagaTask.GetType().GetProperty("Result")?.GetValue(getSagaTask);

                        if (sagaState != null)
                        {
                            await InvokeTimeoutHandler(orchestrator, sagaState, cancellationToken);
                        }
                    }
                }
            }
        }
    }

    private async Task ProcessSagaTimeoutAsync(ISagaState sagaState, CancellationToken cancellationToken)
    {
        var timeout = new SagaTimeout
        {
            SagaId = sagaState.SagaId,
            SagaType = sagaState.SagaType,
            TimeoutAt = DateTime.UtcNow,
            TimeoutAction = sagaState.Metadata.TimeoutAction ?? "Abort"
        };

        await ProcessTimeoutAsync(timeout, cancellationToken);
    }

    private async Task InvokeTimeoutHandler(object orchestrator, object sagaState, CancellationToken cancellationToken)
    {
        var handleTimeoutMethod = orchestrator.GetType().GetMethod("HandleTimeoutAsync");
        if (handleTimeoutMethod != null)
        {
            var task = handleTimeoutMethod.Invoke(orchestrator, new[] { sagaState, cancellationToken }) as Task;
            if (task != null)
            {
                await task;
            }
        }
    }

    private class SagaTimeout
    {
        public Guid SagaId { get; set; }
        public string SagaType { get; set; } = string.Empty;
        public DateTime TimeoutAt { get; set; }
        public string TimeoutAction { get; set; } = string.Empty;
    }
}

public class SagaRetryManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SagaRetryManager> _logger;
    private readonly SagaRetryPolicy _defaultRetryPolicy;

    public SagaRetryManager(
        IServiceProvider serviceProvider,
        ILogger<SagaRetryManager> logger,
        SagaRetryPolicy? defaultRetryPolicy = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _defaultRetryPolicy = defaultRetryPolicy ?? new SagaRetryPolicy();
    }

    public async Task<SagaStepResult> ExecuteWithRetryAsync<TSagaState>(
        ISagaStepHandler<TSagaState> handler,
        TSagaState sagaState,
        SagaRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default) where TSagaState : ISagaState
    {
        var policy = retryPolicy ?? _defaultRetryPolicy;
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < policy.MaxRetries)
        {
            try
            {
                _logger.LogDebug("Executing step {StepName} for saga {SagaId}, attempt {Attempt}",
                    handler.StepName, sagaState.SagaId, attempt + 1);

                var result = await handler.ExecuteAsync(sagaState, cancellationToken);

                if (result.IsSuccess || !result.ShouldRetry)
                {
                    return result;
                }

                lastException = new InvalidOperationException(result.ErrorMessage);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Step {StepName} failed for saga {SagaId}, attempt {Attempt}",
                    handler.StepName, sagaState.SagaId, attempt + 1);
            }

            attempt++;

            if (attempt < policy.MaxRetries)
            {
                var delay = policy.CalculateDelay(attempt);
                _logger.LogDebug("Retrying step {StepName} for saga {SagaId} in {Delay}ms",
                    handler.StepName, sagaState.SagaId, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        _logger.LogError(lastException, "Step {StepName} failed for saga {SagaId} after {Attempts} attempts",
            handler.StepName, sagaState.SagaId, attempt);

        return SagaStepResult.Failure($"Failed after {attempt} attempts: {lastException?.Message}");
    }
}

public class SagaRetryPolicy
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public RetryBackoffStrategy BackoffStrategy { get; set; } = RetryBackoffStrategy.Exponential;
    public double BackoffMultiplier { get; set; } = 2.0;
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
    public double JitterFactor { get; set; } = 0.1;

    public TimeSpan CalculateDelay(int attempt)
    {
        var delay = BackoffStrategy switch
        {
            RetryBackoffStrategy.Fixed => BaseDelay,
            RetryBackoffStrategy.Linear => TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * attempt),
            RetryBackoffStrategy.Exponential => TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attempt - 1)),
            _ => BaseDelay
        };

        // Apply jitter
        if (JitterFactor > 0)
        {
            var jitter = delay.TotalMilliseconds * JitterFactor * (Random.Shared.NextDouble() - 0.5);
            delay = delay.Add(TimeSpan.FromMilliseconds(jitter));
        }

        // Ensure delay doesn't exceed maximum
        if (delay > MaxDelay)
        {
            delay = MaxDelay;
        }

        return delay;
    }
}

public enum RetryBackoffStrategy
{
    Fixed,
    Linear,
    Exponential
}

public class SagaCircuitBreaker
{
    private readonly string _sagaType;
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly ILogger<SagaCircuitBreaker> _logger;

    private int _failureCount;
    private DateTime _lastFailureTime;
    private CircuitBreakerState _state = CircuitBreakerState.Closed;

    public SagaCircuitBreaker(
        string sagaType,
        int failureThreshold = 5,
        TimeSpan? timeout = null,
        ILogger<SagaCircuitBreaker>? logger = null)
    {
        _sagaType = sagaType;
        _failureThreshold = failureThreshold;
        _timeout = timeout ?? TimeSpan.FromMinutes(1);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SagaCircuitBreaker>.Instance;
    }

    public async Task<SagaStepResult> ExecuteAsync<TSagaState>(
        Func<Task<SagaStepResult>> operation,
        TSagaState sagaState) where TSagaState : ISagaState
    {
        if (_state == CircuitBreakerState.Open)
        {
            if (DateTime.UtcNow - _lastFailureTime > _timeout)
            {
                _state = CircuitBreakerState.HalfOpen;
                _logger.LogInformation("Circuit breaker for {SagaType} moved to HalfOpen state", _sagaType);
            }
            else
            {
                _logger.LogWarning("Circuit breaker for {SagaType} is Open, rejecting saga {SagaId}",
                    _sagaType, sagaState.SagaId);
                return SagaStepResult.Failure("Circuit breaker is open");
            }
        }

        try
        {
            var result = await operation();

            if (result.IsSuccess)
            {
                OnSuccess();
            }
            else
            {
                OnFailure();
            }

            return result;
        }
        catch (Exception)
        {
            OnFailure();
            throw;
        }
    }

    private void OnSuccess()
    {
        _failureCount = 0;
        if (_state == CircuitBreakerState.HalfOpen)
        {
            _state = CircuitBreakerState.Closed;
            _logger.LogInformation("Circuit breaker for {SagaType} moved to Closed state", _sagaType);
        }
    }

    private void OnFailure()
    {
        _failureCount++;
        _lastFailureTime = DateTime.UtcNow;

        if (_failureCount >= _failureThreshold)
        {
            _state = CircuitBreakerState.Open;
            _logger.LogWarning("Circuit breaker for {SagaType} opened after {FailureCount} failures",
                _sagaType, _failureCount);
        }
    }

    public CircuitBreakerState State => _state;
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}