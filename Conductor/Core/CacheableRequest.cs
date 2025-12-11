using Conductor.Attributes;
using Conductor.Interfaces;

namespace Conductor.Core;

public abstract class CacheableRequest : BaseRequest, ICacheableRequest
{
	public virtual string GetCacheKey()
	{
		var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
								 .FirstOrDefault() as CacheableAttribute;

		// take first 8 chars of a new GUID for uniqueness
		var uuid = Guid.NewGuid().ToString("N").Substring(0, 8);
		// or use full GUID if preferred
		if (!string.IsNullOrEmpty(attribute?.CacheKey))
		{
			return attribute.CacheKey;
		}
		if (attribute?.UseRequestData == true)
		{
			var json = System.Text.Json.JsonSerializer.Serialize(this);
			var hash = json.GetHashCode();
			return $"{GetType().Name}_{hash:X}";
		}
		return $"{GetType().Name}_{uuid}";
	}

	public virtual TimeSpan GetCacheDuration()
	{
		var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
								 .FirstOrDefault() as CacheableAttribute;
		return attribute?.GetDuration() ?? TimeSpan.FromMinutes(5);
	}
}