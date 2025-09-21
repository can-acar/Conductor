using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Conductor.Saga;

[ApiController]
[Route("api/saga-dashboard")]
public class SagaDashboardController : ControllerBase
{
    private readonly ISagaMonitor _monitor;
    private readonly ISagaDiagnosticService _diagnosticService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SagaDashboardController> _logger;

    public SagaDashboardController(
        ISagaMonitor monitor,
        ISagaDiagnosticService diagnosticService,
        IServiceProvider serviceProvider,
        ILogger<SagaDashboardController> logger)
    {
        _monitor = monitor;
        _diagnosticService = diagnosticService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [HttpGet("health")]
    public async Task<ActionResult<SagaHealthReport>> GetHealthReport(CancellationToken cancellationToken = default)
    {
        try
        {
            var healthReport = await _monitor.GetHealthReportAsync(cancellationToken);
            return Ok(healthReport);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get saga health report");
            return StatusCode(500, "Failed to get health report");
        }
    }

    [HttpGet("performance")]
    public async Task<ActionResult<SagaPerformanceMetrics>> GetPerformanceMetrics(
        [FromQuery] string? sagaType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _monitor.GetPerformanceMetricsAsync(sagaType, cancellationToken);
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get performance metrics for saga type {SagaType}", sagaType);
            return StatusCode(500, "Failed to get performance metrics");
        }
    }

    [HttpGet("anomalies")]
    public async Task<ActionResult<List<SagaAnomaly>>> GetAnomalies(
        [FromQuery] string? sagaType = null,
        [FromQuery] int? lookbackHours = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lookbackPeriod = lookbackHours.HasValue ? TimeSpan.FromHours(lookbackHours.Value) : TimeSpan.FromHours(24);
            var anomalies = await _diagnosticService.DetectAnomaliesAsync(sagaType, lookbackPeriod, cancellationToken);
            return Ok(anomalies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect anomalies");
            return StatusCode(500, "Failed to detect anomalies");
        }
    }

    [HttpGet("statistics")]
    public async Task<ActionResult<SagaStatistics>> GetStatistics(
        [FromQuery] string? sagaType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();

            if (!string.IsNullOrEmpty(sagaType))
            {
                var sagaStateType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name.Contains(sagaType) && typeof(ISagaState).IsAssignableFrom(t));

                if (sagaStateType != null)
                {
                    var persistenceType = typeof(ISagaPersistence<>).MakeGenericType(sagaStateType);
                    var persistence = scope.ServiceProvider.GetService(persistenceType);

                    if (persistence != null)
                    {
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
                                    return Ok(stats);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                var aggregatedStats = new SagaStatistics();
                var persistenceTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .Where(t => t.GetInterfaces().Any(i => i.IsGenericType &&
                                                          i.GetGenericTypeDefinition() == typeof(ISagaPersistence<>)))
                    .ToList();

                foreach (var persistenceType in persistenceTypes)
                {
                    try
                    {
                        var persistence = scope.ServiceProvider.GetService(persistenceType);
                        if (persistence != null)
                        {
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
                                        aggregatedStats.TotalSagas += stats.TotalSagas;
                                        aggregatedStats.RunningSagas += stats.RunningSagas;
                                        aggregatedStats.CompletedSagas += stats.CompletedSagas;
                                        aggregatedStats.FailedSagas += stats.FailedSagas;
                                        aggregatedStats.CompensatingSagas += stats.CompensatingSagas;
                                        aggregatedStats.CompensatedSagas += stats.CompensatedSagas;
                                        aggregatedStats.SuspendedSagas += stats.SuspendedSagas;
                                        aggregatedStats.TimedOutSagas += stats.TimedOutSagas;

                                        foreach (var kvp in stats.SagasByType)
                                        {
                                            aggregatedStats.SagasByType[kvp.Key] = aggregatedStats.SagasByType.GetValueOrDefault(kvp.Key, 0) + kvp.Value;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get statistics from persistence type {Type}", persistenceType.Name);
                    }
                }

                return Ok(aggregatedStats);
            }

            return NotFound("No statistics available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get saga statistics");
            return StatusCode(500, "Failed to get statistics");
        }
    }

    [HttpGet("saga/{sagaId}/report")]
    public async Task<ActionResult<SagaDiagnosticReport>> GetSagaReport(
        Guid sagaId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _diagnosticService.GenerateReportAsync(sagaId, cancellationToken);

            if (report.Status == DiagnosticStatus.NotFound)
            {
                return NotFound($"Saga {sagaId} not found");
            }

            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate report for saga {SagaId}", sagaId);
            return StatusCode(500, "Failed to generate saga report");
        }
    }

    [HttpGet("saga/{sagaId}/trace")]
    public async Task<ActionResult<SagaExecutionTrace>> GetSagaTrace(
        Guid sagaId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var trace = await _diagnosticService.GetExecutionTraceAsync(sagaId, cancellationToken);

            if (string.IsNullOrEmpty(trace.SagaType))
            {
                return NotFound($"Saga {sagaId} not found");
            }

            return Ok(trace);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get execution trace for saga {SagaId}", sagaId);
            return StatusCode(500, "Failed to get execution trace");
        }
    }

