using System.Linq.Expressions;
using System.Reflection;
using Conductor.Attributes;
using Conductor.Core;
using Conductor.Interfaces;

namespace Conductor.Validation;

// Core validation interface
public interface IValidator<in T>
{
    Task<ValidationResult> ValidateAsync(T instance, CancellationToken cancellationToken = default);
    ValidationResult Validate(T instance);
}

// Fluent Validation Infrastructure
public abstract class AbstractValidator<T> : IValidator<T>
{
    private readonly List<IValidationRule<T>> _rules = [];

    protected IRuleBuilder<T, TProperty> RuleFor<TProperty>(Expression<Func<T, TProperty>> expression)
    {
        return new RuleBuilder<T, TProperty>(expression, _rules);
    }

    public virtual async Task<ValidationResult> ValidateAsync(T instance, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        foreach (var rule in _rules)
        {
            var ruleErrors = await rule.ValidateAsync(instance, cancellationToken);
            errors.AddRange(ruleErrors);
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }

    public ValidationResult Validate(T instance)
    {
        return ValidateAsync(instance).GetAwaiter().GetResult();
    }
}

public interface IRuleBuilder<out T, TProperty>
{
    IRuleBuilder<T, TProperty> NotNull();
    IRuleBuilder<T, TProperty> NotEmpty();
    IRuleBuilder<T, TProperty> MinimumLength(int minLength);
    IRuleBuilder<T, TProperty> MaximumLength(int maxLength);
    IRuleBuilder<T, TProperty> Length(int exactLength);
    IRuleBuilder<T, TProperty> Length(int min, int max);
    IRuleBuilder<T, TProperty> GreaterThan(TProperty valueToCompare);
    IRuleBuilder<T, TProperty> GreaterThanOrEqualTo(TProperty valueToCompare);
    IRuleBuilder<T, TProperty> LessThan(TProperty valueToCompare);
    IRuleBuilder<T, TProperty> LessThanOrEqualTo(TProperty valueToCompare);
    IRuleBuilder<T, TProperty> Equal(TProperty valueToCompare);
    IRuleBuilder<T, TProperty> NotEqual(TProperty valueToCompare);
    IRuleBuilder<T, TProperty> Must(Func<TProperty, bool> predicate);
    IRuleBuilder<T, TProperty> Must(Func<T, TProperty, bool> predicate);
    IRuleBuilder<T, TProperty> MustAsync(Func<TProperty, CancellationToken, Task<bool>> predicate);
    IRuleBuilder<T, TProperty> MustAsync(Func<T, TProperty, CancellationToken, Task<bool>> predicate);
    IRuleBuilder<T, TProperty> WithMessage(string errorMessage);
    IRuleBuilder<T, TProperty> WithErrorCode(string errorCode);
    IRuleBuilder<T, TProperty> When(Func<T, bool> predicate);
    IRuleBuilder<T, TProperty> Unless(Func<T, bool> predicate);
}

public class RuleBuilder<T, TProperty> : IRuleBuilder<T, TProperty>
{
    private readonly Expression<Func<T, TProperty>> _expression;
    private readonly List<IPropertyValidator<T, TProperty>> _validators = [];
    private string? _customMessage;
    private string? _customErrorCode;
    private Func<T, bool>? _condition;
    private bool _unless;

    public RuleBuilder(Expression<Func<T, TProperty>> expression, List<IValidationRule<T>> rules)
    {
        _expression = expression;

        // Add the rule to the collection when created
        rules.Add(new PropertyValidationRule<T, TProperty>(expression, _validators, () => _customMessage, () => _customErrorCode, () => _condition, () => _unless));
    }

    public IRuleBuilder<T, TProperty> NotNull()
    {
        _validators.Add(new NotNullValidator<T, TProperty>());
        return this;
    }

    public IRuleBuilder<T, TProperty> NotEmpty()
    {
        _validators.Add(new NotEmptyValidator<T, TProperty>());
        return this;
    }

    public IRuleBuilder<T, TProperty> MinimumLength(int minLength)
    {
        _validators.Add(new MinimumLengthValidator<T, TProperty>(minLength));
        return this;
    }

    public IRuleBuilder<T, TProperty> MaximumLength(int maxLength)
    {
        _validators.Add(new MaximumLengthValidator<T, TProperty>(maxLength));
        return this;
    }

    public IRuleBuilder<T, TProperty> Length(int exactLength)
    {
        _validators.Add(new ExactLengthValidator<T, TProperty>(exactLength));
        return this;
    }

    public IRuleBuilder<T, TProperty> Length(int min, int max)
    {
        _validators.Add(new LengthValidator<T, TProperty>(min, max));
        return this;
    }

    public IRuleBuilder<T, TProperty> GreaterThan(TProperty valueToCompare)
    {
        _validators.Add(new GreaterThanValidator<T, TProperty>(valueToCompare));
        return this;
    }

