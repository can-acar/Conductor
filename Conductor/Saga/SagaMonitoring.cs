using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Conductor.Saga;

public interface ISagaMonitor
{
    Task TrackSagaStartedAsync(ISagaState sagaState, CancellationToken cancellationToken = default);
    Task TrackSagaCompletedAsync(ISagaState sagaState, CancellationToken cancellationToken = default);
    Task TrackSagaFailedAsync(ISagaState sagaState, string reason, CancellationToken cancellationToken = default);
    Task TrackStepStartedAsync(ISagaState sagaState, string stepName, CancellationToken cancellationToken = default);
    Task TrackStepCompletedAsync(ISagaState sagaState, string stepName, TimeSpan duration, CancellationToken cancellationToken = default);
    Task TrackStepFailedAsync(ISagaState sagaState, string stepName, string error, CancellationToken cancellationToken = default);
    Task<SagaHealthReport> GetHealthReportAsync(CancellationToken cancellationToken = default);
    Task<SagaPerformanceMetrics> GetPerformanceMetricsAsync(string? sagaType = null, CancellationToken cancellationToken = default);
}

public class SagaMonitor : BackgroundService, ISagaMonitor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SagaMonitor> _logger;
    private readonly ISagaEventPublisher? _eventPublisher;
    private readonly SagaMonitoringOptions _options;

    private readonly ConcurrentDictionary<Guid, SagaExecutionMetrics> _activeMetrics = new();
    private readonly ConcurrentQueue<SagaMetricSnapshot> _metricHistory = new();
    private readonly ConcurrentDictionary<string, SagaTypeMetrics> _typeMetrics = new();

    public SagaMonitor(
        IServiceProvider serviceProvider,
        ILogger<SagaMonitor> logger,
        ISagaEventPublisher? eventPublisher = null,
        SagaMonitoringOptions? options = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _eventPublisher = eventPublisher;
        _options = options ?? new SagaMonitoringOptions();
    }

    public Task TrackSagaStartedAsync(ISagaState sagaState, CancellationToken cancellationToken = default)
    {
        var metrics = new SagaExecutionMetrics
        {
            SagaId = sagaState.SagaId,
            SagaType = sagaState.SagaType,
            StartedAt = DateTime.UtcNow,
            CorrelationId = sagaState.CorrelationId
        };

        _activeMetrics.TryAdd(sagaState.SagaId, metrics);
        UpdateTypeMetrics(sagaState.SagaType, m => m.ActiveSagas++);

        _logger.LogInformation("Saga {SagaId} of type {SagaType} started",
            sagaState.SagaId, sagaState.SagaType);

        return PublishEventAsync(SagaEventTypes.Started, sagaState, cancellationToken);
    }

    public Task TrackSagaCompletedAsync(ISagaState sagaState, CancellationToken cancellationToken = default)
    {
        if (_activeMetrics.TryRemove(sagaState.SagaId, out var metrics))
        {
            metrics.CompletedAt = DateTime.UtcNow;
            metrics.Duration = metrics.CompletedAt.Value - metrics.StartedAt;
            metrics.Status = SagaExecutionStatus.Completed;

            AddToHistory(metrics);
            UpdateTypeMetrics(sagaState.SagaType, m =>
            {
                m.ActiveSagas--;
                m.CompletedSagas++;
                m.TotalExecutionTime += metrics.Duration;
                m.LastCompletionTime = DateTime.UtcNow;
            });

            _logger.LogInformation("Saga {SagaId} completed in {Duration}ms",
                sagaState.SagaId, metrics.Duration.TotalMilliseconds);
        }

        return PublishEventAsync(SagaEventTypes.Completed, sagaState, cancellationToken);
    }

    public Task TrackSagaFailedAsync(ISagaState sagaState, string reason, CancellationToken cancellationToken = default)
    {
        if (_activeMetrics.TryRemove(sagaState.SagaId, out var metrics))
        {
            metrics.CompletedAt = DateTime.UtcNow;
            metrics.Duration = metrics.CompletedAt.Value - metrics.StartedAt;
            metrics.Status = SagaExecutionStatus.Failed;
            metrics.ErrorMessage = reason;

            AddToHistory(metrics);
            UpdateTypeMetrics(sagaState.SagaType, m =>
            {
                m.ActiveSagas--;
                m.FailedSagas++;
                m.LastFailureTime = DateTime.UtcNow;
                m.LastFailureReason = reason;
            });

            _logger.LogWarning("Saga {SagaId} failed: {Reason}", sagaState.SagaId, reason);
        }

        return PublishEventAsync(SagaEventTypes.Failed, sagaState, new { Reason = reason }, cancellationToken);
    }

    public Task TrackStepStartedAsync(ISagaState sagaState, string stepName, CancellationToken cancellationToken = default)
    {
        if (_activeMetrics.TryGetValue(sagaState.SagaId, out var metrics))
        {
            var stepMetrics = new StepExecutionMetrics
            {
                StepName = stepName,
                StartedAt = DateTime.UtcNow
            };
            metrics.CurrentStep = stepMetrics;
        }

        _logger.LogDebug("Saga {SagaId} started step {StepName}", sagaState.SagaId, stepName);
        return PublishEventAsync(SagaEventTypes.StepStarted, sagaState, new { StepName = stepName }, cancellationToken);
    }

    public Task TrackStepCompletedAsync(ISagaState sagaState, string stepName, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (_activeMetrics.TryGetValue(sagaState.SagaId, out var metrics) &&
            metrics.CurrentStep?.StepName == stepName)
        {
            metrics.CurrentStep.CompletedAt = DateTime.UtcNow;
            metrics.CurrentStep.Duration = duration;
            metrics.CompletedSteps.Add(metrics.CurrentStep);
            metrics.CurrentStep = null;
        }

        UpdateTypeMetrics(sagaState.SagaType, m =>
        {
            if (!m.StepMetrics.ContainsKey(stepName))
                m.StepMetrics[stepName] = new StepTypeMetrics { StepName = stepName };

            var stepMetrics = m.StepMetrics[stepName];
            stepMetrics.ExecutionCount++;
            stepMetrics.TotalExecutionTime += duration;
            stepMetrics.LastExecutionTime = DateTime.UtcNow;
        });

        _logger.LogDebug("Saga {SagaId} completed step {StepName} in {Duration}ms",
            sagaState.SagaId, stepName, duration.TotalMilliseconds);

        return PublishEventAsync(SagaEventTypes.StepCompleted, sagaState,
            new { StepName = stepName, Duration = duration }, cancellationToken);
    }

    public Task TrackStepFailedAsync(ISagaState sagaState, string stepName, string error, CancellationToken cancellationToken = default)
    {
        if (_activeMetrics.TryGetValue(sagaState.SagaId, out var metrics) &&
            metrics.CurrentStep?.StepName == stepName)
        {
            metrics.CurrentStep.CompletedAt = DateTime.UtcNow;
            metrics.CurrentStep.ErrorMessage = error;
            metrics.FailedSteps.Add(metrics.CurrentStep);
            metrics.CurrentStep = null;
        }

        UpdateTypeMetrics(sagaState.SagaType, m =>
        {
            if (!m.StepMetrics.ContainsKey(stepName))
                m.StepMetrics[stepName] = new StepTypeMetrics { StepName = stepName };

            var stepMetrics = m.StepMetrics[stepName];
            stepMetrics.FailureCount++;
            stepMetrics.LastFailureTime = DateTime.UtcNow;
            stepMetrics.LastFailureReason = error;
        });

        _logger.LogWarning("Saga {SagaId} step {StepName} failed: {Error}",
            sagaState.SagaId, stepName, error);

        return PublishEventAsync(SagaEventTypes.StepFailed, sagaState,
            new { StepName = stepName, Error = error }, cancellationToken);
    }

    public async Task<SagaHealthReport> GetHealthReportAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.Subtract(_options.HealthCheckWindow);

        var recentMetrics = GetRecentMetrics(cutoff);
        var totalSagas = recentMetrics.Count();
        var failedSagas = recentMetrics.Count(m => m.Status == SagaExecutionStatus.Failed);
        var timeoutedSagas = recentMetrics.Count(m => m.Status == SagaExecutionStatus.TimedOut);

        var health = new SagaHealthReport
        {
            Timestamp = now,
            OverallStatus = CalculateOverallHealth(failedSagas, timeoutedSagas, totalSagas),
            ActiveSagaCount = _activeMetrics.Count,
            TotalSagasInWindow = totalSagas,
            FailedSagasInWindow = failedSagas,
            TimeoutedSagasInWindow = timeoutedSagas,
            SuccessRate = totalSagas > 0 ? (double)(totalSagas - failedSagas - timeoutedSagas) / totalSagas : 1.0,
            AverageExecutionTime = recentMetrics.Any() ?
                TimeSpan.FromMilliseconds(recentMetrics.Average(m => m.Duration.TotalMilliseconds)) :
                TimeSpan.Zero
        };

        if (_activeMetrics.Any())
        {
            var oldestActive = _activeMetrics.Values.Min(m => m.StartedAt);
            health.LongestRunningSaga = now - oldestActive;
        }

        using var scope = _serviceProvider.CreateScope();
        health.PersistenceHealthChecks = await CheckPersistenceHealthAsync(scope.ServiceProvider, cancellationToken);

        return health;
    }

    public Task<SagaPerformanceMetrics> GetPerformanceMetricsAsync(string? sagaType = null, CancellationToken cancellationToken = default)
    {
        var metrics = new SagaPerformanceMetrics
        {
            Timestamp = DateTime.UtcNow,
            SagaType = sagaType
        };

        if (sagaType != null && _typeMetrics.TryGetValue(sagaType, out var typeMetrics))
        {
            metrics.TotalExecutions = typeMetrics.CompletedSagas + typeMetrics.FailedSagas;
            metrics.SuccessfulExecutions = typeMetrics.CompletedSagas;
            metrics.FailedExecutions = typeMetrics.FailedSagas;
            metrics.ActiveExecutions = typeMetrics.ActiveSagas;
            metrics.AverageExecutionTime = typeMetrics.CompletedSagas > 0 ?
                TimeSpan.FromMilliseconds(typeMetrics.TotalExecutionTime.TotalMilliseconds / typeMetrics.CompletedSagas) :
                TimeSpan.Zero;
            metrics.StepMetrics = typeMetrics.StepMetrics.Values.ToList();
        }
        else
        {
            var allMetrics = GetRecentMetrics(DateTime.UtcNow.Subtract(_options.MetricsWindow));
            metrics.TotalExecutions = allMetrics.Count();
            metrics.SuccessfulExecutions = allMetrics.Count(m => m.Status == SagaExecutionStatus.Completed);
            metrics.FailedExecutions = allMetrics.Count(m => m.Status == SagaExecutionStatus.Failed);
            metrics.ActiveExecutions = _activeMetrics.Count;
            metrics.AverageExecutionTime = allMetrics.Any() ?
                TimeSpan.FromMilliseconds(allMetrics.Average(m => m.Duration.TotalMilliseconds)) :
                TimeSpan.Zero;
        }

        return Task.FromResult(metrics);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Saga monitor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOldMetricsAsync(stoppingToken);
                await CheckForStuckSagasAsync(stoppingToken);
                await Task.Delay(_options.CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in saga monitoring cleanup");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("Saga monitor stopped");
    }

    private async Task CleanupOldMetricsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.Subtract(_options.MetricsRetention);

        while (_metricHistory.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
        {
            _metricHistory.TryDequeue(out _);
        }

        if (_metricHistory.Count > _options.MaxMetricsHistory)
        {
            var excess = _metricHistory.Count - _options.MaxMetricsHistory;
            for (int i = 0; i < excess; i++)
            {
                _metricHistory.TryDequeue(out _);
            }
        }
    }

    private async Task CheckForStuckSagasAsync(CancellationToken cancellationToken)
    {
        var stuckThreshold = DateTime.UtcNow.Subtract(_options.StuckSagaThreshold);
        var stuckSagas = _activeMetrics.Values
            .Where(m => m.StartedAt < stuckThreshold)
            .ToList();

        foreach (var stuckSaga in stuckSagas)
        {
            _logger.LogWarning("Detected stuck saga {SagaId} running for {Duration}",
                stuckSaga.SagaId, DateTime.UtcNow - stuckSaga.StartedAt);

            if (_eventPublisher != null)
            {
                await _eventPublisher.PublishAsync(new SagaEvent<object>
                {
                    SagaId = stuckSaga.SagaId,
                    SagaType = stuckSaga.SagaType,
                    EventType = "SagaStuck",
                    Data = new { RunningFor = DateTime.UtcNow - stuckSaga.StartedAt },
                    CorrelationId = stuckSaga.CorrelationId
                }, cancellationToken);
            }
        }
    }

    private void AddToHistory(SagaExecutionMetrics metrics)
    {
        var snapshot = new SagaMetricSnapshot
        {
            SagaId = metrics.SagaId,
            SagaType = metrics.SagaType,
            Status = metrics.Status,
            Duration = metrics.Duration,
            Timestamp = metrics.CompletedAt!.Value,
            StepCount = metrics.CompletedSteps.Count + metrics.FailedSteps.Count,
            ErrorMessage = metrics.ErrorMessage
        };

        _metricHistory.Enqueue(snapshot);
    }

    private void UpdateTypeMetrics(string sagaType, Action<SagaTypeMetrics> update)
    {
        var metrics = _typeMetrics.GetOrAdd(sagaType, _ => new SagaTypeMetrics { SagaType = sagaType });
        lock (metrics)
        {
            update(metrics);
        }
    }

    private IEnumerable<SagaMetricSnapshot> GetRecentMetrics(DateTime since)
    {
        return _metricHistory.Where(m => m.Timestamp >= since);
    }

    private SagaHealthStatus CalculateOverallHealth(int failed, int timedOut, int total)
    {
        if (total == 0) return SagaHealthStatus.Healthy;

        var failureRate = (double)(failed + timedOut) / total;

        return failureRate switch
        {
            <= 0.05 => SagaHealthStatus.Healthy,
            <= 0.15 => SagaHealthStatus.Degraded,
            _ => SagaHealthStatus.Unhealthy
        };
    }

    private async Task<List<PersistenceHealthCheck>> CheckPersistenceHealthAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var healthChecks = new List<PersistenceHealthCheck>();

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
                    var healthCheck = new PersistenceHealthCheck
                    {
                        PersistenceType = persistenceType.Name,
                        Status = SagaHealthStatus.Healthy,
                        ResponseTime = TimeSpan.Zero
                    };

                    var stopwatch = Stopwatch.StartNew();

                    try
                    {
                        var statsMethod = persistenceType.GetMethod("GetStatisticsAsync");
                        if (statsMethod != null)
                        {
                            var task = statsMethod.Invoke(persistence, new object[] { cancellationToken }) as Task;
                            if (task != null)
                            {
                                await task;
                                healthCheck.ResponseTime = stopwatch.Elapsed;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        healthCheck.Status = SagaHealthStatus.Unhealthy;
                        healthCheck.ErrorMessage = ex.Message;
                    }

                    healthChecks.Add(healthCheck);
                }
            }
            catch (Exception ex)
            {
                healthChecks.Add(new PersistenceHealthCheck
                {
                    PersistenceType = persistenceType.Name,
                    Status = SagaHealthStatus.Unhealthy,
                    ErrorMessage = ex.Message
                });
            }
        }

        return healthChecks;
    }

    private async Task PublishEventAsync(string eventType, ISagaState sagaState, CancellationToken cancellationToken)
    {
        await PublishEventAsync(eventType, sagaState, new object(), cancellationToken);
    }

    private async Task PublishEventAsync(string eventType, ISagaState sagaState, object data, CancellationToken cancellationToken)
    {
        if (_eventPublisher != null)
        {
            try
            {
                await _eventPublisher.PublishAsync(new SagaEvent<object>
                {
                    SagaId = sagaState.SagaId,
                    SagaType = sagaState.SagaType,
                    EventType = eventType,
                    Data = data,
                    CorrelationId = sagaState.CorrelationId
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish saga event {EventType} for saga {SagaId}",
                    eventType, sagaState.SagaId);
            }
        }
    }
}

