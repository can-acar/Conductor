using System.Text.Json;
using Conductor.Enums;

namespace Conductor.Core;

public class AuditEntry
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
	public string? UserId { get; set; }
	public string? UserName { get; set; }
	public string? SessionId { get; set; }
	public string? CorrelationId { get; set; }
	public string? IPAddress { get; set; }
	public string? UserAgent { get; set; }
	public string HandlerType { get; set; } = string.Empty;
	public string HandlerMethod { get; set; } = string.Empty;
	public string RequestType { get; set; } = string.Empty;
	public object? RequestData { get; set; }
	public object? ResponseData { get; set; }
	public long ExecutionTimeMs { get; set; }
	public bool IsSuccess { get; set; } = true;
	public string? ErrorMessage { get; set; }
	public string? StackTrace { get; set; }
	public AuditLevel Level { get; set; } = AuditLevel.Information;
	public string? Category { get; set; }
	public Dictionary<string, object> Metadata { get; set; } = new();

	public string ToJson(JsonSerializerOptions? options = null)
	{
		return JsonSerializer.Serialize(this, options ?? new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = false
		});
	}
}