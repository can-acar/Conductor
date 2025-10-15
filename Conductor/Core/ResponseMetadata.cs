namespace Conductor.Core;

public class ResponseMetadata
{
    public string? CorrelationId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? RequestId { get; set; }
    public string? UserId { get; set; }
    public Dictionary<string, object> CustomProperties { get; set; } = new();
}