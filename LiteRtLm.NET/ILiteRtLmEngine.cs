using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteRtLm.NET;

/// <summary>
/// Platform-agnostic interface for on-device LLM inference via LiteRT-LM.
/// Maps to the SDK's Engine + Conversation pattern.
/// Implementations: AndroidLiteRtLmEngine, iOSLiteRtLmEngine, NullLiteRtLmEngine.
/// </summary>
public interface ILiteRtLmEngine : IDisposable
{
    /// <summary>Whether the engine is initialized and ready for inference.</summary>
    bool IsInitialized { get; }

    /// <summary>Time taken to initialize the engine (ms). 0 if not initialized.</summary>
    long InitializeTimeMs { get; }

    /// <summary>
    /// Initialize the engine: loads model weights into memory.
    /// This can take 5-10 seconds — always call on a background thread.
    /// </summary>
    Task<bool> InitializeAsync(EngineOptions options, CancellationToken ct = default);

    /// <summary>
    /// Create or reset a conversation with optional configuration.
    /// Conversations are lightweight — create new ones for different tasks.
    /// </summary>
    void CreateConversation(ConversationOptions? options = null);

    /// <summary>Send a text message and get a response.</summary>
    Task<InferenceResult> SendMessageAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Send a multimodal message (text + image bytes) and get a response.
    /// Requires EngineOptions.VisionBackend to be set.
    /// </summary>
    Task<InferenceResult> SendMessageAsync(string text, byte[] imageBytes, CancellationToken ct = default);

    /// <summary>Send a text message and stream response tokens.</summary>
    IAsyncEnumerable<string> SendMessageStreamingAsync(string text, CancellationToken ct = default);
}
