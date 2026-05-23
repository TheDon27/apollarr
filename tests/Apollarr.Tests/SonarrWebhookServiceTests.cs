using Apollarr.Models;
using Apollarr.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Apollarr.Tests;

public class SonarrWebhookServiceTests
{
    private static SonarrWebhookService CreateService(Mock<ISonarrService> sonarr, Mock<IStrmFileService> strm) =>
        new(NullLogger<SonarrWebhookService>.Instance, sonarr.Object, strm.Object);

    [Fact]
    public async Task NonSeriesAddEventIsAcknowledgedWithoutProcessing()
    {
        var sonarr = new Mock<ISonarrService>(MockBehavior.Strict);
        var strm = new Mock<IStrmFileService>(MockBehavior.Strict);
        var service = CreateService(sonarr, strm);

        var response = await service.HandleWebhookAsync(new SonarrWebhook { EventType = "Download" });

        Assert.Equal("Webhook received", response.Message);
        Assert.Equal("Download", response.EventType);
        // Strict mocks throw on any unexpected call, proving no Sonarr/strm work was done.
    }

    [Fact]
    public async Task SeriesAddMonitorsValidatesAndRescans()
    {
        var series = new SonarrSeriesDetails
        {
            Id = 5,
            Title = "Example Show",
            Seasons = new List<SeasonDetails>
            {
                new() { SeasonNumber = 0, Monitored = true },
                new() { SeasonNumber = 1, Monitored = false }
            }
        };

        var episodes = new List<Episode>
        {
            new() { Id = 100, SeasonNumber = 1, EpisodeNumber = 1, Monitored = false }
        };

        var sonarr = new Mock<ISonarrService>();
        sonarr.Setup(s => s.GetSeriesDetailsAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(series);
        sonarr.Setup(s => s.GetEpisodesForSeriesAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(episodes);
        sonarr.Setup(s => s.UpdateEpisodeAsync(It.IsAny<Episode>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var strm = new Mock<IStrmFileService>();
        strm.Setup(s => s.ProcessSeriesEpisodesAsync(It.IsAny<SonarrSeriesDetails>(), It.IsAny<List<Episode>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesValidationResult(1, 1, 0, 1));

        var service = CreateService(sonarr, strm);

        var response = await service.HandleWebhookAsync(new SonarrWebhook
        {
            EventType = "seriesAdd",
            Series = new SonarrSeries { Id = 5 }
        });

        Assert.Equal("seriesAdd", response.EventType);
        Assert.Equal("Example Show", response.SeriesTitle);

        // Series is monitored, specials (season 0) are left off, regular seasons turned on.
        Assert.True(series.Monitored);
        Assert.False(series.Seasons.Single(s => s.SeasonNumber == 0).Monitored);
        Assert.True(series.Seasons.Single(s => s.SeasonNumber == 1).Monitored);

        sonarr.Verify(s => s.UpdateSeriesAsync(series, It.IsAny<CancellationToken>()), Times.Once);
        sonarr.Verify(s => s.RescanSeriesAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        strm.Verify(s => s.ProcessSeriesEpisodesAsync(series, episodes, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeriesAddWithMissingSeriesDataThrows()
    {
        var sonarr = new Mock<ISonarrService>();
        var strm = new Mock<IStrmFileService>();
        var service = CreateService(sonarr, strm);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.HandleWebhookAsync(new SonarrWebhook { EventType = "seriesAdd", Series = null }));
    }
}
