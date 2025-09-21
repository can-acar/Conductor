namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class PipelineAttribute : Attribute
{
    public string PipelineName { get; set; }
    public int Order { get; set; } = 0;
    public bool IsAsync { get; set; } = true;
        
    public PipelineAttribute(string pipelineName)
    {
        PipelineName = pipelineName;
    }
}