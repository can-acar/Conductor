namespace Conductor.Interfaces;

public interface IBus
{
    object Data { get; }
    string CorrelationId { get; }
    Dictionary<string, object> Context { get; }
}