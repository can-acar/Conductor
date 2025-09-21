namespace Conductor.Core;

public interface IQueryHandler<TQuery, TResponse>
{
    Task<TResponse> Handle(Query<TQuery> query);
}