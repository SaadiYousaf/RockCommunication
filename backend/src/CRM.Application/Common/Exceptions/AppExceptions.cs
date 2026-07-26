namespace CRM.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = errors;
}

public class NotFoundException : Exception
{
    /// <summary>The entity type (e.g. "Sale") — safe to show a user. The key is NOT, so it's
    /// kept only in <see cref="Exception.Message"/> for server logs and never surfaced to clients.</summary>
    public string Entity { get; }

    public NotFoundException(string entity, object key)
        : base($"{entity} with key '{key}' was not found.") => Entity = entity;
}

public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message = "Access denied.") : base(message) { }
}

public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public class TooManyRequestsException : Exception
{
    public TimeSpan? RetryAfter { get; }
    public TooManyRequestsException(string message, TimeSpan? retryAfter = null) : base(message)
        => RetryAfter = retryAfter;
}
