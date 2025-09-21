namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class HandleAttribute : Attribute
{
    public Type? RequestType { get; set; }
    public Type? ResponseType { get; set; }
    public int Priority { get; set; } = 0;
}