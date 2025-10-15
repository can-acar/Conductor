namespace Conductor.Core;

public abstract class BaseRequest
{
    public Dictionary<string, object> Metadata { get; set; } = new();
   
}