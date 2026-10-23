using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace VideoCompressor.Core.Ffmpeg;

internal sealed record ProcessResult(int ExitCode, string Output, IReadOnlyList<string> ErrorTail)
{
    public string ErrorText => string.Join(Environment.NewLine, ErrorTail);
}

/// <summary>Runs ffmpeg/ffprobe without a console window and without shell quoting (NFR-004).</summary>
internal static class ProcessRunner
{
    /// <param name="onOutputLine">
    /// Receives stdout line by line; when <c>null</c>, stdout is collected into <see cref="ProcessResult.Output"/>.
    /// </param>
    /// <remarks>On cancellation the whole process tree is killed before the exception propagates.</remarks>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string>? onOutputLine = null,
        int errorTailLines = 30,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new StringBuilder();
        var errorTail = new Queue<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            if (onOutputLine is not null)
            {
                onOutputLine(e.Data);
            }
            else
            {
                output.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (errorTail)
            {
                errorTail.Enqueue(e.Data);
                if (errorTail.Count > errorTailLines)
                {
                    errorTail.Dequeue();
                }
            }
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new FfmpegException($"Не удалось запустить {Path.GetFileName(fileName)}.", ex.Message, ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        lock (errorTail)
        {
            return new ProcessResult(process.ExitCode, output.ToString(), [.. errorTail]);
        }
    }
}
