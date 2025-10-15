using Conductor.Core;

namespace Conductor.Interfaces;

public interface IQueryHandler<TQuery, TResponse>
{
    Task<TResponse> Handle(Query<TQuery> query);
}