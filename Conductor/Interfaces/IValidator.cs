using Conductor.Attributes;

namespace Conductor.Interfaces;

public interface IValidator<in T>
{
    Task<ValidationResult> ValidateAsync(T data, CancellationToken cancellationToken = default);
}