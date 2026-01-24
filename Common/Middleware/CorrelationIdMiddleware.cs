using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Apollarr.Common.Middleware;

/// <summary>
/// Adds/propagates a correlation identifier for request tracing.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object?>
               {
                   { "CorrelationId", correlationId }
               }))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var headerValues) &&
            !string.IsNullOrWhiteSpace(headerValues))
        {
            return headerValues.ToString();
        }

        return Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
    }
}
