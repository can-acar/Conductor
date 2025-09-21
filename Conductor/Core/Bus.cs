namespace Conductor.Core;

public class Bus<T> : BaseRequest, IBus
{
    public T Data { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public Dictionary<string, object> Context { get; set; } = new();
        
    object IBus.Data => Data!;
        
    public Bus(T data)
    {
        Data = data;
    }
}