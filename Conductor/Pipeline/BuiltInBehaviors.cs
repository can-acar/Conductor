using Microsoft.Extensions.Logging;
using Conductor.Attributes;
using Conductor.Core;
using Conductor.Interfaces;
using Conductor.Validation;
using Microsoft.AspNetCore.Http;

namespace Conductor.Pipeline;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
where TRequest : BaseRequest
{
	private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
	private readonly IHttpContextAccessor _httpContextAccessor;

	public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger, IHttpContextAccessor httpContextAccessor)
	{
		_logger = logger;
		_httpContextAccessor = httpContextAccessor;
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		var requestName = typeof(TRequest).Name;
		// get header from request if exists otherwise generate new guid
		var correlationId = _httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
							?? Guid.NewGuid().ToString();
		_logger.LogInformation("Handling {RequestName} with ID {CorrelationId}", requestName, correlationId);
		try
		{
			var response = await next();
			_logger.LogInformation("Successfully handled {RequestName} with ID {CorrelationId}",
				requestName, correlationId);
			return response;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error handling {RequestName} with ID {CorrelationId}: {ErrorMessage}",
				requestName, correlationId, ex.Message);
			throw;
		}
	}
}