using Api.Controllers;
using Xunit;

namespace Api.Tests;

public class DownloadDedupTests
{
    [Fact]
    public void NormalizeUrl_IgnoresTrackingParams_SamePlaylistIsEqual()
    {
        var a = DownloadController.NormalizeUrl("https://www.youtube.com/playlist?list=PL123&si=abc123");
        var b = DownloadController.NormalizeUrl("https://www.youtube.com/playlist?list=PL123");

        Assert.Equal(a, b);
    }

    [Fact]
    public void NormalizeUrl_StripsUtmAndFragment()
    {
        var a = DownloadController.NormalizeUrl("https://open.spotify.com/playlist/4x7y?si=zz&utm_source=copy");
        var b = DownloadController.NormalizeUrl("https://open.spotify.com/playlist/4x7y#fragment");

        Assert.Equal(a, b);
    }

    [Fact]
    public void NormalizeUrl_DifferentPlaylistsAreDistinct()
    {
        var a = DownloadController.NormalizeUrl("https://www.youtube.com/playlist?list=PL111");
        var b = DownloadController.NormalizeUrl("https://www.youtube.com/playlist?list=PL222");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NormalizeUrl_KeepsIndexParam_WhenPresent()
    {
        var a = DownloadController.NormalizeUrl("https://www.youtube.com/watch?v=abc&list=PL123&index=2");
        var b = DownloadController.NormalizeUrl("https://www.youtube.com/watch?v=abc&list=PL123&index=3");

        Assert.NotEqual(a, b);
    }
}
