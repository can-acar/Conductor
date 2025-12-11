namespace Conductor.Modules.Pipeline;

public abstract class BasePipelineStep : IPipelineStep
{
	public abstract int Order { get; }
	public abstract string StepName { get; }
	public abstract Task<object> ExecuteAsync(object input, CancellationToken cancellationToken = default);
}