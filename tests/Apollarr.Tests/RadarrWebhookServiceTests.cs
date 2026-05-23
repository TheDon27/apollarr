using Apollarr.Models;
using Apollarr.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Apollarr.Tests;

public class RadarrWebhookServiceTests
{
    private static RadarrWebhookService CreateService(Mock<IRadarrService> radarr, Mock<IStrmFileService> strm) =>
        new(NullLogger<RadarrWebhookService>.Instance, radarr.Object, strm.Object);

    [Fact]
    public async Task NonMovieAddEventIsAcknowledgedWithoutProcessing()
    {
        var radarr = new Mock<IRadarrService>(MockBehavior.Strict);
        var strm = new Mock<IStrmFileService>(MockBehavior.Strict);
        var service = CreateService(radarr, strm);

        var response = await service.HandleWebhookAsync(new RadarrWebhook { EventType = "Download" });

        Assert.Equal("Webhook received", response.Message);
        Assert.Equal("Download", response.EventType);
    }

    [Fact]
    public async Task MovieAddWithNoValidLinkLeavesMovieMonitored()
    {
        var movie = new RadarrMovieDetails { Id = 7, Title = "Example Film", Monitored = false };

        var radarr = new Mock<IRadarrService>();
        radarr.Setup(r => r.GetMovieDetailsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(movie);
        radarr.Setup(r => r.GetQualityProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RadarrQualityProfile> { new() { Id = 3, Name = "SDTV" } });

        var strm = new Mock<IStrmFileService>();
        strm.Setup(s => s.ValidateMovieLinkAsync(It.IsAny<RadarrMovieDetails>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService(radarr, strm);

        var response = await service.HandleWebhookAsync(new RadarrWebhook
        {
            EventType = "movieAdd",
            Movie = new RadarrMovie { Id = 7 }
        });

        Assert.Equal("movieAdd", response.EventType);
        Assert.True(movie.Monitored);
        Assert.Equal(3, movie.QualityProfileId);

        radarr.Verify(r => r.UpdateMovieAsync(movie, It.IsAny<CancellationToken>()), Times.Once);
        radarr.Verify(r => r.RescanMovieAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        strm.Verify(s => s.ProcessMovieAsync(It.IsAny<RadarrMovieDetails>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
