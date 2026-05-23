namespace Apollarr.Tests;

// Lets tests drive HttpClient responses (or exceptions) without real network calls.
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = _responder(request);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }
}
