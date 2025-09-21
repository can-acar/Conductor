using Conductor.Attributes;

namespace Conductor.Validation;

public interface IPropertyValidator<T, TProperty>
{
    Task<bool> IsValidAsync(T instance, TProperty value, CancellationToken cancellationToken = default);
    string GetDefaultMessage();
    string GetDefaultErrorCode();
}

public abstract class BasePropertyValidator<T, TProperty> : IPropertyValidator<T, TProperty>
{
    public virtual Task<bool> IsValidAsync(T instance, TProperty value, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(IsValid(instance, value));
    }

    protected abstract bool IsValid(T instance, TProperty value);
    public abstract string GetDefaultMessage();
    public abstract string GetDefaultErrorCode();
}

public class NotNullValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    protected override bool IsValid(T instance, TProperty value)
    {
        return value != null;
    }

    public override string GetDefaultMessage() => "'{PropertyName}' must not be null.";
    public override string GetDefaultErrorCode() => "NOT_NULL";
}

public class NotEmptyValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    protected override bool IsValid(T instance, TProperty value)
    {
        if (value == null) return false;

        return value switch
        {
            string str => !string.IsNullOrWhiteSpace(str),
            System.Collections.ICollection collection => collection.Count > 0,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Any(),
            _ => !value.Equals(default(TProperty))
        };
    }

    public override string GetDefaultMessage() => "'{PropertyName}' must not be empty.";
    public override string GetDefaultErrorCode() => "NOT_EMPTY";
}

public class MinimumLengthValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly int _minLength;

    public MinimumLengthValidator(int minLength)
    {
        _minLength = minLength;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        if (value == null) return true; // Let NotNull handle null values

        return value switch
        {
            string str => str.Length >= _minLength,
            System.Collections.ICollection collection => collection.Count >= _minLength,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Count() >= _minLength,
            _ => true
        };
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must be at least {_minLength} characters long.";
    public override string GetDefaultErrorCode() => "MIN_LENGTH";
}

public class MaximumLengthValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly int _maxLength;

    public MaximumLengthValidator(int maxLength)
    {
        _maxLength = maxLength;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        if (value == null) return true; // Let NotNull handle null values

        return value switch
        {
            string str => str.Length <= _maxLength,
            System.Collections.ICollection collection => collection.Count <= _maxLength,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Count() <= _maxLength,
            _ => true
        };
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must be {_maxLength} characters or fewer.";
    public override string GetDefaultErrorCode() => "MAX_LENGTH";
}

public class ExactLengthValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly int _length;

    public ExactLengthValidator(int length)
    {
        _length = length;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        if (value == null) return true; // Let NotNull handle null values

        return value switch
        {
            string str => str.Length == _length,
            System.Collections.ICollection collection => collection.Count == _length,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Count() == _length,
            _ => true
        };
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must be exactly {_length} characters long.";
    public override string GetDefaultErrorCode() => "EXACT_LENGTH";
}

public class LengthValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly int _min;
    private readonly int _max;

    public LengthValidator(int min, int max)
    {
        _min = min;
        _max = max;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        if (value == null) return true; // Let NotNull handle null values

        var length = value switch
        {
            string str => str.Length,
            System.Collections.ICollection collection => collection.Count,
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Count(),
            _ => 0
        };

        return length >= _min && length <= _max;
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must be between {_min} and {_max} characters long.";
    public override string GetDefaultErrorCode() => "LENGTH";
}

public class GreaterThanValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly TProperty _valueToCompare;

    public GreaterThanValidator(TProperty valueToCompare)
    {
        _valueToCompare = valueToCompare;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        if (value == null) return true; // Let NotNull handle null values

        if (value is IComparable<TProperty> comparable)
        {
            return comparable.CompareTo(_valueToCompare) > 0;
        }

        if (value is IComparable objComparable && _valueToCompare is IComparable)
        {
            return objComparable.CompareTo(_valueToCompare) > 0;
        }

        return false;
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must be greater than '{_valueToCompare}'.";
    public override string GetDefaultErrorCode() => "GREATER_THAN";
}

public class GreaterThanOrEqualValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly TProperty _valueToCompare;

    public GreaterThanOrEqualValidator(TProperty valueToCompare)
    {
        _valueToCompare = valueToCompare;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        if (value == null) return true; // Let NotNull handle null values

        if (value is IComparable<TProperty> comparable)
        {
            return comparable.CompareTo(_valueToCompare) >= 0;
        }

        if (value is IComparable objComparable && _valueToCompare is IComparable)
        {
            return objComparable.CompareTo(_valueToCompare) >= 0;
        }

        return false;
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must be greater than or equal to '{_valueToCompare}'.";
    public override string GetDefaultErrorCode() => "GREATER_THAN_OR_EQUAL";
}

