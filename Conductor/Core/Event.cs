using Conductor.Interfaces;

namespace Conductor.Core;

public class Event<T> : BaseRequest, IEvent
{
    public T Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string EventId { get; set; } = Guid.NewGuid().ToString();
        
    object IEvent.Data => Data!;
        
    public Event(T data)
    {
        Data = data;
    }
}