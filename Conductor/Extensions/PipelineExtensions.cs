using Conductor.Core;
using Conductor.Interfaces;
using Conductor.Pipeline;
using Conductor.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Conductor.Extensions;

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

public static class PipelineExtensions
{
	public static IServiceCollection AddConductorPipeline(this IServiceCollection services)
	{
		// Register pipeline infrastructure
		services.TryAddScoped<IPipelineExecutor, PipelineExecutor>();

		// Register built-in behaviors
		services.AddDefaultPipelineBehaviors();
		return services;
	}

	private static IServiceCollection AddDefaultPipelineBehaviors(this IServiceCollection services)
	{
		// Register logging behavior for all requests
		services.TryAddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

		// Register performance behavior for all requests
		services.TryAddScoped(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

		// Register validation behavior for all requests
		services.TryAddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
		return services;
	}

	public static IServiceCollection AddPipelineBehavior<TBehavior>(this IServiceCollection services)
	where TBehavior : class
	{
		var behaviorInterfaces = typeof(TBehavior).GetInterfaces()
												  .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))
												  .ToList();
		if (!behaviorInterfaces.Any())
		{
			throw new ArgumentException($"Type {typeof(TBehavior).Name} does not implement IPipelineBehavior<,>");
		}
		foreach (var behaviorInterface in behaviorInterfaces)
		{
			services.AddScoped(behaviorInterface, typeof(TBehavior));
		}
		return services;
	}

	public static IServiceCollection AddPipelineBehavior<TBehavior>(this IServiceCollection services, ServiceLifetime lifetime)
	where TBehavior : class
	{
		var behaviorInterfaces = typeof(TBehavior).GetInterfaces()
												  .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))
												  .ToList();
		if (!behaviorInterfaces.Any())
		{
			throw new ArgumentException($"Type {typeof(TBehavior).Name} does not implement IPipelineBehavior<,>");
		}
		foreach (var behaviorInterface in behaviorInterfaces)
		{
			services.Add(new ServiceDescriptor(behaviorInterface, typeof(TBehavior), lifetime));
		}
		return services;
	}

	public static IServiceCollection AddPipelineBehavior<TRequest, TResponse, TBehavior>(this IServiceCollection services)
	where TRequest : BaseRequest
	where TBehavior : class, IPipelineBehavior<TRequest, TResponse>
	{
		services.AddScoped<IPipelineBehavior<TRequest, TResponse>, TBehavior>();
		return services;
	}

	public static IServiceCollection AddPipelineBehavior<TRequest, TResponse, TBehavior>(this IServiceCollection services, ServiceLifetime lifetime)
	where TRequest : BaseRequest
	where TBehavior : class, IPipelineBehavior<TRequest, TResponse>
	{
		services.Add(new ServiceDescriptor(typeof(IPipelineBehavior<TRequest, TResponse>), typeof(TBehavior), lifetime));
		return services;
	}

	public static IServiceCollection AddRequestPreProcessor<TRequest, TPreProcessor>(this IServiceCollection services)
	where TRequest : BaseRequest
	where TPreProcessor : class, IRequestPreProcessor<TRequest>
	{
		services.AddScoped<IRequestPreProcessor<TRequest>, TPreProcessor>();
		return services;
	}

	public static IServiceCollection AddRequestPostProcessor<TRequest, TResponse, TPostProcessor>(this IServiceCollection services)
	where TRequest : BaseRequest
	where TPostProcessor : class, IRequestPostProcessor<TRequest, TResponse>
	{
		services.AddScoped<IRequestPostProcessor<TRequest, TResponse>, TPostProcessor>();
		return services;
	}

	public static IServiceCollection AddCachingBehavior(this IServiceCollection services)
	{
		services.TryAddScoped(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
		return services;
	}

	public static IServiceCollection AddTransactionBehavior(this IServiceCollection services)
	{
		services.TryAddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
		return services;
	}

	public static IServiceCollection AddAuditingBehavior(this IServiceCollection services)
	{
		services.TryAddScoped(typeof(IPipelineBehavior<,>), typeof(AuditingBehavior<,>));
		return services;
	}

	public static IServiceCollection ConfigurePipelinePerformance(this IServiceCollection services, TimeSpan warningThreshold)
	{
		services.Configure<PipelinePerformanceOptions>(options => { options.WarningThreshold = warningThreshold; });
		services.AddSingleton(typeof(TimeSpan), provider =>
		{
			var options = provider.GetService<Microsoft.Extensions.Options.IOptions<PipelinePerformanceOptions>>();
			return (object)(options?.Value.WarningThreshold ?? TimeSpan.FromMilliseconds(500));
		});
		return services;
	}

	private static IServiceCollection AddPipelineServices(this IServiceCollection services)
	{
		// Add supporting services for pipeline behaviors
		services.TryAddScoped<ICacheService, DefaultCacheService>();
		services.TryAddScoped<ITransactionService, DefaultTransactionService>();
		services.TryAddScoped<IAuthorizationService, DefaultAuthorizationService>();
		services.TryAddScoped<IAuditService, DefaultAuditService>();
		return services;
	}

	public static IServiceCollection AddConductorWithPipeline(this IServiceCollection services)
	{
		// Add core Conductor services
		services.AddConductor();

		// Add pipeline infrastructure
		services.AddConductorPipeline();

		// Add supporting services
		services.AddPipelineServices();
		return services;
	}
}

public class PipelinePerformanceOptions
{
	public TimeSpan WarningThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
}

// Default implementations for supporting services

public class DefaultTransaction : ITransaction
{
	private bool _completed = false;
	private bool _disposed = false;

	public Task CommitAsync(CancellationToken cancellationToken = default)
	{
		_completed = true;
		return Task.CompletedTask;
	}

	public Task RollbackAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		if (!_disposed)
		{
			if (!_completed)
			{
				// Auto-rollback if not committed
			}
			_disposed = true;
		}
		return ValueTask.CompletedTask;
	}
}

public class DefaultAuditService : IAuditService
{
	private readonly Microsoft.Extensions.Logging.ILogger<DefaultAuditService> _logger;

	public DefaultAuditService(Microsoft.Extensions.Logging.ILogger<DefaultAuditService> logger)
	{
		_logger = logger;
	}

	public Task LogAsync(AuditRecord record, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Audit: {UserId} performed {Action} at {Timestamp} - Status: {Status}",
			record.CorrelationId, record.Action, record.Timestamp, record.Status);
		if (!string.IsNullOrEmpty(record.ErrorMessage))
		{
			_logger.LogError("Audit Error: {ErrorMessage}", record.ErrorMessage);
		}
		return Task.CompletedTask;
	}
}