public class LessThanValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly TProperty _valueToCompare;

    public LessThanValidator(TProperty valueToCompare)
    {
        _valueToCompare = valueToCompare;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        if (value == null) return true; // Let NotNull handle null values

        if (value is IComparable<TProperty> comparable)
        {
            return comparable.CompareTo(_valueToCompare) < 0;
        }

        if (value is IComparable objComparable && _valueToCompare is IComparable)
        {
            return objComparable.CompareTo(_valueToCompare) < 0;
        }

        return false;
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must be less than '{_valueToCompare}'.";
    public override string GetDefaultErrorCode() => "LESS_THAN";
}

public class LessThanOrEqualValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly TProperty _valueToCompare;

    public LessThanOrEqualValidator(TProperty valueToCompare)
    {
        _valueToCompare = valueToCompare;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        if (value == null) return true; // Let NotNull handle null values

        if (value is IComparable<TProperty> comparable)
        {
            return comparable.CompareTo(_valueToCompare) <= 0;
        }

        if (value is IComparable objComparable && _valueToCompare is IComparable)
        {
            return objComparable.CompareTo(_valueToCompare) <= 0;
        }

        return false;
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must be less than or equal to '{_valueToCompare}'.";
    public override string GetDefaultErrorCode() => "LESS_THAN_OR_EQUAL";
}

public class EqualValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly TProperty _valueToCompare;

    public EqualValidator(TProperty valueToCompare)
    {
        _valueToCompare = valueToCompare;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        return EqualityComparer<TProperty>.Default.Equals(value, _valueToCompare);
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must be equal to '{_valueToCompare}'.";
    public override string GetDefaultErrorCode() => "EQUAL";
}

public class NotEqualValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly TProperty _valueToCompare;

    public NotEqualValidator(TProperty valueToCompare)
    {
        _valueToCompare = valueToCompare;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        return !EqualityComparer<TProperty>.Default.Equals(value, _valueToCompare);
    }

    public override string GetDefaultMessage() => $"'{{PropertyName}}' must not be equal to '{_valueToCompare}'.";
    public override string GetDefaultErrorCode() => "NOT_EQUAL";
}

public class PredicateValidator<T, TProperty> : BasePropertyValidator<T, TProperty>
{
    private readonly Func<TProperty, bool>? _propertyPredicate;
    private readonly Func<T, TProperty, bool>? _instancePredicate;

    public PredicateValidator(Func<TProperty, bool> predicate)
    {
        _propertyPredicate = predicate;
    }

    public PredicateValidator(Func<T, TProperty, bool> predicate)
    {
        _instancePredicate = predicate;
    }

    protected override bool IsValid(T instance, TProperty value)
    {
        if (_propertyPredicate != null)
        {
            return _propertyPredicate(value);
        }

        if (_instancePredicate != null)
        {
            return _instancePredicate(instance, value);
        }

        return true;
    }

    public override string GetDefaultMessage() => "'{PropertyName}' failed custom validation.";
    public override string GetDefaultErrorCode() => "CUSTOM_VALIDATION";
}

public class AsyncPredicateValidator<T, TProperty> : IPropertyValidator<T, TProperty>
{
    private readonly Func<TProperty, CancellationToken, Task<bool>>? _propertyPredicate;
    private readonly Func<T, TProperty, CancellationToken, Task<bool>>? _instancePredicate;

    public AsyncPredicateValidator(Func<TProperty, CancellationToken, Task<bool>> predicate)
    {
        _propertyPredicate = predicate;
    }

    public AsyncPredicateValidator(Func<T, TProperty, CancellationToken, Task<bool>> predicate)
    {
        _instancePredicate = predicate;
    }

    public async Task<bool> IsValidAsync(T instance, TProperty value, CancellationToken cancellationToken = default)
    {
        if (_propertyPredicate != null)
        {
            return await _propertyPredicate(value, cancellationToken);
        }

        if (_instancePredicate != null)
        {
            return await _instancePredicate(instance, value, cancellationToken);
        }

        return true;
    }

    public string GetDefaultMessage() => "'{PropertyName}' failed custom async validation.";
    public string GetDefaultErrorCode() => "CUSTOM_ASYNC_VALIDATION";
}