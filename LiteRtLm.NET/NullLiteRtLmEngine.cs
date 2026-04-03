using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LiteRtLm.NET;

/// <summary>
/// No-op engine for unsupported platforms (iOS until Swift SDK stabilizes, desktop, etc.).
/// Always returns IsInitialized = false, causing callers to fall back to legacy paths.
/// </summary>
public class NullLiteRtLmEngine : ILiteRtLmEngine
{
    private const string UnavailableError = "LiteRT-LM not available on this platform";

    /// <inheritdoc />
    public bool IsInitialized => false;

    /// <inheritdoc />
    public long InitializeTimeMs => 0;

    /// <inheritdoc />
    public Task<bool> InitializeAsync(EngineOptions options, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <inheritdoc />
    public void CreateConversation(ConversationOptions? options = null) { }

    /// <inheritdoc />
    public Task<InferenceResult> SendMessageAsync(string text, CancellationToken ct = default)
        => Task.FromResult(new InferenceResult { Success = false, Error = UnavailableError });

    /// <inheritdoc />
    public Task<InferenceResult> SendMessageAsync(string text, byte[] imageBytes, CancellationToken ct = default)
        => Task.FromResult(new InferenceResult { Success = false, Error = UnavailableError });

    /// <inheritdoc />
    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        string text, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <inheritdoc />
    public void Dispose() { }
}