public class SagaMonitoringOptions
{
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MetricsRetention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan HealthCheckWindow { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan MetricsWindow { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan StuckSagaThreshold { get; set; } = TimeSpan.FromHours(2);
    public int MaxMetricsHistory { get; set; } = 10000;
}

public class SagaExecutionMetrics
{
    public Guid SagaId { get; set; }
    public string SagaType { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public SagaExecutionStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CorrelationId { get; set; }
    public StepExecutionMetrics? CurrentStep { get; set; }
    public List<StepExecutionMetrics> CompletedSteps { get; set; } = new();
    public List<StepExecutionMetrics> FailedSteps { get; set; } = new();
}

public class StepExecutionMetrics
{
    public string StepName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SagaMetricSnapshot
{
    public Guid SagaId { get; set; }
    public string SagaType { get; set; } = string.Empty;
    public SagaExecutionStatus Status { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime Timestamp { get; set; }
    public int StepCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SagaTypeMetrics
{
    public string SagaType { get; set; } = string.Empty;
    public int ActiveSagas { get; set; }
    public int CompletedSagas { get; set; }
    public int FailedSagas { get; set; }
    public TimeSpan TotalExecutionTime { get; set; }
    public DateTime? LastCompletionTime { get; set; }
    public DateTime? LastFailureTime { get; set; }
    public string? LastFailureReason { get; set; }
    public Dictionary<string, StepTypeMetrics> StepMetrics { get; set; } = new();
}

public class StepTypeMetrics
{
    public string StepName { get; set; } = string.Empty;
    public int ExecutionCount { get; set; }
    public int FailureCount { get; set; }
    public TimeSpan TotalExecutionTime { get; set; }
    public DateTime? LastExecutionTime { get; set; }
    public DateTime? LastFailureTime { get; set; }
    public string? LastFailureReason { get; set; }

    public double SuccessRate => ExecutionCount > 0 ? (double)(ExecutionCount - FailureCount) / ExecutionCount : 1.0;
    public TimeSpan AverageExecutionTime => ExecutionCount > 0 ?
        TimeSpan.FromMilliseconds(TotalExecutionTime.TotalMilliseconds / ExecutionCount) :
        TimeSpan.Zero;
}

public enum SagaExecutionStatus
{
    Running,
    Completed,
    Failed,
    TimedOut,
    Compensated
}

public class SagaHealthReport
{
    public DateTime Timestamp { get; set; }
    public SagaHealthStatus OverallStatus { get; set; }
    public int ActiveSagaCount { get; set; }
    public int TotalSagasInWindow { get; set; }
    public int FailedSagasInWindow { get; set; }
    public int TimeoutedSagasInWindow { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public TimeSpan? LongestRunningSaga { get; set; }
    public List<PersistenceHealthCheck> PersistenceHealthChecks { get; set; } = new();
}

public class PersistenceHealthCheck
{
    public string PersistenceType { get; set; } = string.Empty;
    public SagaHealthStatus Status { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SagaPerformanceMetrics
{
    public DateTime Timestamp { get; set; }
    public string? SagaType { get; set; }
    public int TotalExecutions { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int FailedExecutions { get; set; }
    public int ActiveExecutions { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public List<StepTypeMetrics> StepMetrics { get; set; } = new();

    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions : 1.0;
}

public enum SagaHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}