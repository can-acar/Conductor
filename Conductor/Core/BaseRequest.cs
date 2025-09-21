namespace Conductor.Core;

public abstract class BaseRequest
{
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string? UserId { get; set; }
    public string? CorrelationId { get; set; }
}

// Core conductor interface

// Handler interfaces