namespace Conductor.Modules.Pipeline;

public interface IPipelineModule
{
    Task<TResponse> ExecutePipeline<TResponse>(string pipelineName, object data,
        CancellationToken cancellationToken = default);

    void RegisterPipelineStep<TStep>(string pipelineName, int order) where TStep : class, IPipelineStep;
}