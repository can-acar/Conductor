namespace Conductor.Core;

public interface IQuery
{
    object Data { get; }
    Dictionary<string, object> Metadata { get; }
}