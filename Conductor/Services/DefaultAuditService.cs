using Conductor.Core;
using Conductor.Interfaces;
using Microsoft.Extensions.Logging;

namespace Conductor.Services;

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