    [HttpGet("saga/{sagaId}/debug")]
    public async Task<ActionResult<SagaDebugInfo>> GetSagaDebugInfo(
        Guid sagaId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var debugInfo = await _diagnosticService.GetDebugInfoAsync(sagaId, cancellationToken);

            if (!string.IsNullOrEmpty(debugInfo.ErrorMessage))
            {
                return NotFound($"Saga {sagaId} not found: {debugInfo.ErrorMessage}");
            }

            return Ok(debugInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get debug info for saga {SagaId}", sagaId);
            return StatusCode(500, "Failed to get debug info");
        }
    }

    [HttpGet("saga/{sagaId}/export")]
    public async Task<ActionResult> ExportSagaData(
        Guid sagaId,
        [FromQuery] SagaExportFormat format = SagaExportFormat.Json,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await _diagnosticService.ExportSagaDataAsync(sagaId, format, cancellationToken);

            if (string.IsNullOrEmpty(data))
            {
                return NotFound($"Saga {sagaId} not found");
            }

            var contentType = format switch
            {
                SagaExportFormat.Json => "application/json",
                SagaExportFormat.Xml => "application/xml",
                SagaExportFormat.Csv => "text/csv",
                _ => "application/json"
            };

            var fileName = $"saga-{sagaId}.{format.ToString().ToLower()}";

            return File(System.Text.Encoding.UTF8.GetBytes(data), contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export data for saga {SagaId}", sagaId);
            return StatusCode(500, "Failed to export saga data");
        }
    }

