using Conductor.Core;
using Conductor.Enums;
using Conductor.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Conductor.Pipeline;

public class AuditingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
where TRequest : BaseRequest, IAuditableRequest
{
	private readonly IAuditService _auditService;
	private readonly ILogger<AuditingBehavior<TRequest, TResponse>> _logger;
	private readonly IHttpContextAccessor _httpContextAccessor;

	public AuditingBehavior(IAuditService auditService, ILogger<AuditingBehavior<TRequest, TResponse>> logger, IHttpContextAccessor httpContextAccessor)
	{
		_auditService = auditService;
		_logger = logger;
		_httpContextAccessor = httpContextAccessor;
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		var correlationId = _httpContextAccessor.HttpContext?.TraceIdentifier;
		var auditRecord = new AuditRecord
		{
			Action = typeof(TRequest).Name,
			Timestamp = DateTime.UtcNow,
			CorrelationId = correlationId,
			Details = request.GetAuditDetails()
		};
		try
		{
			var response = await next();
			auditRecord.Status = AuditStatus.Success;
			auditRecord.Response = response?.ToString();
			await _auditService.LogAsync(auditRecord, cancellationToken);
			_logger.LogDebug("Audit logged for successful {RequestName}  CorrelationId {correlationId}", typeof(TRequest).Name, correlationId);
			return response;
		}
		catch(Exception ex)
		{
			auditRecord.Status = AuditStatus.Failed;
			auditRecord.ErrorMessage = ex.Message;
			await _auditService.LogAsync(auditRecord, cancellationToken);
			_logger.LogWarning("Audit logged for failed {RequestName}  CorrelationId {CorrelationId}: {Error}",
				typeof(TRequest).Name, correlationId, ex.Message);
			throw;
		}
	}
}