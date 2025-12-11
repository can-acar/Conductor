using Conductor.Attributes;
using Conductor.Core;

namespace Conductor.Interfaces;

public interface IAuditLogger
{
	Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
	Task LogBatchAsync(IEnumerable<AuditEntry> entries, CancellationToken cancellationToken = default);
}