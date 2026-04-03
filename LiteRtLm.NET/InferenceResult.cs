namespace LiteRtLm.NET;

/// <summary>Response model for inference results.</summary>
public record InferenceResult
{
    /// <summary>Whether inference completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>The model's text response.</summary>
    public string Text { get; init; } = "";

    /// <summary>Wall-clock time for the inference call in milliseconds.</summary>
    public int ProcessingTimeMs { get; init; }

    /// <summary>Error message if Success is false.</summary>
    public string? Error { get; init; }
}
