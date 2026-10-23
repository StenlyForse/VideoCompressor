using System.Runtime.InteropServices;

namespace VideoCompressor.Core.Platform;

/// <summary>
/// Whether the OS's built-in video player can show H.265 (plan §4.8). When unsure, says "no",
/// so that the default output is a file that plays everywhere (spec FR-002, SC-007).
/// </summary>
public static class HevcPlaybackSupport
{
    private static readonly string[] GStreamerPluginDirectories =
    [
        "/usr/lib/x86_64-linux-gnu/gstreamer-1.0",
        "/usr/lib/aarch64-linux-gnu/gstreamer-1.0",
        "/usr/lib64/gstreamer-1.0",
        "/usr/lib/gstreamer-1.0",
    ];

    public static bool IsSupported()
    {
        if (OperatingSystem.IsWindows())
        {
            return MediaFoundation.HasHevcDecoder();
        }

        if (OperatingSystem.IsMacOS())
        {
            // QuickTime has played HEVC since macOS 10.13; .NET 10 requires a newer macOS anyway.
            return true;
        }

        // Linux desktop players (Totem etc.) use GStreamer; H.265 comes with the libav plugin.
        return GStreamerPluginDirectories.Any(dir => File.Exists(Path.Combine(dir, "libgstlibav.so")));
    }

    /// <summary>The built-in Windows players decode through Media Foundation, so ask it directly.</summary>
    private static class MediaFoundation
    {
        private static readonly Guid VideoDecoderCategory = new("d6c02d4b-6833-45b4-971a-05a4b04bab91");
        private static readonly Guid MediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
        private static readonly Guid VideoFormatHevc = new("43564548-0000-0010-8000-00aa00389b71");

        // SYNCMFT | ASYNCMFT | HARDWARE | LOCALMFT | SORTANDFILTER: everything a player could
        // actually use, without "field of use" (locked) and "transcode only" decoders.
        private const uint EnumFlags = 0x1 | 0x2 | 0x4 | 0x10 | 0x40;

        [StructLayout(LayoutKind.Sequential)]
        private struct RegisterTypeInfo
        {
            public Guid MajorType;
            public Guid Subtype;
        }

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFTEnumEx(
            Guid category, uint flags, ref RegisterTypeInfo inputType, IntPtr outputType,
            out IntPtr activates, out uint count);

        public static bool HasHevcDecoder()
        {
            try
            {
                var input = new RegisterTypeInfo { MajorType = MediaTypeVideo, Subtype = VideoFormatHevc };
                if (MFTEnumEx(VideoDecoderCategory, EnumFlags, ref input, IntPtr.Zero, out var activates, out var count) < 0)
                {
                    return false;
                }

                for (var i = 0; i < count; i++)
                {
                    Marshal.Release(Marshal.ReadIntPtr(activates, i * IntPtr.Size));
                }

                Marshal.FreeCoTaskMem(activates);
                return count > 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Windows "N" editions without the Media Feature Pack have no Media Foundation.
                return false;
            }
        }
    }
}
