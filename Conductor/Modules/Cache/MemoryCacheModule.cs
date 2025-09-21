using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Conductor.Modules.Cache;

public class MemoryCacheModule : ICacheModule
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheModule> _logger;
    private readonly ConcurrentDictionary<string, HashSet<string>> _tagIndex = new();

    public MemoryCacheModule(IMemoryCache cache, ILogger<MemoryCacheModule> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        var result = _cache.TryGetValue(key, out var value) ? (T?)value : default;
        _logger.LogDebug("Cache {Operation} for key: {Key}", result != null ? "HIT" : "MISS", key);
        return Task.FromResult(result);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan expiration, bool slidingExpiration = false)
    {
        var options = new MemoryCacheEntryOptions();

        if (slidingExpiration)
            options.SlidingExpiration = expiration;
        else
            options.AbsoluteExpirationRelativeToNow = expiration;

        _cache.Set(key, value, options);
        _logger.LogDebug("Cache SET for key: {Key}, Expiration: {Expiration}", key, expiration);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        _logger.LogDebug("Cache REMOVE for key: {Key}", key);
        return Task.CompletedTask;
    }

    public Task RemoveByTagAsync(string tag)
    {
        if (_tagIndex.TryGetValue(tag, out var keys))
        {
            foreach (var key in keys)
            {
                _cache.Remove(key);
            }

            _tagIndex.TryRemove(tag, out _);
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0);
        }

        _tagIndex.Clear();
        return Task.CompletedTask;
    }
}