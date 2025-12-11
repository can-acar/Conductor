using Conductor.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Conductor.Interfaces;

public interface ICorrelationIdHelper
{
    void SetCorrelationId(string correlationId);
    string? GetCorrelationId();
}
