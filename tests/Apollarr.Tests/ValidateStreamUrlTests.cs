using System.Net;
using Apollarr.Common;
using Apollarr.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Apollarr.Tests;

public class ValidateStreamUrlTests
{
    private static StrmFileService CreateService(FakeHttpMessageHandler handler)
    {
        var settings = new AppSettings
        {
            Apollo = new ApolloSettings { Username = "user", Password = "pass" },
            Strm = new StrmSettings { ValidationTimeoutSeconds = 5 }
        };

        return new StrmFileService(
            NullLogger<StrmFileService>.Instance,
            new HttpClient(handler),
            Mock.Of<IFileSystemService>(),
            Mock.Of<ISonarrService>(),
            Options.Create(settings),
            radarrService: null);
    }

    [Fact]
    public async Task ReturnsTrueOnSuccessStatus()
    {
        var service = CreateService(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        Assert.True(await service.ValidateStreamUrlAsync("https://starlite.best/api/stream/x"));
    }

    [Fact]
    public async Task ReturnsFalseOnNonSuccessStatus()
    {
        var service = CreateService(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.False(await service.ValidateStreamUrlAsync("https://starlite.best/api/stream/x"));
    }

    [Fact]
    public async Task ReturnsFalseWhenRedirectedToErrorPage()
    {
        var service = CreateService(new FakeHttpMessageHandler(_ =>
        {
            // Simulate the client having followed a redirect to the provider's error page.
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Head, "https://error.starlite.best/not-found")
            };
            return response;
        }));

        Assert.False(await service.ValidateStreamUrlAsync("https://starlite.best/api/stream/x"));
    }

    [Fact]
    public async Task ReturnsFalseWhenValidationTimesOut()
    {
        // An OperationCanceledException not tied to the caller's token represents the validation timeout.
        var service = CreateService(new FakeHttpMessageHandler(_ => throw new OperationCanceledException()));

        Assert.False(await service.ValidateStreamUrlAsync("https://starlite.best/api/stream/x"));
    }
}
