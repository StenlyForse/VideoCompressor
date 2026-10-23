using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Media;

namespace VideoCompressor.Core.Tests;

public class TargetSizePlannerTests
{
    private const long MiB = 1024 * 1024;

    // The reference recording (spec 001 §4).
    private static readonly VideoInfo Reference = new(
        TimeSpan.FromSeconds(65.617), 199_792_913, 2560, 1440, 59.9993, "h264", 1, IsHdr: false);

    private static CompressionSettings Target(long mib, OutputFormat format = OutputFormat.Efficient) =>
        CompressionSettings.Default with { Format = format, TargetSizeBytes = mib * MiB };

    [Theory]
    [InlineData(25, 2940)]
    [InlineData(10, 1080)]
    [InlineData(50, 6040)]
    public void VideoBitrate_MatchesResearch(long mib, int expectedKbps) =>
        Assert.Equal(expectedKbps, TargetSizePlanner.Plan(Reference, Target(mib)).VideoBitrateKbps, tolerance: 1);

    [Fact]
    public void AudioBudget_CountsEveryTrack()
    {
        var twoTracks = Reference with { AudioTrackCount = 2 };
        var one = TargetSizePlanner.Plan(Reference, Target(25)).VideoBitrateKbps;
        var two = TargetSizePlanner.Plan(twoTracks, Target(25)).VideoBitrateKbps;
        Assert.Equal(160, one - two, tolerance: 1);
    }

    [Fact]
    public void EnoughSize_IsOk()
    {
        var plan = TargetSizePlanner.Plan(Reference, Target(25));
        Assert.Equal(TargetVerdict.Ok, plan.Verdict);
        Assert.Equal(0.0133, plan.BitsPerPixel, tolerance: 0.0005);
        Assert.Null(plan.Suggestion);
    }

    [Fact]
    public void SourceNotLargerThanTarget_IsAlreadySmaller()
    {
        var plan = TargetSizePlanner.Plan(Reference, Target(200));
        Assert.Equal(TargetVerdict.AlreadySmaller, plan.Verdict);
    }

    [Fact]
    public void AudioAloneExceedsBudget_IsImpossible()
    {
        // 1 MB for over a minute: 160 kbit/s of audio alone needs ~1.3 MB.
        var plan = TargetSizePlanner.Plan(Reference, Target(1));
        Assert.Equal(TargetVerdict.Impossible, plan.Verdict);
    }

    [Fact]
    public void TooSmallForH265_SuggestsHalvingFpsFirst()
    {
        // research §3: 10 MB at 1440p60 is visibly bad; 1440p30 is already acceptable.
        var plan = TargetSizePlanner.Plan(Reference, Target(10));
        Assert.Equal(TargetVerdict.LowQuality, plan.Verdict);
        Assert.Equal(new TargetSuggestion(MaxHeight: null, MaxFps: 30), plan.Suggestion);
    }

    [Fact]
    public void TooSmallForH264_SuggestsFpsAndThen1080p()
    {
        // H.264 needs ~1.5x more bits: 1440p30 is not enough, 1080p30 is.
        var plan = TargetSizePlanner.Plan(Reference, Target(10, OutputFormat.Compatible));
        Assert.Equal(TargetVerdict.LowQuality, plan.Verdict);
        Assert.Equal(new TargetSuggestion(MaxHeight: 1080, MaxFps: 30), plan.Suggestion);
    }

    [Fact]
    public void NothingHelps_SuggestsMinimalSize()
    {
        var plan = TargetSizePlanner.Plan(Reference, Target(2));
        Assert.Equal(TargetVerdict.LowQuality, plan.Verdict);
        Assert.Null(plan.Suggestion);
        Assert.NotNull(plan.MinReasonableSizeBytes);
        Assert.True(plan.MinReasonableSizeBytes > 2 * MiB);
        Assert.Equal(0, plan.MinReasonableSizeBytes % MiB);

        // The suggested minimum really is enough at the original resolution and frame rate.
        var atMinimum = TargetSizePlanner.Plan(Reference, Target(plan.MinReasonableSizeBytes!.Value / MiB));
        Assert.Equal(TargetVerdict.Ok, atMinimum.Verdict);
    }

    [Fact]
    public void Suggestion_NeverRepeatsWhatIsAlreadyApplied()
    {
        // Already at 30 fps: the first useful step is a lower resolution, not "30 fps" again.
        var settings = Target(10, OutputFormat.Compatible) with { MaxFps = 30 };
        var plan = TargetSizePlanner.Plan(Reference, settings);
        Assert.Equal(new TargetSuggestion(MaxHeight: 1080, MaxFps: 30), plan.Suggestion);
    }

    [Fact]
    public void Limits_AreTakenIntoAccount()
    {
        // With 30 fps already chosen, 10 MB is acceptable for H.265.
        var plan = TargetSizePlanner.Plan(Reference, Target(10) with { MaxFps = 30 });
        Assert.Equal(TargetVerdict.Ok, plan.Verdict);
    }

    [Fact]
    public void Plan_RequiresTargetMode() =>
        Assert.Throws<ArgumentException>(() => TargetSizePlanner.Plan(Reference, CompressionSettings.Default));
}
