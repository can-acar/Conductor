namespace Conductor.Interfaces;

public interface IEvent
{
    object Data { get; }
    DateTime Timestamp { get; }
    string EventId { get; }
    Dictionary<string, object> Metadata { get; }
}