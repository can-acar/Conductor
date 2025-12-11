namespace Conductor.Interfaces;

public interface ICacheableRequest
{
	string GetCacheKey();
	TimeSpan GetCacheDuration();
}