using System.Text.Json;

namespace Conductor.Saga;

public interface ISagaState
{
    Guid SagaId { get; set; }
    string SagaType { get; set; }
    SagaStatus Status { get; set; }
    string CurrentStep { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime? CompletedAt { get; set; }
    DateTime LastUpdatedAt { get; set; }
    int Version { get; set; }
    string? CorrelationId { get; set; }
    Dictionary<string, object> Data { get; set; }
    List<SagaStep> Steps { get; set; }
    List<SagaCompensation> Compensations { get; set; }
    SagaMetadata Metadata { get; set; }

    // Helper methods used by orchestrator and common saga flow
    void Touch();
    T GetData<T>(string key, T defaultValue = default!);
    void SetData<T>(string key, T value);
    SagaStep? GetStep(string stepName);
    void AddStep(SagaStep step);
    void UpdateStepStatus(string stepName, SagaStepStatus status, string? output = null, string? errorMessage = null);
    bool CanCompensate();
    List<SagaStep> GetCompensableSteps();
}

public enum SagaStatus
{
    NotStarted = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Compensating = 4,
    Compensated = 5,
    Aborted = 6,
    Suspended = 7,
    TimedOut = 8
}

public class SagaStep
{
    public string Name { get; set; } = string.Empty;
    public string StepType { get; set; } = string.Empty;
    public SagaStepStatus Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public TimeSpan? Timeout { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string? CompensationAction { get; set; }
    public bool IsCompensable { get; set; } = true;
}

public enum SagaStepStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4,
    Compensating = 5,
    Compensated = 6,
    CompensationFailed = 7
}

public class SagaCompensation
{
    public string StepName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public SagaStepStatus Status { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
}

public class SagaMetadata
{
    public string? InitiatedBy { get; set; }
    public string? BusinessContext { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public TimeSpan? Timeout { get; set; }
    public string? TimeoutAction { get; set; }
    public bool IsCritical { get; set; }
    public int Priority { get; set; } = 1;
    public string? ParentSagaId { get; set; }
    public List<string> ChildSagaIds { get; set; } = new();
}

public class DefaultSagaState : ISagaState
{
    public Guid SagaId { get; set; } = Guid.NewGuid();
    public string SagaType { get; set; } = string.Empty;
    public SagaStatus Status { get; set; } = SagaStatus.NotStarted;
    public string CurrentStep { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; } = 1;
    public string? CorrelationId { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public List<SagaStep> Steps { get; set; } = new();
    public List<SagaCompensation> Compensations { get; set; } = new();
    public SagaMetadata Metadata { get; set; } = new();

    public T GetData<T>(string key, T defaultValue = default!)
    {
        if (Data.TryGetValue(key, out var value))
        {
            if (value is JsonElement jsonElement)
            {
                return jsonElement.Deserialize<T>() ?? defaultValue;
            }

            if (value is T directValue)
            {
                return directValue;
            }

            // Try to convert
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        return defaultValue;
    }

    public void SetData<T>(string key, T value)
    {
        Data[key] = value!;
        Touch();
    }

    public void Touch()
    {
        LastUpdatedAt = DateTime.UtcNow;
        Version++;
    }

    public SagaStep? GetStep(string stepName)
    {
        return Steps.FirstOrDefault(s => s.Name == stepName);
    }

    public void AddStep(SagaStep step)
    {
        Steps.Add(step);
        Touch();
    }

    public void UpdateStepStatus(string stepName, SagaStepStatus status, string? output = null, string? errorMessage = null)
    {
        var step = GetStep(stepName);
        if (step != null)
        {
            step.Status = status;
            step.Output = output;
            step.ErrorMessage = errorMessage;

            if (status == SagaStepStatus.Running && !step.StartedAt.HasValue)
            {
                step.StartedAt = DateTime.UtcNow;
            }
            else if (status == SagaStepStatus.Completed || status == SagaStepStatus.Failed)
            {
                step.CompletedAt = DateTime.UtcNow;
            }

            Touch();
        }
    }

    public bool CanCompensate()
    {
        return Steps.Any(s => s.Status == SagaStepStatus.Completed && s.IsCompensable);
    }

    public List<SagaStep> GetCompensableSteps()
    {
        return Steps
            .Where(s => s.Status == SagaStepStatus.Completed && s.IsCompensable)
            .OrderByDescending(s => s.CompletedAt)
            .ToList();
    }
}