namespace VideoCompressor.App.ViewModels;

/// <summary>An item of a settings drop-down: what the user sees and what it means.</summary>
/// <param name="Hint">Optional explanation shown under the drop-down.</param>
public sealed record Option<T>(string Name, T Value, string? Hint = null);
