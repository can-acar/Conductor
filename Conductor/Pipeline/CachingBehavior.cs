using Conductor.Core;
using Conductor.Interfaces;
using Microsoft.Extensions.Logging;

namespace Conductor.Pipeline;

public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
where TRequest : BaseRequest, ICacheableRequest
{
	private readonly ICacheService _cacheService;
	private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

	public CachingBehavior(ICacheService cacheService, ILogger<CachingBehavior<TRequest, TResponse>> logger)
	{
		_cacheService = cacheService;
		_logger = logger;
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		var cacheKey = request.GetCacheKey();
		if (!string.IsNullOrEmpty(cacheKey))
		{
			var cachedResponse = await _cacheService.GetAsync<TResponse>(cacheKey, cancellationToken);
			if (cachedResponse != null)
			{
				_logger.LogDebug("Cache hit for {RequestName} with key {CacheKey}",
					typeof(TRequest).Name, cacheKey);
				return cachedResponse;
			}
		}
		var response = await next();
		if (!string.IsNullOrEmpty(cacheKey) && response != null)
		{
			await _cacheService.SetAsync(cacheKey, response, request.GetCacheDuration(), cancellationToken);
			_logger.LogDebug("Cached response for {RequestName} with key {CacheKey} for {Duration}",
				typeof(TRequest).Name, cacheKey, request.GetCacheDuration());
		}
		return response;
	}
}