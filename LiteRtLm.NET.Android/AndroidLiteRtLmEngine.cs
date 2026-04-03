using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Com.Google.AI.Edge.Litertlm;
using Channel = System.Threading.Channels.Channel;
using SdkEngine = Com.Google.AI.Edge.Litertlm.Engine;
using SdkBackend = Com.Google.AI.Edge.Litertlm.Backend;

namespace LiteRtLm.NET.Android;

/// <summary>
/// Android implementation of <see cref="ILiteRtLmEngine"/>.
/// Uses auto-generated Java bindings directly for synchronous SDK calls
/// (EngineConfig, Backend, Engine constructor, Engine.CreateConversation),
/// and delegates to the <c>LiteRtLmInterop</c> Kotlin bridge for
/// Kotlin-specific async patterns (suspend initialize, suspend sendMessage,
/// Flow streaming, Companion factory access).
/// </summary>
public class AndroidLiteRtLmEngine : ILiteRtLmEngine
{
    private SdkEngine? _engine;
    private Conversation? _conversation;
    private Com.Litertlm.Dotnet.LiteRtLmInterop? _interop;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private long _initTimeMs;

    /// <inheritdoc />
    public bool IsInitialized => _engine?.IsInitialized ?? false;

    /// <inheritdoc />
    public long InitializeTimeMs => _initTimeMs;

    /// <inheritdoc />
    public Task<bool> InitializeAsync(EngineOptions options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>();
        ct.Register(() => tcs.TrySetCanceled());

        // Build SDK config objects via generated C# bindings (direct JNI)
        var backend = MapBackend(options.Backend, options.NativeLibraryDir);
        var visionBackend = MapBackendOrNull(options.VisionBackend, options.NativeLibraryDir);
        var audioBackend = MapBackendOrNull(options.AudioBackend, options.NativeLibraryDir);

        var config = new EngineConfig(
            options.ModelPath,
            backend,
            visionBackend,
            audioBackend,
            null,              // maxNumTokens
            options.CacheDir);

        _engine = new SdkEngine(config);
        _interop = new Com.Litertlm.Dotnet.LiteRtLmInterop();

        // Use Kotlin bridge for the suspend Engine.initialize()
        var sw = Stopwatch.StartNew();
        _interop.InitializeEngine(_engine, new InitCallbackImpl(tcs, sw, ms => _initTimeMs = ms));

        return tcs.Task;
    }

    /// <inheritdoc />
    public void CreateConversation(ConversationOptions? options = null)
    {
        var eng = _engine ?? throw new InvalidOperationException("Engine not initialized");
        var interop = _interop ?? throw new InvalidOperationException("Engine not initialized");

        _conversation?.Close();

        var opts = options ?? ConversationOptions.Default;

        // Use bridge to create ConversationConfig (needs Contents.Companion + named params)
        var convConfig = interop.CreateConversationConfig(
            opts.SystemInstruction,
            opts.TopK,
            (double)opts.TopP,
            (double)opts.Temperature);

        _conversation = eng.CreateConversation(convConfig);
    }

    /// <inheritdoc />
    public async Task<InferenceResult> SendMessageAsync(
        string text, CancellationToken ct = default)
    {
        await _inferenceLock.WaitAsync(ct);
        try
        {
            var conv = _conversation
                ?? throw new InvalidOperationException("No active conversation");
            var interop = _interop
                ?? throw new InvalidOperationException("Engine not initialized");

            var tcs = new TaskCompletionSource<InferenceResult>();
            ct.Register(() => tcs.TrySetCanceled());

            // Use bridge for the suspend sendMessage()
            interop.SendMessage(conv, text, new MessageCallbackImpl(tcs));
            return await tcs.Task;
        }
        finally { _inferenceLock.Release(); }
    }

