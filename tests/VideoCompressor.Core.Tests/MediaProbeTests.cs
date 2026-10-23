using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.Media;
using VideoCompressor.Core.Tests.Infrastructure;

namespace VideoCompressor.Core.Tests;

[Collection(FfmpegCollection.Name)]
public class MediaProbeTests(FfmpegFixture fixture)
{
    [Fact]
    public async Task Probe_ReadsGeneratedClip()
    {
        var probe = new MediaProbe(fixture.RequireTools());

        var info = await probe.ProbeAsync(fixture.ShortClip, TestContext.Current.CancellationToken);

        Assert.Equal(2.0, info.Duration.TotalSeconds, tolerance: 0.1);
        Assert.Equal(320, info.Width);
        Assert.Equal(240, info.Height);
        Assert.Equal(30.0, info.Fps, tolerance: 0.01);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal(2, info.AudioTrackCount);
        Assert.False(info.IsHdr);
        Assert.Equal(new FileInfo(fixture.ShortClip).Length, info.SizeBytes);
    }

    [Fact]
    public async Task Probe_TextFileWithVideoExtension_ThrowsFriendlyError()
    {
        var probe = new MediaProbe(fixture.RequireTools());
        var fake = Path.Combine(fixture.Directory, $"не видео {Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(fake, "это просто текст", TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<FfmpegException>(
            () => probe.ProbeAsync(fake, TestContext.Current.CancellationToken));
        Assert.Equal(MediaProbe.NotAVideoMessage, ex.Message);
    }

    [Fact]
    public void Parse_SkipsCoverArtAndDetectsHdr()
    {
        const string json = """
        {
          "streams": [
            { "codec_type": "video", "codec_name": "mjpeg", "width": 600, "height": 600,
              "disposition": { "attached_pic": 1 } },
            { "codec_type": "video", "codec_name": "hevc", "width": 3840, "height": 2160,
              "avg_frame_rate": "0/0", "r_frame_rate": "60000/1001", "color_transfer": "smpte2084",
              "disposition": { "attached_pic": 0 } },
            { "codec_type": "audio" }
          ],
          "format": { "duration": "12.5" }
        }
        """;

        var info = MediaProbe.Parse(json, fileSize: 42);

        Assert.Equal("hevc", info.VideoCodec);
        Assert.Equal(3840, info.Width);
        Assert.Equal(59.94, info.Fps, tolerance: 0.01);
        Assert.True(info.IsHdr);
        Assert.Equal(1, info.AudioTrackCount);
        Assert.Equal(TimeSpan.FromSeconds(12.5), info.Duration);
    }

    [Fact]
    public void Parse_AudioOnly_Throws()
    {
        const string json = """{ "streams": [ { "codec_type": "audio" } ], "format": { "duration": "3" } }""";
        Assert.Throws<FfmpegException>(() => MediaProbe.Parse(json, 1));
    }
}
