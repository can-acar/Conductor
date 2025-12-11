using Conductor.Attributes;
using Conductor.Core;

namespace Conductor.Interfaces;

public interface IValidationRule<in T>
{
    Task<IEnumerable<ValidationError>> ValidateAsync(T instance, CancellationToken cancellationToken = default);
}