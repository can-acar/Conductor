using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Conductor.Core;
using Conductor.Interfaces;
using Conductor.Services;
using Microsoft.AspNetCore.Http;

namespace Conductor.Pipeline;

public class PipelineExecutor : IPipelineExecutor
{
	private readonly IServiceProvider _serviceProvider;
	private readonly ILogger<PipelineExecutor> _logger;
	private readonly IHttpContextAccessor _contextAccessor;

	public PipelineExecutor(IServiceProvider serviceProvider, ILogger<PipelineExecutor> logger, IHttpContextAccessor contextAccessor)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
		_contextAccessor = contextAccessor;
	}

	public async Task<TResponse> ExecuteAsync<TResponse>(BaseRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();
		var correlationId = _contextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();
		var context = new PipelineContext
		{
			CorrelationId = correlationId
		};
		try
		{
			// Get all pipeline behaviors for this request type
			var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(request.GetType(), typeof(TResponse));
			var behaviors = _serviceProvider.GetServices(behaviorType).Cast<IPipelineBehavior<BaseRequest, TResponse>>().ToList();

			// Get pre-processors
			var preProcessorType = typeof(IRequestPreProcessor<>).MakeGenericType(request.GetType());
			var preProcessors = _serviceProvider.GetServices(preProcessorType).Cast<IRequestPreProcessor<BaseRequest>>().ToList();

			// Get post-processors
			var postProcessorType = typeof(IRequestPostProcessor<,>).MakeGenericType(request.GetType(), typeof(TResponse));
			var postProcessors = _serviceProvider.GetServices(postProcessorType).Cast<IRequestPostProcessor<BaseRequest, TResponse>>().ToList();

			// Execute pre-processors
			foreach (var preProcessor in preProcessors)
			{
				await preProcessor.Process(request, cancellationToken);
			}

			// Build pipeline
			RequestHandlerDelegate<TResponse> handler = () => ExecuteHandlerAsync<TResponse>(request, cancellationToken);

			// Execute behaviors in reverse order (last registered executes first)
			for (var i = behaviors.Count - 1; i >= 0; i--)
			{
				var behavior = behaviors[i];
				var nextHandler = handler;
				handler = () => behavior.Handle(request, nextHandler, cancellationToken);
			}

			// Execute the pipeline
			var response = await handler();

			// Execute post-processors
			foreach (var postProcessor in postProcessors)
			{
				await postProcessor.Process(request, response, cancellationToken);
			}
			return response;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Pipeline execution failed for request {RequestType} with ID {RequestId}",
				request.GetType().Name, context.CorrelationId);
			throw;
		}
	}

	private async Task<TResponse> ExecuteHandlerAsync<TResponse>(BaseRequest request, CancellationToken cancellationToken)
	{
		// Get the actual handler from ConductorService
		using var scope = _serviceProvider.CreateScope();
		var conductorService = scope.ServiceProvider.GetRequiredService<ConductorService>();
		var result = await conductorService.ExecuteHandlerAsync(request, cancellationToken);
		if (result is TResponse typedResponse)
		{
			return typedResponse;
		}
		if (typeof(TResponse) == typeof(object))
		{
			return (TResponse)result;
		}
		throw new InvalidOperationException($"Handler returned {result?.GetType().Name} but expected {typeof(TResponse).Name}");
	}
}

public static class PipelineContextExtensions
{
	private static readonly AsyncLocal<PipelineContext?> _context = new();

	public static PipelineContext? Current
	{
		get => _context.Value;
		set => _context.Value = value;
	}

	public static void SetContext(PipelineContext context)
	{
		_context.Value = context;
	}

	public static void ClearContext()
	{
		_context.Value = null;
	}

	public static T? GetItem<T>(this PipelineContext context, string key)
	{
		if (context.Items.TryGetValue(key, out var value) && value is T typedValue)
		{
			return typedValue;
		}
		return default;
	}

	public static void SetItem<T>(this PipelineContext context, string key, T value)
	{
		context.Items[key] = value!;
	}
}