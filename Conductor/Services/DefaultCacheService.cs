using Conductor.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Conductor.Services;

public class DefaultCacheService : ICacheService
{
	private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

	public DefaultCacheService(Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
	{
		_cache = cache;
	}

	public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
	{
		var value = _cache.Get<T>(key);
		return Task.FromResult(value);
	}

	public Task SetAsync<T>(string key, T value, TimeSpan duration, CancellationToken cancellationToken = default)
	{
		_cache.Set(key, value, duration);
		return Task.CompletedTask;
	}

	public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
	{
		_cache.Remove(key);
		return Task.CompletedTask;
	}
}