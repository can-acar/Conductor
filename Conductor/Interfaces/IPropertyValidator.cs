namespace Conductor.Interfaces;

public interface IPropertyValidator<in T, in TProperty>
{
    Task<bool> IsValidAsync(T instance, TProperty value, CancellationToken cancellationToken = default);
    string GetDefaultMessage();
    string GetDefaultErrorCode();
}