namespace LiteRtLm.NET;

/// <summary>
/// Conversation-level configuration. Includes sampling parameters.
/// Each CreateConversation() call can use different options.
/// </summary>
public record ConversationOptions
{
    /// <summary>System instruction prepended to the conversation.</summary>
    public string? SystemInstruction { get; init; }

    /// <summary>Top-K sampling. 1 = greedy/deterministic. Default: 1.</summary>
    public int TopK { get; init; } = 1;

    /// <summary>Top-P (nucleus) sampling. 1.0 = disabled. Default: 1.0f.</summary>
    public float TopP { get; init; } = 1.0f;

    /// <summary>Temperature. 0.0 = deterministic. Default: 0.0f.</summary>
    public float Temperature { get; init; } = 0.0f;

    /// <summary>Default options for deterministic use (TopK=1, Temp=0).</summary>
    public static ConversationOptions Default { get; } = new();
}
