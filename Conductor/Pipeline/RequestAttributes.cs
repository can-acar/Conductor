using Conductor.Core;

namespace Conductor.Pipeline;

// Marker interfaces for pipeline behaviors
public interface ICacheableRequest
{
    string GetCacheKey();
    TimeSpan GetCacheDuration();
}

public interface ITransactionalRequest
{
    bool RequiresTransaction { get; }
}

public interface IAuthorizedRequest
{
    IEnumerable<string> GetRequiredPermissions();
}

public interface IAuditableRequest
{
    string GetAuditDetails();
}

// Attributes for easy configuration
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CacheableAttribute : Attribute
{
    public string? CacheKey { get; set; }
    public int DurationSeconds { get; set; } = 300; // 5 minutes default
    public bool UseRequestData { get; set; } = true;

    public TimeSpan GetDuration() => TimeSpan.FromSeconds(DurationSeconds);
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class TransactionalAttribute : Attribute
{
    public bool RequireTransaction { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AuthorizeAttribute : Attribute
{
    public string[] Permissions { get; set; } = Array.Empty<string>();

    public AuthorizeAttribute(params string[] permissions)
    {
        Permissions = permissions;
    }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AuditableAttribute : Attribute
{
    public bool LogRequestData { get; set; } = false;
    public bool LogResponseData { get; set; } = false;
    public string? Category { get; set; }
}

// Base classes that implement the interfaces
public abstract class CacheableRequest : BaseRequest, ICacheableRequest
{
    public virtual string GetCacheKey()
    {
        var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
            .FirstOrDefault() as CacheableAttribute;

        if (!string.IsNullOrEmpty(attribute?.CacheKey))
        {
            return attribute.CacheKey;
        }

        if (attribute?.UseRequestData == true)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(this);
            var hash = json.GetHashCode();
            return $"{GetType().Name}_{hash:X}";
        }

        return $"{GetType().Name}_{UserId}";
    }

    public virtual TimeSpan GetCacheDuration()
    {
        var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
            .FirstOrDefault() as CacheableAttribute;

        return attribute?.GetDuration() ?? TimeSpan.FromMinutes(5);
    }
}

public abstract class TransactionalRequest : BaseRequest, ITransactionalRequest
{
    public virtual bool RequiresTransaction
    {
        get
        {
            var attribute = GetType().GetCustomAttributes(typeof(TransactionalAttribute), true)
                .FirstOrDefault() as TransactionalAttribute;

            return attribute?.RequireTransaction ?? true;
        }
    }
}

public abstract class AuthorizedRequest : BaseRequest, IAuthorizedRequest
{
    public virtual IEnumerable<string> GetRequiredPermissions()
    {
        var attribute = GetType().GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .FirstOrDefault() as AuthorizeAttribute;

        return attribute?.Permissions ?? Enumerable.Empty<string>();
    }
}

public abstract class AuditableRequest : BaseRequest, IAuditableRequest
{
    public virtual string GetAuditDetails()
    {
        var attribute = GetType().GetCustomAttributes(typeof(AuditableAttribute), true)
            .FirstOrDefault() as AuditableAttribute;

        if (attribute?.LogRequestData == true)
        {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }

        return $"Request: {GetType().Name}, User: {UserId}";
    }
}

// Combined base classes for common scenarios
public abstract class CacheableTransactionalRequest : BaseRequest, ICacheableRequest, ITransactionalRequest
{
    public virtual string GetCacheKey()
    {
        var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
            .FirstOrDefault() as CacheableAttribute;

        if (!string.IsNullOrEmpty(attribute?.CacheKey))
        {
            return attribute.CacheKey;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(this);
        var hash = json.GetHashCode();
        return $"{GetType().Name}_{hash:X}";
    }

    public virtual TimeSpan GetCacheDuration()
    {
        var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
            .FirstOrDefault() as CacheableAttribute;

        return attribute?.GetDuration() ?? TimeSpan.FromMinutes(5);
    }

    public virtual bool RequiresTransaction
    {
        get
        {
            var attribute = GetType().GetCustomAttributes(typeof(TransactionalAttribute), true)
                .FirstOrDefault() as TransactionalAttribute;

            return attribute?.RequireTransaction ?? true;
        }
    }
}

public abstract class AuthorizedAuditableRequest : BaseRequest, IAuthorizedRequest, IAuditableRequest
{
    public virtual IEnumerable<string> GetRequiredPermissions()
    {
        var attribute = GetType().GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .FirstOrDefault() as AuthorizeAttribute;

        return attribute?.Permissions ?? Enumerable.Empty<string>();
    }

    public virtual string GetAuditDetails()
    {
        var attribute = GetType().GetCustomAttributes(typeof(AuditableAttribute), true)
            .FirstOrDefault() as AuditableAttribute;

        if (attribute?.LogRequestData == true)
        {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }

        return $"Request: {GetType().Name}, User: {UserId}";
    }
}

// Ultimate base class with all pipeline features
public abstract class FullPipelineRequest : BaseRequest, ICacheableRequest, ITransactionalRequest, IAuthorizedRequest, IAuditableRequest
{
    public virtual string GetCacheKey()
    {
        var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
            .FirstOrDefault() as CacheableAttribute;

        if (!string.IsNullOrEmpty(attribute?.CacheKey))
        {
            return attribute.CacheKey;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(this);
        var hash = json.GetHashCode();
        return $"{GetType().Name}_{hash:X}";
    }

    public virtual TimeSpan GetCacheDuration()
    {
        var attribute = GetType().GetCustomAttributes(typeof(CacheableAttribute), true)
            .FirstOrDefault() as CacheableAttribute;

        return attribute?.GetDuration() ?? TimeSpan.FromMinutes(5);
    }

    public virtual bool RequiresTransaction
    {
        get
        {
            var attribute = GetType().GetCustomAttributes(typeof(TransactionalAttribute), true)
                .FirstOrDefault() as TransactionalAttribute;

            return attribute?.RequireTransaction ?? true;
        }
    }

    public virtual IEnumerable<string> GetRequiredPermissions()
    {
        var attribute = GetType().GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .FirstOrDefault() as AuthorizeAttribute;

        return attribute?.Permissions ?? Enumerable.Empty<string>();
    }

    public virtual string GetAuditDetails()
    {
        var attribute = GetType().GetCustomAttributes(typeof(AuditableAttribute), true)
            .FirstOrDefault() as AuditableAttribute;

        if (attribute?.LogRequestData == true)
        {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }

        return $"Request: {GetType().Name}, User: {UserId}";
    }
}