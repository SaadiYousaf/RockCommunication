namespace CRM.Application.Common.Exceptions;

public class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = errors;
}

public class NotFoundException : Exception
{
    public NotFoundException(string entity, object key)
        : base($"{entity} with key '{key}' was not found.") { }
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
