using System.Text.Json;

namespace Conductor.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AuditAttribute : Attribute
{
    public bool LogRequest { get; set; } = true;
    public bool LogResponse { get; set; } = true;
    public bool LogExecutionTime { get; set; } = true;
    public bool LogParameters { get; set; } = true;
    public string? Category { get; set; }
    public AuditLevel Level { get; set; } = AuditLevel.Information;
    public bool IncludeSensitiveData { get; set; } = false;
    public int Priority { get; set; } = 50;
    public string[]? ExcludeProperties { get; set; }

    public AuditAttribute()
    {
    }

    public AuditAttribute(AuditLevel level)
    {
        Level = level;
    }

    public AuditAttribute(string category, AuditLevel level = AuditLevel.Information)
    {
        Category = category;
        Level = level;
    }
}

public enum AuditLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task LogBatchAsync(IEnumerable<AuditEntry> entries, CancellationToken cancellationToken = default);
}

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

public class AuditContext
{
    private static readonly AsyncLocal<AuditEntry?> _current = new();

    public static AuditEntry? Current => _current.Value;

    public static void Set(AuditEntry entry)
    {
        _current.Value = entry;
    }

    public static void Clear()
    {
        _current.Value = null;
    }

    public static void AddMetadata(string key, object value)
    {
        if (_current.Value != null)
        {
            _current.Value.Metadata[key] = value;
        }
    }
}