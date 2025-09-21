namespace Conductor.Core;

public interface IConductor
{
// Generic methods for type safety
    Task<TResponse> Send<TResponse>(BaseRequest request, CancellationToken cancellationToken = default);
    Task<object> Send(BaseRequest request, CancellationToken cancellationToken = default);

    // Event publishing with multiple overloads for flexibility
    Task Publish<T>(Event<T> eventData, CancellationToken cancellationToken = default);
    Task Publish(IEvent eventData, CancellationToken cancellationToken = default);
    Task PublishAll(CancellationToken cancellationToken = default, params IEvent[] events);

    // Pipeline methods
    Task<TResponse> SendThrough<TResponse>(Bus<object> busData, CancellationToken cancellationToken = default);
    Task<TResponse> SendThrough<TData, TResponse>(Bus<TData> busData, CancellationToken cancellationToken = default);
    Task<object> SendThrough(IBus busData, CancellationToken cancellationToken = default);
}