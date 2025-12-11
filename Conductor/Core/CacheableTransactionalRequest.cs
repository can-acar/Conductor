using Conductor.Attributes;
using Conductor.Interfaces;

namespace Conductor.Core;

public abstract class CacheableTransactionalRequest : BaseRequest, ICacheableRequest, ITransactionalRequest
{
	public virtual string GetCacheKey()
	{
		var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
								 .FirstOrDefault() as CacheableAttribute;
		if (!string.IsNullOrEmpty(attribute?.CacheKey))
		{
			return attribute.CacheKey;
		}
		var json = System.Text.Json.JsonSerializer.Serialize(this);
		var hash = json.GetHashCode();
		return $"{GetType().Name}_{hash:X}";
	}

	public virtual TimeSpan GetCacheDuration()
	{
		var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
								 .FirstOrDefault() as CacheableAttribute;
		return attribute?.GetDuration() ?? TimeSpan.FromMinutes(5);
	}

	public virtual bool RequiresTransaction
	{
		get
		{
			var attribute = GetType().GetCustomAttributes(typeof(TransactionalAttribute), true)
									 .FirstOrDefault() as TransactionalAttribute;
			return attribute?.RequireTransaction ?? true;
		}
	}
}