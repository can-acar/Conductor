using Conductor.Pipeline;

namespace Conductor.Core;

public class AuditRecord
{
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? CorrelationId { get; set; }
    public string? Details { get; set; }
    public AuditStatus Status { get; set; }
    public string? Response { get; set; }
    public string? ErrorMessage { get; set; }

    public IDictionary<string, string>? AuditDetails { get; set; }
}