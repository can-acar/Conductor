using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Conductor.Saga;

public class SagaOrchestrator<TSagaState> : ISagaOrchestrator<TSagaState> where TSagaState : ISagaState
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISagaPersistence<TSagaState> _persistence;
    private readonly ISagaEventPublisher _eventPublisher;
    private readonly ILogger<SagaOrchestrator<TSagaState>> _logger;
    private readonly Dictionary<string, Type> _stepHandlers = new();

    public SagaOrchestrator(
        IServiceProvider serviceProvider,
        ISagaPersistence<TSagaState> persistence,
        ISagaEventPublisher eventPublisher,
        ILogger<SagaOrchestrator<TSagaState>> logger)
    {
        _serviceProvider = serviceProvider;
        _persistence = persistence;
        _eventPublisher = eventPublisher;
        _logger = logger;

        RegisterStepHandlers();
    }

    public async Task<TSagaState> StartAsync(TSagaState sagaState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Starting saga {SagaId} of type {SagaType}", sagaState.SagaId, sagaState.SagaType);

        sagaState.Status = SagaStatus.Running;
        sagaState.CreatedAt = DateTime.UtcNow;
        sagaState.Touch();

        await _persistence.SaveAsync(sagaState, cancellationToken);

        await PublishEventAsync(sagaState, SagaEventTypes.Started, sagaState, cancellationToken);

        // Start with first step if any steps are defined
        if (sagaState.Steps.Any())
        {
            var firstStep = sagaState.Steps.First();
            return await ExecuteStepAsync(sagaState, firstStep.Name, cancellationToken);
        }

        return sagaState;
    }

    public async Task<TSagaState> ContinueAsync(TSagaState sagaState, string stepName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        ArgumentException.ThrowIfNullOrEmpty(stepName);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Continuing saga {SagaId} with step {StepName}", sagaState.SagaId, stepName);

        return await ExecuteStepAsync(sagaState, stepName, cancellationToken);
    }

    public async Task<TSagaState> CompensateAsync(TSagaState sagaState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogWarning("Starting compensation for saga {SagaId}", sagaState.SagaId);

        sagaState.Status = SagaStatus.Compensating;
        sagaState.Touch();

        await _persistence.SaveAsync(sagaState, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.Compensating, sagaState, cancellationToken);

        var compensableSteps = sagaState.GetCompensableSteps();

        foreach (var step in compensableSteps)
        {
            try
            {
                await CompensateStepAsync(sagaState, step, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compensate step {StepName} for saga {SagaId}", step.Name, sagaState.SagaId);

                var compensation = new SagaCompensation
                {
                    StepName = step.Name,
                    Action = step.CompensationAction ?? "Unknown",
                    Status = SagaStepStatus.CompensationFailed,
                    ExecutedAt = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                };

                sagaState.Compensations.Add(compensation);
            }
        }

        sagaState.Status = SagaStatus.Compensated;
        sagaState.CompletedAt = DateTime.UtcNow;
        sagaState.Touch();

        await _persistence.SaveAsync(sagaState, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.Compensated, sagaState, cancellationToken);

        return sagaState;
    }

    public async Task<TSagaState> AbortAsync(TSagaState sagaState, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        ArgumentException.ThrowIfNullOrEmpty(reason);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogWarning("Aborting saga {SagaId}: {Reason}", sagaState.SagaId, reason);

        sagaState.Status = SagaStatus.Aborted;
        sagaState.CompletedAt = DateTime.UtcNow;
        sagaState.SetData("AbortReason", reason);
        sagaState.Touch();

        await _persistence.SaveAsync(sagaState, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.Aborted, new { Reason = reason }, cancellationToken);

        return sagaState;
    }

    public async Task<TSagaState> SuspendAsync(TSagaState sagaState, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        ArgumentException.ThrowIfNullOrEmpty(reason);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Suspending saga {SagaId}: {Reason}", sagaState.SagaId, reason);

        sagaState.Status = SagaStatus.Suspended;
        sagaState.SetData("SuspendReason", reason);
        sagaState.SetData("SuspendedAt", DateTime.UtcNow);
        sagaState.Touch();

        await _persistence.SaveAsync(sagaState, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.Suspended, new { Reason = reason }, cancellationToken);

        return sagaState;
    }

    public async Task<TSagaState> ResumeAsync(TSagaState sagaState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        cancellationToken.ThrowIfCancellationRequested();

        if (sagaState.Status != SagaStatus.Suspended)
        {
            throw new InvalidOperationException($"Cannot resume saga {sagaState.SagaId} with status {sagaState.Status}");
        }

        _logger.LogInformation("Resuming saga {SagaId}", sagaState.SagaId);

        sagaState.Status = SagaStatus.Running;
        sagaState.SetData("ResumedAt", DateTime.UtcNow);
        sagaState.Touch();

        await _persistence.SaveAsync(sagaState, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.Resumed, sagaState, cancellationToken);

        // Continue with current step
        if (!string.IsNullOrEmpty(sagaState.CurrentStep))
        {
            return await ContinueAsync(sagaState, sagaState.CurrentStep, cancellationToken);
        }

        return sagaState;
    }

    public async Task<bool> CanExecuteStepAsync(TSagaState sagaState, string stepName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        ArgumentException.ThrowIfNullOrEmpty(stepName);

        if (sagaState.Status != SagaStatus.Running)
        {
            return false;
        }

        var handler = await GetStepHandlerAsync(stepName, cancellationToken);
        if (handler == null)
        {
            return false;
        }

        return await handler.CanExecuteAsync(sagaState, cancellationToken);
    }

    public async Task<TSagaState> HandleTimeoutAsync(TSagaState sagaState, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sagaState);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogWarning("Handling timeout for saga {SagaId}", sagaState.SagaId);

        sagaState.Status = SagaStatus.TimedOut;
        sagaState.CompletedAt = DateTime.UtcNow;
        sagaState.Touch();

        var timeoutAction = sagaState.Metadata.TimeoutAction;

        if (timeoutAction == "Compensate")
        {
            return await CompensateAsync(sagaState, cancellationToken);
        }
        else if (timeoutAction == "Abort")
        {
            return await AbortAsync(sagaState, "Saga timed out", cancellationToken);
        }
        else
        {
            await _persistence.SaveAsync(sagaState, cancellationToken);
            await PublishEventAsync(sagaState, SagaEventTypes.TimedOut, sagaState, cancellationToken);
        }

        return sagaState;
    }

    private async Task<TSagaState> ExecuteStepAsync(TSagaState sagaState, string stepName, CancellationToken cancellationToken)
    {
        var step = sagaState.GetStep(stepName);
        if (step == null)
        {
            throw new InvalidOperationException($"Step {stepName} not found in saga {sagaState.SagaId}");
        }

        var handler = await GetStepHandlerAsync(stepName, cancellationToken);
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for step {stepName}");
        }

        _logger.LogDebug("Executing step {StepName} for saga {SagaId}", stepName, sagaState.SagaId);

        sagaState.CurrentStep = stepName;
        sagaState.UpdateStepStatus(stepName, SagaStepStatus.Running);

        await _persistence.SaveAsync(sagaState, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.StepStarted, new { StepName = stepName }, cancellationToken);

        try
        {
            var result = await ExecuteWithRetryAsync(handler, sagaState, step, cancellationToken);

            switch (result.Action)
            {
                case SagaStepAction.Continue:
                    return await HandleStepSuccess(sagaState, stepName, result, cancellationToken);

                case SagaStepAction.Complete:
                    return await CompleteSagaAsync(sagaState, cancellationToken);

                case SagaStepAction.Compensate:
                    return await CompensateAsync(sagaState, cancellationToken);

                case SagaStepAction.Suspend:
                    return await SuspendAsync(sagaState, result.ErrorMessage ?? "Step requested suspension", cancellationToken);

                case SagaStepAction.Abort:
                    return await AbortAsync(sagaState, result.ErrorMessage ?? "Step requested abort", cancellationToken);

                case SagaStepAction.Retry:
                    return await RetryStepAsync(sagaState, stepName, result.RetryDelay, cancellationToken);

                default:
                    throw new InvalidOperationException($"Unknown saga step action: {result.Action}");
            }
        }
        catch (Exception ex)
        {
            return await HandleStepFailure(sagaState, stepName, ex, cancellationToken);
        }
    }

    private async Task<SagaStepResult> ExecuteWithRetryAsync(
        ISagaStepHandler<TSagaState> handler,
        TSagaState sagaState,
        SagaStep step,
        CancellationToken cancellationToken)
    {
        while (step.RetryCount < step.MaxRetries)
        {
            try
            {
                var result = await handler.ExecuteAsync(sagaState, cancellationToken);

                if (result.IsSuccess || !result.ShouldRetry)
                {
                    return result;
                }

                step.RetryCount++;

                if (step.RetryCount < step.MaxRetries)
                {
                    _logger.LogWarning("Step {StepName} failed, retrying {RetryCount}/{MaxRetries}",
                        step.Name, step.RetryCount, step.MaxRetries);

                    if (result.RetryDelay.HasValue)
                    {
                        await Task.Delay(result.RetryDelay.Value, cancellationToken);
                    }
                }
                else
                {
                    return result; // Max retries exceeded
                }
            }
            catch (Exception ex)
            {
                step.RetryCount++;

                if (step.RetryCount >= step.MaxRetries)
                {
                    throw;
                }

                _logger.LogWarning(ex, "Step {StepName} failed with exception, retrying {RetryCount}/{MaxRetries}",
                    step.Name, step.RetryCount, step.MaxRetries);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, step.RetryCount)), cancellationToken);
            }
        }

        return SagaStepResult.Failure("Max retries exceeded");
    }

    private async Task<TSagaState> HandleStepSuccess(
        TSagaState sagaState,
        string stepName,
        SagaStepResult result,
        CancellationToken cancellationToken)
    {
        sagaState.UpdateStepStatus(stepName, SagaStepStatus.Completed,
            result.Output?.ToString(), null);

        // Add result data to saga state
        foreach (var kvp in result.Data)
        {
            sagaState.SetData(kvp.Key, kvp.Value);
        }

        await _persistence.SaveAsync(sagaState, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.StepCompleted,
            new { StepName = stepName, Output = result.Output }, cancellationToken);

        // Continue to next step if specified
        if (!string.IsNullOrEmpty(result.NextStep))
        {
            return await ContinueAsync(sagaState, result.NextStep, cancellationToken);
        }

        // Check if all steps are completed
        if (sagaState.Steps.All(s => s.Status == SagaStepStatus.Completed || s.Status == SagaStepStatus.Skipped))
        {
            return await CompleteSagaAsync(sagaState, cancellationToken);
        }

        return sagaState;
    }

    private async Task<TSagaState> HandleStepFailure(
        TSagaState sagaState,
        string stepName,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Step {StepName} failed for saga {SagaId}", stepName, sagaState.SagaId);

        sagaState.UpdateStepStatus(stepName, SagaStepStatus.Failed, null, exception.Message);
        sagaState.Status = SagaStatus.Failed;
        sagaState.CompletedAt = DateTime.UtcNow;

        await _persistence.SaveAsync(sagaState, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.StepFailed,
            new { StepName = stepName, Error = exception.Message }, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.Failed, sagaState, cancellationToken);

        // Auto-compensate if enabled
        if (sagaState.CanCompensate())
        {
            return await CompensateAsync(sagaState, cancellationToken);
        }

        return sagaState;
    }

    private async Task<TSagaState> CompleteSagaAsync(TSagaState sagaState, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Completing saga {SagaId}", sagaState.SagaId);

        sagaState.Status = SagaStatus.Completed;
        sagaState.CompletedAt = DateTime.UtcNow;
        sagaState.CurrentStep = string.Empty;
        sagaState.Touch();

        await _persistence.SaveAsync(sagaState, cancellationToken);
        await PublishEventAsync(sagaState, SagaEventTypes.Completed, sagaState, cancellationToken);

        return sagaState;
    }

    private async Task<TSagaState> RetryStepAsync(
        TSagaState sagaState,
        string stepName,
        TimeSpan? delay,
        CancellationToken cancellationToken)
    {
        if (delay.HasValue)
        {
            await Task.Delay(delay.Value, cancellationToken);
        }

        return await ContinueAsync(sagaState, stepName, cancellationToken);
    }

    private async Task CompensateStepAsync(TSagaState sagaState, SagaStep step, CancellationToken cancellationToken)
    {
        var handler = await GetStepHandlerAsync(step.Name, cancellationToken);
        if (handler == null)
        {
            throw new InvalidOperationException($"No handler found for step {step.Name}");
        }

        if (!await handler.CanCompensateAsync(sagaState, cancellationToken))
        {
            _logger.LogWarning("Step {StepName} cannot be compensated for saga {SagaId}", step.Name, sagaState.SagaId);
            return;
        }

        _logger.LogDebug("Compensating step {StepName} for saga {SagaId}", step.Name, sagaState.SagaId);

        var result = await handler.CompensateAsync(sagaState, cancellationToken);

        var compensation = new SagaCompensation
        {
            StepName = step.Name,
            Action = step.CompensationAction ?? handler.GetType().Name,
            Status = result.IsSuccess ? SagaStepStatus.Compensated : SagaStepStatus.CompensationFailed,
            ExecutedAt = DateTime.UtcNow,
            ErrorMessage = result.ErrorMessage,
            Input = step.Input,
            Output = result.Output?.ToString()
        };

        sagaState.Compensations.Add(compensation);
    }

    private async Task<ISagaStepHandler<TSagaState>?> GetStepHandlerAsync(string stepName, CancellationToken cancellationToken)
    {
        if (!_stepHandlers.TryGetValue(stepName, out var handlerType))
        {
            return null;
        }

        return _serviceProvider.GetService(handlerType) as ISagaStepHandler<TSagaState>;
    }

    private void RegisterStepHandlers()
    {
        var handlers = _serviceProvider.GetServices<ISagaStepHandler<TSagaState>>();

        foreach (var handler in handlers)
        {
            _stepHandlers[handler.StepName] = handler.GetType();
        }
    }

    private async Task PublishEventAsync<T>(TSagaState sagaState, string eventType, T data, CancellationToken cancellationToken)
    {
        var sagaEvent = new SagaEvent<T>
        {
            SagaId = sagaState.SagaId,
            SagaType = sagaState.SagaType,
            EventType = eventType,
            Data = data,
            CorrelationId = sagaState.CorrelationId,
            Metadata = new Dictionary<string, object>
            {
                ["Status"] = sagaState.Status.ToString(),
                ["CurrentStep"] = sagaState.CurrentStep,
                ["Version"] = sagaState.Version
            }
        };

        await _eventPublisher.PublishAsync(sagaEvent, cancellationToken);
    }
}