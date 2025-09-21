namespace Conductor.Modules.Cache;

public interface ICacheModule
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan expiration, bool slidingExpiration = false);
    Task RemoveAsync(string key);
    Task RemoveByTagAsync(string tag);
    Task ClearAsync();
}