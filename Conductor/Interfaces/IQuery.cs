namespace Conductor.Interfaces;

public interface IQuery
{
    object Data { get; }
    Dictionary<string, object> Metadata { get; }
}