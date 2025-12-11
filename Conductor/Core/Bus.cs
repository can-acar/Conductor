using Conductor.Interfaces;

namespace Conductor.Core;

public class Bus<T>(T data) : BaseRequest, IBus
{
	public T Data { get; set; } = data;
	public string CorrelationId { get; } = Guid.NewGuid().ToString();
	public Dictionary<string, object> Context { get; set; } = new();
	object IBus.Data => Data!;

	public void AddMetadata(string key, object value)
	{
		Metadata[key] = value;
	}
}