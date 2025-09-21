namespace Conductor.Modules.Pipeline;

public interface IPipelineStep
{
    Task<object> ExecuteAsync(object input, CancellationToken cancellationToken = default);
    int Order { get; }
    string StepName { get; }
}