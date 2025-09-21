namespace Conductor.Core;

public interface IPipelineHandler<TData, TResponse>
{
    Task<TResponse> Handle(Bus<TData> busData);
}