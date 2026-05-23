using Apollarr.Models;
using Apollarr.Services;

namespace Apollarr.Tests;

public class SanitizeFileNameTests
{
    [Fact]
    public void LeavesACleanNameUnchanged()
    {
        Assert.Equal("Normal Title.strm", StrmFileService.SanitizeFileName("Normal Title.strm"));
    }

    [Theory]
    [InlineData("Title: Subtitle")]
    [InlineData("What?")]
    [InlineData("A|B")]
    [InlineData("Quote\"d")]
    [InlineData("Wild*card")]
    [InlineData("Less<More>")]
    public void StripsCharactersThatAreProblematicInFileNames(string input)
    {
        var result = StrmFileService.SanitizeFileName(input);

        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("?", result);
        Assert.DoesNotContain("|", result);
        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
    }

    [Fact]
    public void CollapsesConsecutiveSpaces()
    {
        Assert.Equal("A B", StrmFileService.SanitizeFileName("A    B"));
    }

    [Fact]
    public void TrimsTrailingPeriodsAndWhitespace()
    {
        Assert.Equal("Title", StrmFileService.SanitizeFileName("  Title.  "));
    }
}

public class GetEpisodeTitleTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TBA")]
    [InlineData("tbd")]
    [InlineData("Episode 5")]
    public void FallsBackToGenericNameForMissingOrPlaceholderTitles(string title)
    {
        var episode = new Episode { EpisodeNumber = 7, Title = title };

        Assert.Equal("Episode 7", StrmFileService.GetEpisodeTitle(episode));
    }

    [Fact]
    public void KeepsARealTitle()
    {
        var episode = new Episode { EpisodeNumber = 7, Title = "The Reckoning" };

        Assert.Equal("The Reckoning", StrmFileService.GetEpisodeTitle(episode));
    }
}
