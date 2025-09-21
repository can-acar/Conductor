using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

namespace Conductor.Saga;

public interface ISagaDiagnosticService
{
    Task<SagaDiagnosticReport> GenerateReportAsync(Guid sagaId, CancellationToken cancellationToken = default);
    Task<SagaExecutionTrace> GetExecutionTraceAsync(Guid sagaId, CancellationToken cancellationToken = default);
    Task<List<SagaAnomaly>> DetectAnomaliesAsync(string? sagaType = null, TimeSpan? lookbackPeriod = null, CancellationToken cancellationToken = default);
    Task<SagaDebugInfo> GetDebugInfoAsync(Guid sagaId, CancellationToken cancellationToken = default);
    Task<string> ExportSagaDataAsync(Guid sagaId, SagaExportFormat format = SagaExportFormat.Json, CancellationToken cancellationToken = default);
}

public class SagaDiagnosticService : ISagaDiagnosticService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISagaMonitor _monitor;
    private readonly ILogger<SagaDiagnosticService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public SagaDiagnosticService(
        IServiceProvider serviceProvider,
        ISagaMonitor monitor,
        ILogger<SagaDiagnosticService> logger)
    {
        _serviceProvider = serviceProvider;
        _monitor = monitor;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<SagaDiagnosticReport> GenerateReportAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating diagnostic report for saga {SagaId}", sagaId);

        var report = new SagaDiagnosticReport
        {
            SagaId = sagaId,
            GeneratedAt = DateTime.UtcNow
        };

        try
        {
            using var scope = _serviceProvider.CreateScope();

            report.SagaState = await GetSagaStateFromPersistenceAsync(scope.ServiceProvider, sagaId, cancellationToken);
            if (report.SagaState == null)
            {
                report.Status = DiagnosticStatus.NotFound;
                report.Summary = "Saga not found in any persistence store";
                return report;
            }

            report.ExecutionTrace = await GetExecutionTraceAsync(sagaId, cancellationToken);
            report.PerformanceMetrics = await _monitor.GetPerformanceMetricsAsync(report.SagaState.SagaType, cancellationToken);
            report.Anomalies = await DetectAnomaliesForSagaAsync(report.SagaState, cancellationToken);
            report.Status = DetermineOverallStatus(report);
            report.Summary = GenerateSummary(report);

            _logger.LogDebug("Successfully generated diagnostic report for saga {SagaId}", sagaId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate diagnostic report for saga {SagaId}", sagaId);
            report.Status = DiagnosticStatus.Error;
            report.Summary = $"Error generating report: {ex.Message}";
            report.Errors.Add(ex.Message);
        }

        return report;
    }

    public async Task<SagaExecutionTrace> GetExecutionTraceAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        var trace = new SagaExecutionTrace
        {
            SagaId = sagaId,
            GeneratedAt = DateTime.UtcNow
        };

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var sagaState = await GetSagaStateFromPersistenceAsync(scope.ServiceProvider, sagaId, cancellationToken);

            if (sagaState != null)
            {
                trace.SagaType = sagaState.SagaType;
                trace.Status = sagaState.Status;
                trace.CreatedAt = sagaState.CreatedAt;
                trace.LastUpdatedAt = sagaState.LastUpdatedAt;
                trace.CompletedAt = sagaState.CompletedAt;
                trace.CorrelationId = sagaState.CorrelationId;

                trace.Steps = sagaState.Steps.Select(step => new StepExecutionTrace
                {
                    StepName = step.Name,
                    StepType = step.StepType,
                    Status = step.Status,
                    StartedAt = step.StartedAt,
                    CompletedAt = step.CompletedAt,
                    Duration = step.CompletedAt.HasValue && step.StartedAt.HasValue ?
                        step.CompletedAt.Value - step.StartedAt.Value : null,
                    Input = step.Input,
                    Output = step.Output,
                    ErrorMessage = step.ErrorMessage,
                    RetryCount = step.RetryCount,
                    MaxRetries = step.MaxRetries
                }).ToList();

                trace.Compensations = sagaState.Compensations.Select(comp => new CompensationTrace
                {
                    StepName = comp.StepName,
                    Action = comp.Action,
                    Status = comp.Status,
                    ExecutedAt = comp.ExecutedAt,
                    ErrorMessage = comp.ErrorMessage,
                    RetryCount = comp.RetryCount
                }).ToList();

                trace.DataSnapshot = JsonSerializer.Serialize(sagaState.Data, _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get execution trace for saga {SagaId}", sagaId);
            throw;
        }

        return trace;
    }

    public async Task<List<SagaAnomaly>> DetectAnomaliesAsync(string? sagaType = null, TimeSpan? lookbackPeriod = null, CancellationToken cancellationToken = default)
    {
        var anomalies = new List<SagaAnomaly>();
        var period = lookbackPeriod ?? TimeSpan.FromHours(24);

        try
        {
            var healthReport = await _monitor.GetHealthReportAsync(cancellationToken);
            var performanceMetrics = await _monitor.GetPerformanceMetricsAsync(sagaType, cancellationToken);

            if (healthReport.SuccessRate < 0.95)
            {
                anomalies.Add(new SagaAnomaly
                {
                    Type = AnomalyType.HighFailureRate,
                    Severity = AnomalySeverity.High,
                    Description = $"High failure rate detected: {healthReport.SuccessRate:P2}",
                    Value = healthReport.SuccessRate,
                    Threshold = 0.95,
                    DetectedAt = DateTime.UtcNow,
                    SagaType = sagaType
                });
            }

            if (healthReport.AverageExecutionTime > TimeSpan.FromMinutes(30))
            {
                anomalies.Add(new SagaAnomaly
                {
                    Type = AnomalyType.SlowExecution,
                    Severity = AnomalySeverity.Medium,
                    Description = $"Slow execution detected: {healthReport.AverageExecutionTime}",
                    Value = healthReport.AverageExecutionTime.TotalMilliseconds,
                    Threshold = TimeSpan.FromMinutes(30).TotalMilliseconds,
                    DetectedAt = DateTime.UtcNow,
                    SagaType = sagaType
                });
            }

            if (healthReport.LongestRunningSaga.HasValue && healthReport.LongestRunningSaga.Value > TimeSpan.FromHours(2))
            {
                anomalies.Add(new SagaAnomaly
                {
                    Type = AnomalyType.StuckSaga,
                    Severity = AnomalySeverity.High,
                    Description = $"Long-running saga detected: {healthReport.LongestRunningSaga.Value}",
                    Value = healthReport.LongestRunningSaga.Value.TotalMilliseconds,
                    Threshold = TimeSpan.FromHours(2).TotalMilliseconds,
                    DetectedAt = DateTime.UtcNow,
                    SagaType = sagaType
                });
            }

            await DetectStepAnomaliesAsync(performanceMetrics, anomalies, sagaType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect anomalies for saga type {SagaType}", sagaType);
            throw;
        }

        return anomalies;
    }

    public async Task<SagaDebugInfo> GetDebugInfoAsync(Guid sagaId, CancellationToken cancellationToken = default)
    {
        var debugInfo = new SagaDebugInfo
        {
            SagaId = sagaId,
            GeneratedAt = DateTime.UtcNow
        };

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var sagaState = await GetSagaStateFromPersistenceAsync(scope.ServiceProvider, sagaId, cancellationToken);

            if (sagaState != null)
            {
                debugInfo.CurrentState = JsonSerializer.Serialize(sagaState, _jsonOptions);
                debugInfo.StepDefinitions = await GetStepDefinitionsAsync(sagaState.SagaType, scope.ServiceProvider);
                debugInfo.AvailableHandlers = await GetAvailableHandlersAsync(sagaState.SagaType, scope.ServiceProvider);
                debugInfo.PersistenceInfo = await GetPersistenceInfoAsync(sagaState, scope.ServiceProvider, cancellationToken);
                debugInfo.ValidationResults = await ValidateSagaStateAsync(sagaState);
                debugInfo.NextPossibleSteps = await GetNextPossibleStepsAsync(sagaState, scope.ServiceProvider, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get debug info for saga {SagaId}", sagaId);
            debugInfo.ErrorMessage = ex.Message;
        }

        return debugInfo;
    }

    public async Task<string> ExportSagaDataAsync(Guid sagaId, SagaExportFormat format = SagaExportFormat.Json, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var sagaState = await GetSagaStateFromPersistenceAsync(scope.ServiceProvider, sagaId, cancellationToken);

        if (sagaState == null)
            return string.Empty;

        return format switch
        {
            SagaExportFormat.Json => JsonSerializer.Serialize(sagaState, _jsonOptions),
            SagaExportFormat.Xml => ConvertToXml(sagaState),
            SagaExportFormat.Csv => ConvertToCsv(sagaState),
            _ => JsonSerializer.Serialize(sagaState, _jsonOptions)
        };
    }

    private async Task<ISagaState?> GetSagaStateFromPersistenceAsync(IServiceProvider serviceProvider, Guid sagaId, CancellationToken cancellationToken)
    {
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
                    var getMethod = persistenceType.GetMethod("GetAsync");
                    if (getMethod != null)
                    {
                        var task = getMethod.Invoke(persistence, new object[] { sagaId, cancellationToken }) as Task;
                        if (task != null)
                        {
                            await task;
                            var result = task.GetType().GetProperty("Result")?.GetValue(task);
                            if (result is ISagaState sagaState)
                            {
                                return sagaState;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get saga from persistence type {Type}", persistenceType.Name);
            }
        }

        return null;
    }

    private async Task<List<SagaAnomaly>> DetectAnomaliesForSagaAsync(ISagaState sagaState, CancellationToken cancellationToken)
    {
        var anomalies = new List<SagaAnomaly>();

        if (sagaState.Steps.Any(s => s.RetryCount > s.MaxRetries / 2))
        {
            anomalies.Add(new SagaAnomaly
            {
                Type = AnomalyType.HighRetryCount,
                Severity = AnomalySeverity.Medium,
                Description = "High retry count detected on some steps",
                DetectedAt = DateTime.UtcNow,
                SagaId = sagaState.SagaId
            });
        }

        var runningTime = DateTime.UtcNow - sagaState.CreatedAt;
        if (runningTime > TimeSpan.FromHours(1) && sagaState.Status == SagaStatus.Running)
        {
            anomalies.Add(new SagaAnomaly
            {
                Type = AnomalyType.StuckSaga,
                Severity = AnomalySeverity.High,
                Description = $"Saga has been running for {runningTime}",
                Value = runningTime.TotalMilliseconds,
                DetectedAt = DateTime.UtcNow,
                SagaId = sagaState.SagaId
            });
        }

        return anomalies;
    }

    private async Task DetectStepAnomaliesAsync(SagaPerformanceMetrics metrics, List<SagaAnomaly> anomalies, string? sagaType)
    {
        foreach (var stepMetric in metrics.StepMetrics)
        {
            if (stepMetric.SuccessRate < 0.9)
            {
                anomalies.Add(new SagaAnomaly
                {
                    Type = AnomalyType.HighStepFailureRate,
                    Severity = AnomalySeverity.Medium,
                    Description = $"Step '{stepMetric.StepName}' has high failure rate: {stepMetric.SuccessRate:P2}",
                    Value = stepMetric.SuccessRate,
                    Threshold = 0.9,
                    DetectedAt = DateTime.UtcNow,
                    SagaType = sagaType,
                    StepName = stepMetric.StepName
                });
            }

            if (stepMetric.AverageExecutionTime > TimeSpan.FromMinutes(10))
            {
                anomalies.Add(new SagaAnomaly
                {
                    Type = AnomalyType.SlowStep,
                    Severity = AnomalySeverity.Low,
                    Description = $"Step '{stepMetric.StepName}' is slow: {stepMetric.AverageExecutionTime}",
                    Value = stepMetric.AverageExecutionTime.TotalMilliseconds,
                    Threshold = TimeSpan.FromMinutes(10).TotalMilliseconds,
                    DetectedAt = DateTime.UtcNow,
                    SagaType = sagaType,
                    StepName = stepMetric.StepName
                });
            }
        }
    }

    private async Task<List<string>> GetStepDefinitionsAsync(string sagaType, IServiceProvider serviceProvider)
    {
        var stepHandlerTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType &&
                                                  i.GetGenericTypeDefinition() == typeof(ISagaStepHandler<>)))
            .ToList();

        return stepHandlerTypes.Select(t => t.Name).ToList();
    }

    private async Task<List<string>> GetAvailableHandlersAsync(string sagaType, IServiceProvider serviceProvider)
    {
        var orchestratorTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType &&
                                                  i.GetGenericTypeDefinition() == typeof(ISagaOrchestrator<>)))
            .ToList();

        return orchestratorTypes.Select(t => t.Name).ToList();
    }

    private async Task<PersistenceDebugInfo> GetPersistenceInfoAsync(ISagaState sagaState, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var info = new PersistenceDebugInfo();

        try
        {
            var persistenceType = typeof(ISagaPersistence<>).MakeGenericType(sagaState.GetType());
            var persistence = serviceProvider.GetService(persistenceType);

            if (persistence != null)
            {
                info.PersistenceType = persistence.GetType().Name;
                info.LastSaveTime = sagaState.LastUpdatedAt;
                info.Version = sagaState.Version;

                var statsMethod = persistenceType.GetMethod("GetStatisticsAsync");
                if (statsMethod != null)
                {
                    var task = statsMethod.Invoke(persistence, new object[] { cancellationToken }) as Task;
                    if (task != null)
                    {
                        await task;
                        var stats = task.GetType().GetProperty("Result")?.GetValue(task) as SagaStatistics;
                        if (stats != null)
                        {
                            info.TotalSagasInStore = stats.TotalSagas;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            info.ErrorMessage = ex.Message;
        }

        return info;
    }

    private async Task<List<ValidationResult>> ValidateSagaStateAsync(ISagaState sagaState)
    {
        var results = new List<ValidationResult>();

        if (sagaState.SagaId == Guid.Empty)
            results.Add(new ValidationResult { Field = "SagaId", Error = "SagaId cannot be empty" });

        if (string.IsNullOrEmpty(sagaState.SagaType))
            results.Add(new ValidationResult { Field = "SagaType", Error = "SagaType cannot be empty" });

        if (sagaState.CreatedAt == default)
            results.Add(new ValidationResult { Field = "CreatedAt", Error = "CreatedAt must be set" });

        if (sagaState.Version <= 0)
            results.Add(new ValidationResult { Field = "Version", Error = "Version must be greater than 0" });

        var incompleteSteps = sagaState.Steps.Where(s => s.Status == SagaStepStatus.Running && !s.StartedAt.HasValue).ToList();
        if (incompleteSteps.Any())
        {
            results.Add(new ValidationResult
            {
                Field = "Steps",
                Error = $"Steps with Running status must have StartedAt set: {string.Join(", ", incompleteSteps.Select(s => s.Name))}"
            });
        }

        return results;
    }

    private async Task<List<string>> GetNextPossibleStepsAsync(ISagaState sagaState, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var possibleSteps = new List<string>();

        try
        {
            var orchestratorType = typeof(ISagaOrchestrator<>).MakeGenericType(sagaState.GetType());
            var orchestrator = serviceProvider.GetService(orchestratorType);

            if (orchestrator != null)
            {
                var stepHandlerTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .Where(t => t.GetInterfaces().Any(i => i.IsGenericType &&
                                                          i.GetGenericTypeDefinition() == typeof(ISagaStepHandler<>)))
                    .ToList();

                foreach (var handlerType in stepHandlerTypes)
                {
                    var handler = serviceProvider.GetService(handlerType);
                    if (handler != null)
                    {
                        var stepNameProperty = handlerType.GetProperty("StepName");
                        if (stepNameProperty?.GetValue(handler) is string stepName)
                        {
                            var canExecuteMethod = orchestratorType.GetMethod("CanExecuteStepAsync");
                            if (canExecuteMethod != null)
                            {
                                var task = canExecuteMethod.Invoke(orchestrator, new object[] { sagaState, stepName, cancellationToken }) as Task;
                                if (task != null)
                                {
                                    await task;
                                    var canExecute = (bool)(task.GetType().GetProperty("Result")?.GetValue(task) ?? false);
                                    if (canExecute)
                                    {
                                        possibleSteps.Add(stepName);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to determine next possible steps for saga {SagaId}", sagaState.SagaId);
        }

        return possibleSteps;
    }

    private DiagnosticStatus DetermineOverallStatus(SagaDiagnosticReport report)
    {
        if (report.Errors.Any())
            return DiagnosticStatus.Error;

        if (report.Anomalies.Any(a => a.Severity == AnomalySeverity.High))
            return DiagnosticStatus.Critical;

        if (report.Anomalies.Any(a => a.Severity == AnomalySeverity.Medium))
            return DiagnosticStatus.Warning;

        return DiagnosticStatus.Healthy;
    }

    private string GenerateSummary(SagaDiagnosticReport report)
    {
        if (report.SagaState == null)
            return "Saga not found";

        var summary = new StringBuilder();
        summary.AppendLine($"Saga {report.SagaId} ({report.SagaState.SagaType})");
        summary.AppendLine($"Status: {report.SagaState.Status}");
        summary.AppendLine($"Created: {report.SagaState.CreatedAt:yyyy-MM-dd HH:mm:ss}");

        if (report.SagaState.CompletedAt.HasValue)
        {
            var duration = report.SagaState.CompletedAt.Value - report.SagaState.CreatedAt;
            summary.AppendLine($"Duration: {duration}");
        }
        else
        {
            var runningTime = DateTime.UtcNow - report.SagaState.CreatedAt;
            summary.AppendLine($"Running for: {runningTime}");
        }

        summary.AppendLine($"Steps: {report.SagaState.Steps.Count} total, {report.SagaState.Steps.Count(s => s.Status == SagaStepStatus.Completed)} completed");

        if (report.Anomalies.Any())
        {
            summary.AppendLine($"Anomalies: {report.Anomalies.Count} detected");
        }

        return summary.ToString();
    }

    private string ConvertToXml(ISagaState sagaState)
    {
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<SagaState>
    <SagaId>{sagaState.SagaId}</SagaId>
    <SagaType>{sagaState.SagaType}</SagaType>
    <Status>{sagaState.Status}</Status>
    <CreatedAt>{sagaState.CreatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</CreatedAt>
    <LastUpdatedAt>{sagaState.LastUpdatedAt:yyyy-MM-ddTHH:mm:ss.fffZ}</LastUpdatedAt>
    <Version>{sagaState.Version}</Version>
    <Steps>
        {string.Join("\n        ", sagaState.Steps.Select(s => $"<Step Name=\"{s.Name}\" Status=\"{s.Status}\" />"))}
    </Steps>
</SagaState>";
    }

    private string ConvertToCsv(ISagaState sagaState)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Field,Value");
        csv.AppendLine($"SagaId,{sagaState.SagaId}");
        csv.AppendLine($"SagaType,{sagaState.SagaType}");
        csv.AppendLine($"Status,{sagaState.Status}");
        csv.AppendLine($"CreatedAt,{sagaState.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        csv.AppendLine($"LastUpdatedAt,{sagaState.LastUpdatedAt:yyyy-MM-dd HH:mm:ss}");
        csv.AppendLine($"Version,{sagaState.Version}");
        csv.AppendLine($"StepCount,{sagaState.Steps.Count}");

        return csv.ToString();
    }
}

public class SagaDiagnosticReport
{
    public Guid SagaId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DiagnosticStatus Status { get; set; }
    public string Summary { get; set; } = string.Empty;
    public ISagaState? SagaState { get; set; }
    public SagaExecutionTrace? ExecutionTrace { get; set; }
    public SagaPerformanceMetrics? PerformanceMetrics { get; set; }
    public List<SagaAnomaly> Anomalies { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class SagaExecutionTrace
{
    public Guid SagaId { get; set; }
    public string SagaType { get; set; } = string.Empty;
    public SagaStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public List<StepExecutionTrace> Steps { get; set; } = new();
    public List<CompensationTrace> Compensations { get; set; } = new();
    public string DataSnapshot { get; set; } = string.Empty;
}

public class StepExecutionTrace
{
    public string StepName { get; set; } = string.Empty;
    public string StepType { get; set; } = string.Empty;
    public SagaStepStatus Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
}

public class CompensationTrace
{
    public string StepName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public SagaStepStatus Status { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
}

public class SagaAnomaly
{
    public AnomalyType Type { get; set; }
    public AnomalySeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public double? Value { get; set; }
    public double? Threshold { get; set; }
    public DateTime DetectedAt { get; set; }
    public Guid? SagaId { get; set; }
    public string? SagaType { get; set; }
    public string? StepName { get; set; }
}

public class SagaDebugInfo
{
    public Guid SagaId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public List<string> StepDefinitions { get; set; } = new();
    public List<string> AvailableHandlers { get; set; } = new();
    public PersistenceDebugInfo PersistenceInfo { get; set; } = new();
    public List<ValidationResult> ValidationResults { get; set; } = new();
    public List<string> NextPossibleSteps { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class PersistenceDebugInfo
{
    public string PersistenceType { get; set; } = string.Empty;
    public DateTime LastSaveTime { get; set; }
    public int Version { get; set; }
    public int TotalSagasInStore { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ValidationResult
{
    public string Field { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public enum DiagnosticStatus
{
    Healthy,
    Warning,
    Critical,
    Error,
    NotFound
}

public enum AnomalyType
{
    HighFailureRate,
    SlowExecution,
    StuckSaga,
    HighRetryCount,
    HighStepFailureRate,
    SlowStep,
    UnexpectedStatus,
    DataInconsistency
}

public enum AnomalySeverity
{
    Low,
    Medium,
    High
}

public enum SagaExportFormat
{
    Json,
    Xml,
    Csv
}