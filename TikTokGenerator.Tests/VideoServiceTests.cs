using TikTokGenerator.Services;

namespace TikTokGenerator.Tests;

public sealed class VideoServiceTests
{
    [Fact]
    public void StreamSignaturesAreCompatible_WhenAllStreamsMatch_AllowsCopyConcat()
    {
        var signature = "video:h264:high:yuv420p:::30/1:1/15360:1080:1920|audio:aac:::48000:stereo::1/48000::";

        Assert.True(VideoService.StreamSignaturesAreCompatible([signature, signature, signature]));
    }

    [Fact]
    public void StreamSignaturesAreCompatible_WhenStreamsDiffer_RequiresReencodeConcat()
    {
        var first = "video:h264:high:yuv420p:::30/1:1/15360:1080:1920|audio:aac:::48000:stereo::1/48000::";
        var second = "video:h264:main:yuv420p:::24/1:1/12288:1080:1920|audio:aac:::44100:stereo::1/44100::";

        Assert.False(VideoService.StreamSignaturesAreCompatible([first, second]));
    }
}