    public IRuleBuilder<T, TProperty> GreaterThanOrEqualTo(TProperty valueToCompare)
    {
        _validators.Add(new GreaterThanOrEqualValidator<T, TProperty>(valueToCompare));
        return this;
    }

    public IRuleBuilder<T, TProperty> LessThan(TProperty valueToCompare)
    {
        _validators.Add(new LessThanValidator<T, TProperty>(valueToCompare));
        return this;
    }

    public IRuleBuilder<T, TProperty> LessThanOrEqualTo(TProperty valueToCompare)
    {
        _validators.Add(new LessThanOrEqualValidator<T, TProperty>(valueToCompare));
        return this;
    }

    public IRuleBuilder<T, TProperty> Equal(TProperty valueToCompare)
    {
        _validators.Add(new EqualValidator<T, TProperty>(valueToCompare));
        return this;
    }

    public IRuleBuilder<T, TProperty> NotEqual(TProperty valueToCompare)
    {
        _validators.Add(new NotEqualValidator<T, TProperty>(valueToCompare));
        return this;
    }

    public IRuleBuilder<T, TProperty> Must(Func<TProperty, bool> predicate)
    {
        _validators.Add(new PredicateValidator<T, TProperty>(predicate));
        return this;
    }

    public IRuleBuilder<T, TProperty> Must(Func<T, TProperty, bool> predicate)
    {
        _validators.Add(new PredicateValidator<T, TProperty>(predicate));
        return this;
    }

    public IRuleBuilder<T, TProperty> MustAsync(Func<TProperty, CancellationToken, Task<bool>> predicate)
    {
        _validators.Add(new AsyncPredicateValidator<T, TProperty>(predicate));
        return this;
    }

    public IRuleBuilder<T, TProperty> MustAsync(Func<T, TProperty, CancellationToken, Task<bool>> predicate)
    {
        _validators.Add(new AsyncPredicateValidator<T, TProperty>(predicate));
        return this;
    }

    public IRuleBuilder<T, TProperty> WithMessage(string errorMessage)
    {
        _customMessage = errorMessage;
        return this;
    }

    public IRuleBuilder<T, TProperty> WithErrorCode(string errorCode)
    {
        _customErrorCode = errorCode;
        return this;
    }

    public IRuleBuilder<T, TProperty> When(Func<T, bool> predicate)
    {
        _condition = predicate;
        _unless = false;
        return this;
    }

    public IRuleBuilder<T, TProperty> Unless(Func<T, bool> predicate)
    {
        _condition = predicate;
        _unless = true;
        return this;
    }
}

public class PropertyValidationRule<T, TProperty> : IValidationRule<T>
{
    private readonly Expression<Func<T, TProperty>> _expression;
    private readonly List<IPropertyValidator<T, TProperty>> _validators;
    private readonly Func<string?> _messageProvider;
    private readonly Func<string?> _errorCodeProvider;
    private readonly Func<Func<T, bool>?> _conditionProvider;
    private readonly Func<bool> _unlessProvider;
    private readonly Func<T, TProperty> _propertyFunc;
    private readonly string _propertyName;

    public PropertyValidationRule(
        Expression<Func<T, TProperty>> expression,
        List<IPropertyValidator<T, TProperty>> validators,
        Func<string?> messageProvider,
        Func<string?> errorCodeProvider,
        Func<Func<T, bool>?> conditionProvider,
        Func<bool> unlessProvider)
    {
        _expression = expression;
        _validators = validators;
        _messageProvider = messageProvider;
        _errorCodeProvider = errorCodeProvider;
        _conditionProvider = conditionProvider;
        _unlessProvider = unlessProvider;
        _propertyFunc = expression.Compile();
        _propertyName = GetPropertyName(expression);
    }

    public async Task<IEnumerable<ValidationError>> ValidateAsync(T instance, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        // Check condition
        var condition = _conditionProvider();
        if (condition != null)
        {
            var conditionResult = condition(instance);
            if (_unlessProvider() ? conditionResult : !conditionResult)
            {
                return errors; // Skip validation
            }
        }

        var propertyValue = _propertyFunc(instance);

        foreach (var validator in _validators)
        {
            var isValid = await validator.IsValidAsync(instance, propertyValue, cancellationToken);
            if (!isValid)
            {
                var errorMessage = _messageProvider() ?? validator.GetDefaultMessage();
                var errorCode = _errorCodeProvider() ?? validator.GetDefaultErrorCode();

                // Replace placeholder with actual property name
                errorMessage = errorMessage.Replace("{PropertyName}", _propertyName);

                errors.Add(new ValidationError(_propertyName, errorMessage, errorCode)
                {
                    AttemptedValue = propertyValue
                });
            }
        }

        return errors;
    }

    private static string GetPropertyName(Expression<Func<T, TProperty>> expression)
    {
        if (expression.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }

        throw new ArgumentException("Expression must be a property accessor", nameof(expression));
    }
}