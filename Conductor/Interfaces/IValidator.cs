using Conductor.Attributes;
using Conductor.Core;

namespace Conductor.Interfaces;

public interface IValidator<in T>
{
    Task<ValidationResult> ValidateAsync(T data, CancellationToken cancellationToken = default);
}