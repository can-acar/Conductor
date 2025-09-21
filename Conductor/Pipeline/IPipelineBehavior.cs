namespace Conductor.Pipeline;

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : Core.BaseRequest
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

public interface IRequestPreProcessor<in TRequest> where TRequest : Core.BaseRequest
{
    Task Process(TRequest request, CancellationToken cancellationToken);
}

public interface IRequestPostProcessor<in TRequest, in TResponse> where TRequest : Core.BaseRequest
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}

public interface IPipelineExecutor
{
    Task<TResponse> ExecuteAsync<TResponse>(Core.BaseRequest request, CancellationToken cancellationToken);
}

public class PipelineContext
{
    public Dictionary<string, object> Items { get; } = new();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string? UserId { get; set; }
    public string? CorrelationId { get; set; }
}