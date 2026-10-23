using VideoCompressor.Core.Compression;

namespace VideoCompressor.App.ViewModels;

/// <summary>A named compression level as shown in the UI (spec US-2.1, US-2.2).</summary>
public sealed record LevelOption(CompressionLevel Level, string Name, string Description)
{
    // Ratios are from the reference game recording (research R-2); screen captures usually compress better.
    public static IReadOnlyList<LevelOption> All { get; } =
    [
        new(CompressionLevel.Minimal, "Минимальное",
            "Почти без потерь качества. Файл примерно в 2–3 раза меньше."),
        new(CompressionLevel.Balanced, "Сбалансированное",
            "Оптимально для записей экрана и игр: отличное качество, файл примерно в 5 раз меньше."),
        new(CompressionLevel.Strong, "Сильное",
            "Небольшие потери в мелких деталях. Файл примерно в 10 раз меньше."),
        new(CompressionLevel.Maximum, "Максимальное",
            "Минимальный размер, потери качества заметны."),
    ];

    public int Percent => (int)Level;
}
