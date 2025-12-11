namespace Conductor.Core;

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