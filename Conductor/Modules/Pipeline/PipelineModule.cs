using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Conductor.Modules.Pipeline;

public class PipelineModule : IPipelineModule
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PipelineModule> _logger;
    private readonly ConcurrentDictionary<string, List<Type>> _pipelines = new();

    public PipelineModule(IServiceProvider serviceProvider, ILogger<PipelineModule> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void RegisterPipelineStep<TStep>(string pipelineName, int order) where TStep : class, IPipelineStep
    {
        _pipelines.AddOrUpdate(pipelineName,
            new List<Type> { typeof(TStep) },
            (key, existing) =>
            {
                existing.Add(typeof(TStep));
                return existing.OrderBy(t =>
                {
                    var instance = Activator.CreateInstance(t) as IPipelineStep;
                    return instance?.Order ?? 0;
                }).ToList();
            });
    }

    public async Task<TResponse> ExecutePipeline<TResponse>(string pipelineName, object data,
        CancellationToken cancellationToken = default)
    {
        if (!_pipelines.TryGetValue(pipelineName, out var stepTypes))
        {
            throw new InvalidOperationException($"Pipeline '{pipelineName}' not found");
        }

        object currentData = data;

        foreach (var stepType in stepTypes)
        {
            var step = _serviceProvider.GetService(stepType) as IPipelineStep
                       ?? Activator.CreateInstance(stepType) as IPipelineStep;

            if (step == null)
                throw new InvalidOperationException($"Could not create pipeline step: {stepType.Name}");

            _logger.LogDebug("Executing pipeline step: {StepName}", step.StepName);
            currentData = await step.ExecuteAsync(currentData, cancellationToken);
        }

        return (TResponse)currentData;
    }
}