using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Apollarr.Common.Middleware;

/// <summary>
/// Centralizes exception handling and returns ProblemDetails with correlation id.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly ProblemDetailsFactory _problemDetailsFactory;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        ProblemDetailsFactory problemDetailsFactory)
    {
        _next = next;
        _logger = logger;
        _problemDetailsFactory = problemDetailsFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (context.Response.HasStarted == false)
        {
            const int ClientClosedStatusCode = 499;
            var statusCode = ex switch
            {
                ArgumentException or InvalidOperationException => StatusCodes.Status400BadRequest,
                OperationCanceledException => ClientClosedStatusCode,
                _ => StatusCodes.Status500InternalServerError
            };

            var problemDetails = _problemDetailsFactory.CreateProblemDetails(
                context,
                statusCode: statusCode,
                title: statusCode >= 500 ? "An unexpected error occurred" : "Request failed",
                detail: ex.Message);

            problemDetails.Extensions["correlationId"] = context.TraceIdentifier;

            LogException(ex, statusCode, context.TraceIdentifier);

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problemDetails);
        }
    }

    private void LogException(Exception exception, int statusCode, string correlationId)
    {
        if (statusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled exception ({CorrelationId})", correlationId);
        }
        else
        {
            _logger.LogWarning(exception, "Request failed ({CorrelationId})", correlationId);
        }
    }
}
