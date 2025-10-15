using Conductor.Core;

namespace Conductor.Interfaces;

public interface IAuditService
{
    Task LogAsync(AuditRecord record, CancellationToken cancellationToken = default);
}