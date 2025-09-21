namespace Conductor.Saga;

public interface ISagaOrchestrator<TSagaState> where TSagaState : ISagaState
{
    Task<TSagaState> StartAsync(TSagaState sagaState, CancellationToken cancellationToken = default);
    Task<TSagaState> ContinueAsync(TSagaState sagaState, string stepName, CancellationToken cancellationToken = default);
    Task<TSagaState> CompensateAsync(TSagaState sagaState, CancellationToken cancellationToken = default);
    Task<TSagaState> AbortAsync(TSagaState sagaState, string reason, CancellationToken cancellationToken = default);
    Task<TSagaState> SuspendAsync(TSagaState sagaState, string reason, CancellationToken cancellationToken = default);
    Task<TSagaState> ResumeAsync(TSagaState sagaState, CancellationToken cancellationToken = default);
    Task<bool> CanExecuteStepAsync(TSagaState sagaState, string stepName, CancellationToken cancellationToken = default);
    Task<TSagaState> HandleTimeoutAsync(TSagaState sagaState, CancellationToken cancellationToken = default);
}

public interface ISagaStepHandler<TSagaState> where TSagaState : ISagaState
{
    string StepName { get; }
    Task<SagaStepResult> ExecuteAsync(TSagaState sagaState, CancellationToken cancellationToken = default);
    Task<SagaStepResult> CompensateAsync(TSagaState sagaState, CancellationToken cancellationToken = default);
    Task<bool> CanExecuteAsync(TSagaState sagaState, CancellationToken cancellationToken = default);
    Task<bool> CanCompensateAsync(TSagaState sagaState, CancellationToken cancellationToken = default);
}

public class SagaStepResult
{
    public bool IsSuccess { get; set; }
    public object? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public string? NextStep { get; set; }
    public bool ShouldRetry { get; set; }
    public TimeSpan? RetryDelay { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public SagaStepAction Action { get; set; } = SagaStepAction.Continue;

    public static SagaStepResult Success(object? output = null, string? nextStep = null)
    {
        return new SagaStepResult
        {
            IsSuccess = true,
            Output = output,
            NextStep = nextStep
        };
    }

    public static SagaStepResult Failure(string errorMessage, bool shouldRetry = false, TimeSpan? retryDelay = null)
    {
        return new SagaStepResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ShouldRetry = shouldRetry,
            RetryDelay = retryDelay
        };
    }

    public static SagaStepResult Complete()
    {
        return new SagaStepResult
        {
            IsSuccess = true,
            Action = SagaStepAction.Complete
        };
    }

    public static SagaStepResult Compensate(string reason)
    {
        return new SagaStepResult
        {
            IsSuccess = false,
            ErrorMessage = reason,
            Action = SagaStepAction.Compensate
        };
    }

    public static SagaStepResult Suspend(string reason)
    {
        return new SagaStepResult
        {
            IsSuccess = false,
            ErrorMessage = reason,
            Action = SagaStepAction.Suspend
        };
    }
}

public enum SagaStepAction
{
    Continue,
    Complete,
    Compensate,
    Suspend,
    Abort,
    Retry
}

public interface ISagaPersistence<TSagaState> where TSagaState : ISagaState
{
    Task<TSagaState?> GetAsync(Guid sagaId, CancellationToken cancellationToken = default);
    Task<TSagaState> SaveAsync(TSagaState sagaState, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid sagaId, CancellationToken cancellationToken = default);
    Task<IEnumerable<TSagaState>> GetByStatusAsync(SagaStatus status, CancellationToken cancellationToken = default);
    Task<IEnumerable<TSagaState>> GetTimeoutedSagasAsync(DateTime before, CancellationToken cancellationToken = default);
    Task<IEnumerable<TSagaState>> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);
    Task<SagaStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

public class SagaStatistics
{
    public int TotalSagas { get; set; }
    public int RunningSagas { get; set; }
    public int CompletedSagas { get; set; }
    public int FailedSagas { get; set; }
    public int CompensatingSagas { get; set; }
    public int CompensatedSagas { get; set; }
    public int SuspendedSagas { get; set; }
    public int TimedOutSagas { get; set; }
    public Dictionary<string, int> SagasByType { get; set; } = new();
    public TimeSpan AverageExecutionTime { get; set; }
}

public interface ISagaEventPublisher
{
    Task PublishAsync<T>(SagaEvent<T> sagaEvent, CancellationToken cancellationToken = default);
}

public class SagaEvent<T>
{
    public Guid SagaId { get; set; }
    public string SagaType { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public T Data { get; set; } = default!;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public static class SagaEventTypes
{
    public const string Started = "SagaStarted";
    public const string StepStarted = "SagaStepStarted";
    public const string StepCompleted = "SagaStepCompleted";
    public const string StepFailed = "SagaStepFailed";
    public const string Completed = "SagaCompleted";
    public const string Failed = "SagaFailed";
    public const string Compensating = "SagaCompensating";
    public const string Compensated = "SagaCompensated";
    public const string Suspended = "SagaSuspended";
    public const string Resumed = "SagaResumed";
    public const string TimedOut = "SagaTimedOut";
    public const string Aborted = "SagaAborted";
}