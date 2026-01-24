using System.Net;
using System.Net.Sockets;

namespace Apollarr.Common;

public static class RetryPolicy
{
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        ILogger logger,
        string operationName,
        int maxRetries = 3,
        int[]? customRetryDelays = null,
        CancellationToken cancellationToken = default)
    {
        var retryDelays = customRetryDelays ?? new[] { 2000, 3000, 5000 };
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                logger.LogDebug("Executing {OperationName} (Attempt {Attempt}/{MaxAttempts})",
                    operationName, attempt + 1, maxRetries + 1);

                return await operation();
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                lastException = ex;
                var delay = retryDelays[Math.Min(attempt, retryDelays.Length - 1)];
                logger.LogWarning(ex, "HTTP error during {OperationName}, waiting {Delay}ms before retry {Attempt}/{MaxRetries}",
                    operationName, delay, attempt + 1, maxRetries);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during {OperationName} after {Attempts} attempts",
                    operationName, attempt + 1);
                throw;
            }
        }

        logger.LogError("Failed {OperationName} after {MaxRetries} retries",
            operationName, maxRetries + 1);
        throw new HttpRequestException($"Failed {operationName} after {maxRetries + 1} attempts", lastException);
    }

    public static async Task<HttpResponseMessage> ExecuteHttpRequestWithRetryAsync(
        Func<Task<HttpResponseMessage>> httpRequest,
        ILogger logger,
        string operationName,
        int maxRetries = 3,
        int[]? customRetryDelays = null,
        HttpStatusCode[]? retryOnStatusCodes = null,
        CancellationToken cancellationToken = default)
    {
        var retryDelays = customRetryDelays ?? new[] { 2000, 3000, 5000 };
        var retryStatusCodes = retryOnStatusCodes ?? new[] { HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable };
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                logger.LogDebug("Executing HTTP {OperationName} (Attempt {Attempt}/{MaxAttempts})",
                    operationName, attempt + 1, maxRetries + 1);

                var response = await httpRequest();

                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                // Check if we should retry on this status code
                if (retryStatusCodes.Contains(response.StatusCode) && attempt < maxRetries)
                {
                    var delay = retryDelays[Math.Min(attempt, retryDelays.Length - 1)];
                    logger.LogWarning("HTTP {OperationName} returned {StatusCode}, waiting {Delay}ms before retry {Attempt}/{MaxRetries}",
                        operationName, response.StatusCode, delay, attempt + 1, maxRetries);
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                // For other errors or last attempt, ensure success or throw
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries && IsRetryableException(ex))
            {
                lastException = ex;
                var delay = retryDelays[Math.Min(attempt, retryDelays.Length - 1)];
                logger.LogWarning(ex, "HTTP error during {OperationName}, waiting {Delay}ms before retry {Attempt}/{MaxRetries}",
                    operationName, delay, attempt + 1, maxRetries);
                await Task.Delay(delay, cancellationToken);
            }
            catch (SocketException ex) when (attempt < maxRetries && IsRetryableSocketException(ex))
            {
                lastException = ex;
                var delay = retryDelays[Math.Min(attempt, retryDelays.Length - 1)];
                logger.LogWarning(ex, "Socket error during {OperationName}, waiting {Delay}ms before retry {Attempt}/{MaxRetries}",
                    operationName, delay, attempt + 1, maxRetries);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during {OperationName} after {Attempts} attempts",
                    operationName, attempt + 1);
                throw;
            }
        }

        logger.LogError("Failed HTTP {OperationName} after {MaxRetries} retries",
            operationName, maxRetries + 1);
        throw new HttpRequestException($"Failed {operationName} after {maxRetries + 1} attempts", lastException);
    }

    private static bool IsRetryableException(HttpRequestException ex)
    {
        // Check if the inner exception is a retryable socket exception
        if (ex.InnerException is SocketException socketEx)
        {
            return IsRetryableSocketException(socketEx);
        }
        
        // Retry on all HttpRequestExceptions (network issues, timeouts, etc.)
        return true;
    }

    private static bool IsRetryableSocketException(SocketException ex)
    {
        // SocketException error codes that are typically retryable:
        // 11 = EAGAIN/EWOULDBLOCK (Resource temporarily unavailable)
        // 10051 = Network is unreachable
        // 10054 = Connection reset by peer
        // 10060 = Connection timed out
        // 10061 = Connection refused
        return ex.SocketErrorCode is
            SocketError.TimedOut or
            SocketError.NetworkUnreachable or
            SocketError.ConnectionReset or
            SocketError.ConnectionRefused or
            SocketError.TryAgain or
            SocketError.WouldBlock;
    }
}
