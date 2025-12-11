using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Logging;
using Conductor.Interfaces;

namespace Conductor.Core;

/// <summary>
/// Helper for generating, validating, extracting, and logging correlation identifiers for distributed systems.
/// Fast, allocation-conscious operations with optional prefixes and HTTP/Logger utilities.
/// </summary>
public class CorrelationIdHelper : ICorrelationIdHelper
{
	private string _correlationId;
	private readonly IHttpContextAccessor _httpContextAccessor;
	private readonly string[] _refIdKeys = { "traceparent", "x-correlation-id", "x-client-trace-id", "x-request-id" };

	public CorrelationIdHelper(IHttpContextAccessor httpContextAccessor)
	{
		// null checked in DI
		_httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
	}

	public string? GetCorrelationId()
	{
		if (HasCorrelationId())
		{
			if (_correlationId == null)
			{
				var context = _httpContextAccessor.HttpContext;
				if (context != null)
				{
					foreach (var refKey in _refIdKeys)
					{
						if (context.Request.Headers.TryGetValue(refKey, out StringValues values) && !StringValues.IsNullOrEmpty(values))
						{
							_correlationId = values.First()!;
							break;
						}
					}
				}
			}
		}
		else
		{
			_correlationId = Guid.NewGuid().ToString();
			SetKeyHeader();
		}
		return _correlationId;
	}

	public void SetCorrelationId(string correlationId)
	{
		_correlationId = correlationId;
		SetKeyHeader();
	}

	private bool HasCorrelationId()
	{
		var context = _httpContextAccessor.HttpContext;
		if (context == null)
			return false;
		return context.Request.Headers.ContainsKey("traceparent");
	}

	private void SetKeyHeader()
	{
		var context = _httpContextAccessor.HttpContext;
		if (context != null)
		{
			if (HasCorrelationId())
			{
				// already has one, remove it
				foreach (var refKey in _refIdKeys)
				{
					if (context.Request.Headers.ContainsKey(refKey))
					{
						context.Request.Headers.Remove(refKey);
					}
					context.Request.Headers.Append(refKey, _correlationId!);
				}
			}
		}
	}
}