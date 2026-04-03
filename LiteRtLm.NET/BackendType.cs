namespace LiteRtLm.NET;

/// <summary>
/// Hardware acceleration backend for LiteRT-LM inference.
/// Maps to the SDK's Backend.GPU(), Backend.CPU(), Backend.NPU().
/// </summary>
public enum BackendType
{
    /// <summary>CPU inference (universal, slower).</summary>
    Cpu = 0,

    /// <summary>GPU inference (faster, requires GPU driver support).</summary>
    Gpu = 1,

    /// <summary>
    /// NPU inference (fastest on supported SoCs, e.g. Qualcomm, MediaTek).
    /// Requires NativeLibraryDir in EngineOptions.
    /// </summary>
    Npu = 2
}
