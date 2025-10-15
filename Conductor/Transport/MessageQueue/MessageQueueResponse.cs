using Conductor.Core;

namespace Conductor.Transport.MessageQueue;

public class MessageQueueResponse
{
    public bool Success { get; set; } = true;
    public object? Data { get; set; }
    public string? Message { get; set; }
    public List<string> Errors { get; set; } = new();
    public ResponseMetadata? Metadata { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string RoutingKey { get; set; } = string.Empty;
    public Dictionary<string, object> Headers { get; set; } = new();
}