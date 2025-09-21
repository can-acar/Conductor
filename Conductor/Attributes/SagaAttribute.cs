namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class SagaAttribute : Attribute
{
    public string SagaName { get; set; }
    public int Order { get; set; } = 0;
    public bool IsCompensating { get; set; } = false;
        
    public SagaAttribute(string sagaName)
    {
        SagaName = sagaName;
    }
}