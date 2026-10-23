using VideoCompressor.Core.Compression;
using VideoCompressor.Core.Ffmpeg;
using VideoCompressor.Core.Media;
using VideoCompressor.Core.Tests.Infrastructure;

namespace VideoCompressor.Core.Tests;

[Collection(FfmpegCollection.Name)]
public class BatchCompressionServiceTests(FfmpegFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CompressionSettings Cpu = CompressionSettings.Default with { Encoder = EncoderKind.Cpu };

    [Fact]
    public async Task FailedFile_DoesNotStopTheQueue()
    {
        var tools = fixture.RequireTools();
        var copy = CopyToNewFolder(fixture.ShortClip);
        var notVideo = Path.Combine(Path.GetDirectoryName(copy)!, "не видео.mp4");
        await File.WriteAllTextAsync(notVideo, "текст", Ct);
        var source = await new MediaProbe(tools).ProbeAsync(copy, Ct);

        // The broken file gets a fake VideoInfo: the queue must survive ffmpeg failing on it.
        BatchItem[] items = [new(copy, source), new(notVideo, source), new(fixture.ShortClip, source, fixture.NewOutputPath())];
        var updates = new List<BatchItemUpdate>();

        var results = await new BatchCompressionService(tools).RunAsync(
            items, Cpu, deleteNotSmaller: true, new SyncProgress<BatchItemUpdate>(updates.Add), null, Ct);

        Assert.Equal([BatchItemState.Done, BatchItemState.Failed, BatchItemState.Done], results.Select(r => r.State));
        Assert.NotNull(results[1].Error);
        Assert.True(File.Exists(results[0].Result!.OutputPath));
        Assert.Equal(items[2].OutputPath, results[2].Result!.OutputPath);

        // Every file reported "compressing" before its final state, in order.
        var compressingOrder = updates.Where(u => u.State == BatchItemState.Compressing).Select(u => u.Index).Distinct();
        Assert.Equal([0, 1, 2], compressingOrder);
    }

    [Fact]
    public async Task Cancel_KeepsFinishedFiles_RemovesCurrent_SkipsRest()
    {
        var tools = fixture.RequireTools();
        var probe = new MediaProbe(tools);
        var first = CopyToNewFolder(fixture.ShortClip);
        var second = CopyToNewFolder(fixture.LongClip);
        var third = CopyToNewFolder(fixture.ShortClip);
        BatchItem[] items =
        [
            new(first, await probe.ProbeAsync(first, Ct)),
            new(second, await probe.ProbeAsync(second, Ct)),
            new(third, await probe.ProbeAsync(third, Ct)),
        ];

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var updates = new List<BatchItemUpdate>();
        var progress = new SyncProgress<BatchItemUpdate>(u =>
        {
            updates.Add(u);
            if (u.Index == 1 && u.Progress is { Fraction: > 0 })
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BatchCompressionService(tools).RunAsync(items, Cpu, deleteNotSmaller: true, progress, null, cts.Token));

        var final = updates.GroupBy(u => u.Index).ToDictionary(g => g.Key, g => g.Last());
        Assert.Equal(BatchItemState.Done, final[0].State);
        Assert.True(File.Exists(final[0].Result!.OutputPath));
        Assert.Equal(BatchItemState.Cancelled, final[1].State);
        Assert.Equal(BatchItemState.Cancelled, final[2].State);
        Assert.DoesNotContain(updates, u => u.Index == 2 && u.State == BatchItemState.Compressing);

        // Nothing but the first result was written next to the second and third sources.
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(second)!));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(third)!));
    }

    [Fact]
    public async Task NotSmallerResult_IsDeletedAndSkipped_WhenAsked()
    {
        var tools = fixture.RequireTools();
        var input = CopyToNewFolder(fixture.AlreadyCompressedClip);
        var source = await new MediaProbe(tools).ProbeAsync(input, Ct);

        var results = await new BatchCompressionService(tools).RunAsync(
            [new BatchItem(input, source)], Cpu, deleteNotSmaller: true, null, null, Ct);

        Assert.Equal(BatchItemState.SkippedNotSmaller, results[0].State);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(input)!));
    }

    [Fact]
    public async Task NotSmallerResult_IsKept_WhenNotAsked()
    {
        var tools = fixture.RequireTools();
        var input = CopyToNewFolder(fixture.AlreadyCompressedClip);
        var source = await new MediaProbe(tools).ProbeAsync(input, Ct);

        var results = await new BatchCompressionService(tools).RunAsync(
            [new BatchItem(input, source)], Cpu, deleteNotSmaller: false, null, null, Ct);

        Assert.Equal(BatchItemState.Done, results[0].State);
        Assert.True(results[0].Result!.IsNotSmaller);
        Assert.True(File.Exists(results[0].Result!.OutputPath));
    }

    [Fact]
    public async Task BatchProgress_IsMonotonicAndEndsAtOne()
    {
        var tools = fixture.RequireTools();
        var probe = new MediaProbe(tools);
        var a = CopyToNewFolder(fixture.ShortClip);
        var b = CopyToNewFolder(fixture.LongClip);
        BatchItem[] items = [new(a, await probe.ProbeAsync(a, Ct)), new(b, await probe.ProbeAsync(b, Ct))];
        var reports = new List<BatchProgress>();

        await new BatchCompressionService(tools).RunAsync(
            items, Cpu, deleteNotSmaller: true, null, new SyncProgress<BatchProgress>(reports.Add), Ct);

        Assert.True(reports.Count >= 2);
        Assert.All(reports.Zip(reports.Skip(1)), p => Assert.True(p.Second.Fraction >= p.First.Fraction));
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Equal(2, reports[^1].Count);
        Assert.Contains(reports, r => r.CurrentIndex == 1);
    }

    /// <summary>Each test works in its own folder, so default output names never collide.</summary>
    private string CopyToNewFolder(string file)
    {
        var dir = Directory.CreateDirectory(Path.Combine(fixture.Directory, Guid.NewGuid().ToString("N"))).FullName;
        var copy = Path.Combine(dir, Path.GetFileName(file));
        File.Copy(file, copy);
        return copy;
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
