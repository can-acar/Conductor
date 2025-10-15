namespace Conductor.Interfaces;

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