    [HttpGet("sagas")]
    public async Task<ActionResult<SagaListResponse>> GetSagas(
        [FromQuery] SagaStatus? status = null,
        [FromQuery] string? sagaType = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            pageSize = Math.Min(pageSize, 100); // Limit page size

            using var scope = _serviceProvider.CreateScope();
            var allSagas = new List<ISagaState>();

            var persistenceTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType &&
                                                      i.GetGenericTypeDefinition() == typeof(ISagaPersistence<>)))
                .ToList();

            foreach (var persistenceType in persistenceTypes)
            {
                try
                {
                    var persistence = scope.ServiceProvider.GetService(persistenceType);
                    if (persistence != null)
                    {
                        if (status.HasValue)
                        {
                            var getByStatusMethod = persistenceType.GetMethod("GetByStatusAsync");
                            if (getByStatusMethod != null)
                            {
                                var task = getByStatusMethod.Invoke(persistence, new object[] { status.Value, cancellationToken }) as Task;
                                if (task != null)
                                {
                                    await task;
                                    var result = task.GetType().GetProperty("Result")?.GetValue(task);
                                    if (result is IEnumerable<ISagaState> sagas)
                                    {
                                        allSagas.AddRange(sagas);
                                    }
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(correlationId))
                        {
                            var getByCorrelationMethod = persistenceType.GetMethod("GetByCorrelationIdAsync");
                            if (getByCorrelationMethod != null)
                            {
                                var task = getByCorrelationMethod.Invoke(persistence, new object[] { correlationId, cancellationToken }) as Task;
                                if (task != null)
                                {
                                    await task;
                                    var result = task.GetType().GetProperty("Result")?.GetValue(task);
                                    if (result is IEnumerable<ISagaState> sagas)
                                    {
                                        allSagas.AddRange(sagas);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get sagas from persistence type {Type}", persistenceType.Name);
                }
            }

            var filteredSagas = allSagas.AsQueryable();

            if (!string.IsNullOrEmpty(sagaType))
            {
                filteredSagas = filteredSagas.Where(s => s.SagaType.Contains(sagaType, StringComparison.OrdinalIgnoreCase));
            }

            var totalCount = filteredSagas.Count();
            var pagedSagas = filteredSagas
                .OrderByDescending(s => s.LastUpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SagaSummary
                {
                    SagaId = s.SagaId,
                    SagaType = s.SagaType,
                    Status = s.Status,
                    CurrentStep = s.CurrentStep,
                    CreatedAt = s.CreatedAt,
                    LastUpdatedAt = s.LastUpdatedAt,
                    CompletedAt = s.CompletedAt,
                    CorrelationId = s.CorrelationId,
                    StepCount = s.Steps.Count,
                    CompletedStepCount = s.Steps.Count(step => step.Status == SagaStepStatus.Completed),
                    FailedStepCount = s.Steps.Count(step => step.Status == SagaStepStatus.Failed)
                })
                .ToList();

            var response = new SagaListResponse
            {
                Sagas = pagedSagas,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get saga list");
            return StatusCode(500, "Failed to get saga list");
        }
    }

    [HttpPost("saga/{sagaId}/abort")]
    public async Task<ActionResult> AbortSaga(
        Guid sagaId,
        [FromBody] AbortSagaRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var sagaState = await GetSagaStateFromAnyPersistenceAsync(scope.ServiceProvider, sagaId, cancellationToken);

            if (sagaState == null)
            {
                return NotFound($"Saga {sagaId} not found");
            }

            var orchestratorType = typeof(ISagaOrchestrator<>).MakeGenericType(sagaState.GetType());
            var orchestrator = scope.ServiceProvider.GetService(orchestratorType);

            if (orchestrator == null)
            {
                return BadRequest("No orchestrator found for saga type");
            }

            var abortMethod = orchestratorType.GetMethod("AbortAsync");
            if (abortMethod != null)
            {
                var task = abortMethod.Invoke(orchestrator, new object[] { sagaState, request.Reason ?? "Manual abort", cancellationToken }) as Task;
                if (task != null)
                {
                    await task;
                    _logger.LogInformation("Saga {SagaId} aborted manually by user", sagaId);
                    return Ok(new { Message = "Saga aborted successfully" });
                }
            }

            return BadRequest("Failed to abort saga");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to abort saga {SagaId}", sagaId);
            return StatusCode(500, "Failed to abort saga");
        }
    }

    [HttpPost("saga/{sagaId}/resume")]
    public async Task<ActionResult> ResumeSaga(
        Guid sagaId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var sagaState = await GetSagaStateFromAnyPersistenceAsync(scope.ServiceProvider, sagaId, cancellationToken);

            if (sagaState == null)
            {
                return NotFound($"Saga {sagaId} not found");
            }

            if (sagaState.Status != SagaStatus.Suspended)
            {
                return BadRequest($"Saga is not suspended (current status: {sagaState.Status})");
            }

            var orchestratorType = typeof(ISagaOrchestrator<>).MakeGenericType(sagaState.GetType());
            var orchestrator = scope.ServiceProvider.GetService(orchestratorType);

            if (orchestrator == null)
            {
                return BadRequest("No orchestrator found for saga type");
            }

            var resumeMethod = orchestratorType.GetMethod("ResumeAsync");
            if (resumeMethod != null)
            {
                var task = resumeMethod.Invoke(orchestrator, new object[] { sagaState, cancellationToken }) as Task;
                if (task != null)
                {
                    await task;
                    _logger.LogInformation("Saga {SagaId} resumed manually by user", sagaId);
                    return Ok(new { Message = "Saga resumed successfully" });
                }
            }

            return BadRequest("Failed to resume saga");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume saga {SagaId}", sagaId);
            return StatusCode(500, "Failed to resume saga");
        }
    }

    private async Task<ISagaState?> GetSagaStateFromAnyPersistenceAsync(IServiceProvider serviceProvider, Guid sagaId, CancellationToken cancellationToken)
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
}

public class SagaSummary
{
    public Guid SagaId { get; set; }
    public string SagaType { get; set; } = string.Empty;
    public SagaStatus Status { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CorrelationId { get; set; }
    public int StepCount { get; set; }
    public int CompletedStepCount { get; set; }
    public int FailedStepCount { get; set; }

    public TimeSpan Duration => CompletedAt.HasValue ?
        CompletedAt.Value - CreatedAt :
        DateTime.UtcNow - CreatedAt;

    public double ProgressPercentage => StepCount > 0 ?
        (double)CompletedStepCount / StepCount * 100 :
        0;
}

public class SagaListResponse
{
    public List<SagaSummary> Sagas { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class AbortSagaRequest
{
    public string? Reason { get; set; }
}