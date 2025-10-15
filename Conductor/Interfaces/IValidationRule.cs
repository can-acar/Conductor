using Conductor.Attributes;

namespace Conductor.Validation;

public interface IValidationRule<in T>
{
    Task<IEnumerable<ValidationError>> ValidateAsync(T instance, CancellationToken cancellationToken = default);
}