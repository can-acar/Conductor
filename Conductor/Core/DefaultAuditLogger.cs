using Conductor.Attributes;
using Conductor.Enums;
using Conductor.Interfaces;
using Microsoft.Extensions.Logging;

namespace Conductor.Core;

public class DefaultAuditLogger : IAuditLogger
{
	private readonly ILogger<DefaultAuditLogger> _logger;

	public DefaultAuditLogger(ILogger<DefaultAuditLogger> logger)
	{
		_logger = logger;
	}

	public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
	{
		var logLevel = entry.Level switch
		{
			AuditLevel.Trace => LogLevel.Trace,
			AuditLevel.Debug => LogLevel.Debug,
			AuditLevel.Information => LogLevel.Information,
			AuditLevel.Warning => LogLevel.Warning,
			AuditLevel.Error => LogLevel.Error,
			AuditLevel.Critical => LogLevel.Critical,
			_ => LogLevel.Information
		};
		var message = $"[AUDIT] {entry.HandlerType}.{entry.HandlerMethod} " +
					  $"| Request: {entry.RequestType} " +
					  $"| Duration: {entry.ExecutionTimeMs}ms " +
					  $"| Success: {entry.IsSuccess}";
		if (!string.IsNullOrEmpty(entry.Category))
			message = $"[{entry.Category}] {message}";
		_logger.Log(logLevel, message);
		if (entry.Level >= AuditLevel.Debug)
		{
			_logger.Log(logLevel, "Audit Details: {AuditEntry}", entry.ToJson());
		}
		return Task.CompletedTask;
	}

	public async Task LogBatchAsync(IEnumerable<AuditEntry> entries, CancellationToken cancellationToken = default)
	{
		foreach (var entry in entries)
		{
			await LogAsync(entry, cancellationToken);
		}
	}
}