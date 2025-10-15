using Conductor.Core;

namespace Conductor.Interfaces;

public interface IPipelineHandler<TData, TResponse>
{
    Task<TResponse> Handle(Bus<TData> busData);
}