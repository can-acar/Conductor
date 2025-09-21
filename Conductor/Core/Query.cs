namespace Conductor.Core;

public class Query<T> : BaseRequest
{
    public T Data { get; set; }

    public Query(T data)
    {
        Data = data;
    }
}