    /// <inheritdoc />
    public async Task<InferenceResult> SendMessageAsync(
        string text, byte[] imageBytes, CancellationToken ct = default)
    {
        await _inferenceLock.WaitAsync(ct);
        try
        {
            var conv = _conversation
                ?? throw new InvalidOperationException("No active conversation");
            var interop = _interop
                ?? throw new InvalidOperationException("Engine not initialized");

            var tcs = new TaskCompletionSource<InferenceResult>();
            ct.Register(() => tcs.TrySetCanceled());

            // Use bridge for multimodal (needs Contents.Companion.of + suspend)
            interop.SendImageMessage(conv, text, imageBytes, new MessageCallbackImpl(tcs));
            return await tcs.Task;
        }
        finally { _inferenceLock.Release(); }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> SendMessageStreamingAsync(
        string text, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await _inferenceLock.WaitAsync(ct);
        try
        {
            var conv = _conversation
                ?? throw new InvalidOperationException("No active conversation");
            var interop = _interop
                ?? throw new InvalidOperationException("Engine not initialized");

            var channel = Channel.CreateUnbounded<string>();

            // Use bridge to convert Kotlin Flow → per-token callbacks → Channel
            interop.StreamMessage(conv, text, new StreamCallbackImpl(channel.Writer));

            await foreach (var token in channel.Reader.ReadAllAsync(ct))
                yield return token;
        }
        finally { _inferenceLock.Release(); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _inferenceLock.Dispose();
        _conversation?.Close();
        _conversation = null;
        _engine?.Close();
        _engine = null;
        _interop?.Dispose();
        _interop = null;
    }

    // ── Backend mapping helpers ──

    private static SdkBackend MapBackend(BackendType type, string? nativeLibDir) => type switch
    {
        BackendType.Gpu => new SdkBackend.GPU(),
        BackendType.Npu => new SdkBackend.NPU(nativeLibDir ?? ""),
        _ => new SdkBackend.CPU()
    };

    private static SdkBackend? MapBackendOrNull(BackendType? type, string? nativeLibDir) => type switch
    {
        BackendType.Gpu => new SdkBackend.GPU(),
        BackendType.Cpu => new SdkBackend.CPU(),
        BackendType.Npu => new SdkBackend.NPU(nativeLibDir ?? ""),
        _ => null
    };

    // ── Callback adapters (Java callbacks → C# async) ──

    private sealed class InitCallbackImpl :
        Java.Lang.Object, Com.Litertlm.Dotnet.LiteRtLmInterop.IInitCallback
    {
        private readonly TaskCompletionSource<bool> _tcs;
        private readonly Stopwatch _sw;
        private readonly Action<long> _setInitTime;

        public InitCallbackImpl(
            TaskCompletionSource<bool> tcs, Stopwatch sw, Action<long> setInitTime)
        {
            _tcs = tcs;
            _sw = sw;
            _setInitTime = setInitTime;
        }

        public void OnSuccess()
        {
            _setInitTime(_sw.ElapsedMilliseconds);
            _tcs.TrySetResult(true);
        }

        public void OnError(string message)
        {
            _setInitTime(_sw.ElapsedMilliseconds);
            _tcs.TrySetResult(false);
        }
    }

    private sealed class MessageCallbackImpl :
        Java.Lang.Object, Com.Litertlm.Dotnet.LiteRtLmInterop.IMessageCallback
    {
        private readonly TaskCompletionSource<InferenceResult> _tcs;

        public MessageCallbackImpl(TaskCompletionSource<InferenceResult> tcs) => _tcs = tcs;

        public void OnResult(string text, long elapsedMs) =>
            _tcs.TrySetResult(new InferenceResult
            {
                Success = true,
                Text = text,
                ProcessingTimeMs = (int)elapsedMs
            });

        public void OnError(string message) =>
            _tcs.TrySetResult(new InferenceResult { Success = false, Error = message });
    }

    private sealed class StreamCallbackImpl :
        Java.Lang.Object, Com.Litertlm.Dotnet.LiteRtLmInterop.IStreamCallback
    {
        private readonly ChannelWriter<string> _writer;

        public StreamCallbackImpl(ChannelWriter<string> writer) => _writer = writer;

        public void OnToken(string text) => _writer.TryWrite(text);
        public void OnDone() => _writer.Complete();
        public void OnError(string message) => _writer.Complete(new Exception(message));
    }
}
