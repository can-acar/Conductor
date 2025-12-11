using System.Diagnostics;
using Conductor.Core;
using Conductor.Interfaces;
using Microsoft.Extensions.Logging;

namespace Conductor.Pipeline;

public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
where TRequest : BaseRequest
{
	private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
	private readonly TimeSpan _warningThreshold;

	public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> logger, TimeSpan? warningThreshold = null)
	{
		_logger = logger;
		_warningThreshold = warningThreshold ?? TimeSpan.FromMilliseconds(500);
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		var stopwatch = Stopwatch.StartNew();
		var requestName = typeof(TRequest).Name;
		try
		{
			var response = await next();
			stopwatch.Stop();
			if (stopwatch.Elapsed > _warningThreshold)
			{
				_logger.LogWarning("Slow request detected: {RequestName} took {ElapsedMs}ms (Threshold: {ThresholdMs}ms)",
					requestName, stopwatch.ElapsedMilliseconds, _warningThreshold.TotalMilliseconds);
			}
			else
			{
				_logger.LogDebug("Request {RequestName} completed in {ElapsedMs}ms",
					requestName, stopwatch.ElapsedMilliseconds);
			}

			// Store performance metrics in context
			if (PipelineContextExtensions.Current != null)
			{
				PipelineContextExtensions.Current.SetItem("ExecutionTime", stopwatch.Elapsed);
				PipelineContextExtensions.Current.SetItem("RequestName", requestName);
			}
			return response;
		}
		catch(Exception)
		{
			stopwatch.Stop();
			_logger.LogWarning("Failed request {RequestName} took {ElapsedMs}ms before failure",
				requestName, stopwatch.ElapsedMilliseconds);
			throw;
		}
	}
}