using CRM.Domain.Common;
using FluentValidation;
using MediatR;
using ValidationException = CRM.Application.Common.Exceptions.ValidationException;

namespace CRM.Application.Common.Behaviors;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = Guard.AgainstNull(validators);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any()) return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToArray();

        if (failures.Length == 0) return await next(cancellationToken);

        var errors = failures
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        throw new ValidationException(errors);
    }
}
