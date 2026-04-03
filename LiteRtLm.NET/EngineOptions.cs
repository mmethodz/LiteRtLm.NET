namespace LiteRtLm.NET;

/// <summary>
/// Engine-level configuration. Corresponds to the LiteRT-LM SDK's EngineConfig.
/// Immutable — create a new instance to change settings.
/// </summary>
public record EngineOptions
{
    /// <summary>Absolute path to the .litertlm model file.</summary>
    public string ModelPath { get; init; } = "";

    /// <summary>Hardware backend for text inference. Default: CPU.</summary>
    public BackendType Backend { get; init; } = BackendType.Cpu;

    /// <summary>
    /// Hardware backend for vision (image) inference.
    /// Set to GPU for multimodal use. Null = not configured.
    /// </summary>
    public BackendType? VisionBackend { get; init; }

    /// <summary>Hardware backend for audio inference. Null = not configured.</summary>
    public BackendType? AudioBackend { get; init; }

    /// <summary>
    /// Optional cache directory for compiled model artifacts.
    /// Significantly speeds up subsequent initializations (2nd+ load).
    /// </summary>
    public string? CacheDir { get; init; }

    /// <summary>Native library directory (required for NPU backend on some devices).</summary>
    public string? NativeLibraryDir { get; init; }
}
