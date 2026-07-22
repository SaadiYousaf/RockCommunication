using CRM.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CRM.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) => _logger = Guard.AgainstNull(logger);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Handling {RequestName}", name);
        try
        {
            var result = await next(cancellationToken);
            sw.Stop();
            _logger.LogInformation("Handled {RequestName} in {Elapsed}ms", name, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Failed {RequestName} after {Elapsed}ms", name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
