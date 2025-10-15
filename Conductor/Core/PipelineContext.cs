namespace Conductor.Core;

public class PipelineContext
{
    public Dictionary<string, object> Items { get; } = new();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}