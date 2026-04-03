namespace LiteRtLm.NET;

/// <summary>
/// Device tier classification for model selection.
/// </summary>
public enum DeviceTier
{
    /// <summary>8+ GB RAM — can run E4B model.</summary>
    HighEnd,

    /// <summary>6-8 GB RAM — can run E2B model.</summary>
    MidRange,

    /// <summary>&lt;6 GB RAM — no on-device LLM, use legacy OCR only.</summary>
    LowEnd
}

/// <summary>
/// RAM detection and tier classification.
/// Platform-specific RAM retrieval is handled by the caller.
/// </summary>
public static class DeviceCapability
{
    /// <summary>
    /// Classify device tier based on total RAM in megabytes.
    /// </summary>
    public static DeviceTier ClassifyTier(long totalRamMB)
    {
        if (totalRamMB >= 8192)
            return DeviceTier.HighEnd;
        if (totalRamMB >= 6144)
            return DeviceTier.MidRange;
        return DeviceTier.LowEnd;
    }

    /// <summary>
    /// Whether the device has enough RAM to run any LiteRT-LM model.
    /// </summary>
    public static bool CanRunLiteRtLm(long totalRamMB) =>
        ClassifyTier(totalRamMB) != DeviceTier.LowEnd;

    /// <summary>
    /// Returns the recommended model variant for the given RAM.
    /// Null if the device cannot run any model.
    /// </summary>
    public static string? RecommendedModel(long totalRamMB) => ClassifyTier(totalRamMB) switch
    {
        DeviceTier.HighEnd => "gemma-4-E4B-it-int4",
        DeviceTier.MidRange => "gemma-4-E2B-it-int4",
        _ => null
    };
}
