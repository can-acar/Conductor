using Conductor.Attributes;
using Conductor.Core;
using Conductor.Interfaces;
using Microsoft.Extensions.Logging;

namespace Conductor.Pipeline;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
where TRequest : BaseRequest
{
	private readonly IEnumerable<Conductor.Validation.IValidator<TRequest>> _validators;
	private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

	public ValidationBehavior(IEnumerable<Conductor.Validation.IValidator<TRequest>> validators, ILogger<ValidationBehavior<TRequest, TResponse>> logger)
	{
		_validators = validators;
		_logger = logger;
	}

	public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
	{
		if (_validators.Any())
		{
			var validationResults = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(request, cancellationToken)));
			var failures = validationResults.SelectMany(r => r.Errors).Where(f => f != null).ToList();
			if (failures.Any())
			{
				_logger.LogWarning("Validation failed for {RequestName}: {ValidationErrors}",
					typeof(TRequest).Name, string.Join(", ", failures.Select(f => f.ErrorMessage)));
				throw new ValidationException(ValidationResult.Failure(failures.ToArray()));
			}
		}
		return await next();
	}
}