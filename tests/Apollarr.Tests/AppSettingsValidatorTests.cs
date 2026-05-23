using Apollarr.Common;

namespace Apollarr.Tests;

public class AppSettingsValidatorTests
{
    private static AppSettings ValidSettings() => new()
    {
        Sonarr = new SonarrSettings { Url = "http://localhost:8989", ApiKey = "sonarr-key" },
        Radarr = new RadarrSettings { Url = "http://localhost:7878", ApiKey = "radarr-key" },
        Apollo = new ApolloSettings { Username = "user", Password = "pass" },
        Strm = new StrmSettings { StreamUrlTemplate = "https://example/{username}", ValidationTimeoutSeconds = 10 },
        Scheduling = new SchedulingSettings()
    };

    [Fact]
    public void SucceedsWhenAllRequiredSettingsArePresent()
    {
        var result = new AppSettingsValidator().Validate(null, ValidSettings());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void FailsWhenNestedRequiredValueIsMissing()
    {
        var settings = ValidSettings();
        settings.Sonarr.ApiKey = string.Empty;

        var result = new AppSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Sonarr.ApiKey", result.FailureMessage);
    }

    [Fact]
    public void FailsWhenUrlIsMalformed()
    {
        var settings = ValidSettings();
        settings.Radarr.Url = "not-a-url";

        var result = new AppSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Radarr.Url", result.FailureMessage);
    }

    [Fact]
    public void FailsWhenValidationTimeoutOutOfRange()
    {
        var settings = ValidSettings();
        settings.Strm.ValidationTimeoutSeconds = 0;

        var result = new AppSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Strm.ValidationTimeoutSeconds", result.FailureMessage);
    }
}
