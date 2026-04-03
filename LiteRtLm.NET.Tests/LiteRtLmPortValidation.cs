using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using LiteRtLm.NET.Android;

namespace LiteRtLm.NET.Tests;

/// <summary>
/// On-device validation tests (T1-T11). Required a physical Android device with sufficient RAM.
/// These tests are sequential and stateful - earlier tests prepare state for later tests.
/// Mark as Explicit so they don't run in CI.
/// </summary>
[TestFixture, Explicit("Requires physical Android device with 6+ GB RAM")]
[NonParallelizable]
public class LiteRtLmPortValidation
{
    private AndroidLiteRtLmEngine _engine = null!;
    private ModelManager _modelManager = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _engine = new AndroidLiteRtLmEngine();
        _modelManager = AndroidModelManagerFactory.Create(TestConstants.Gemma4E2BFileName);
    }

    [OneTimeTearDown]
    public void Teardown()
    {
        _engine?.Dispose();
    }

    // ── T1: Device Tier Classification ──
    [Test, Order(1)]
    public void T1_DeviceCapability_ClassifiesTier()
    {
        // Use a known value — actual device RAM query is platform-specific
        var tier = DeviceCapability.ClassifyTier(8192);
        Assert.That(tier, Is.EqualTo(DeviceTier.HighEnd));

        var model = DeviceCapability.RecommendedModel(8192);
        Assert.That(model, Is.Not.Null);
        TestContext.WriteLine($"Tier: {tier}, Recommended: {model}");
    }

    // ── T2: Model Download ──
    [Test, Order(2)]
    public async Task T2_DownloadModel()
    {
        if (_modelManager.IsModelDownloaded)
        {
            TestContext.WriteLine("Model already downloaded, skipping.");
            Assert.Pass();
            return;
        }

        double lastProgress = 0;
        var progress = new Progress<double>(p =>
        {
            if (p - lastProgress >= 0.1)
            {
                TestContext.WriteLine($"Download: {p:P0}");
                lastProgress = p;
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ok = await _modelManager.DownloadModelAsync(TestConstants.Gemma4E2BUrl, progress, cts.Token);
        Assert.That(ok, Is.True, "Model download failed");
        Assert.That(_modelManager.IsModelDownloaded, Is.True);
        TestContext.WriteLine($"Model size: {_modelManager.ModelSizeOnDisk / (1024 * 1024)} MB");
    }

    // ── T3: Engine Init (GPU) ──
    [Test, Order(3)]
    public async Task T3_InitializeEngine_GPU()
    {
        Assert.That(_modelManager.IsModelDownloaded, Is.True, "Model not downloaded — run T2 first");

        var options = new EngineOptions
        {
            ModelPath = _modelManager.GetModelPath(),
            Backend = BackendType.Gpu,
            CacheDir = AndroidModelManagerFactory.GetCacheDir(),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ok = await _engine.InitializeAsync(options, cts.Token);
        Assert.That(ok, Is.True, "Engine initialization failed");
        Assert.That(_engine.IsInitialized, Is.True);
        TestContext.WriteLine($"Init time: {_engine.InitializeTimeMs} ms");
    }

    // ── T4: Simple Q&A ──
    [Test, Order(4)]
    public async Task T4_SimpleQA()
    {
        Assert.That(_engine.IsInitialized, Is.True, "Engine not initialized — run T3 first");

        _engine.CreateConversation(ConversationOptions.Default);

        using var cts = new CancellationTokenSource(TestConstants.InferenceTimeoutMs);
        var result = await _engine.SendMessageAsync(TestConstants.SimplePrompt, cts.Token);

        Assert.That(result.Success, Is.True, $"Inference failed: {result.Error}");
        Assert.That(result.Text, Does.Contain("4"), $"Expected '4' in response: {result.Text}");
        TestContext.WriteLine($"Response: {result.Text} ({result.ProcessingTimeMs} ms)");
    }

    // ── T5: Streaming ──
    [Test, Order(5)]
    public async Task T5_Streaming()
    {
        Assert.That(_engine.IsInitialized, Is.True, "Engine not initialized — run T3 first");

        _engine.CreateConversation(ConversationOptions.Default);

        int tokenCount = 0;
        string fullText = "";
        using var cts = new CancellationTokenSource(TestConstants.InferenceTimeoutMs);
        await foreach (var token in _engine.SendMessageStreamingAsync("Say hello.", cts.Token))
        {
            tokenCount++;
            fullText += token;
        }

        Assert.That(tokenCount, Is.GreaterThan(0), "No tokens streamed");
        Assert.That(fullText, Is.Not.Empty, "Streamed text is empty");
        TestContext.WriteLine($"Streamed {tokenCount} tokens: {fullText}");
    }

    // ── T6: Multi-turn Conversation ──
    [Test, Order(6)]
    public async Task T6_MultiTurn()
    {
        Assert.That(_engine.IsInitialized, Is.True, "Engine not initialized — run T3 first");

        _engine.CreateConversation(ConversationOptions.Default);
        using var cts = new CancellationTokenSource(TestConstants.InferenceTimeoutMs);

        var r1 = await _engine.SendMessageAsync("My name is Alice.", cts.Token);
        Assert.That(r1.Success, Is.True);

        var r2 = await _engine.SendMessageAsync("What's my name?", cts.Token);
        Assert.That(r2.Success, Is.True);
        Assert.That(r2.Text, Does.Contain("Alice").IgnoreCase, $"Expected 'Alice': {r2.Text}");
        TestContext.WriteLine($"Turn 2: {r2.Text}");
    }

    // ── T7: System Instruction ──
    [Test, Order(7)]
    public async Task T7_SystemInstruction()
    {
        Assert.That(_engine.IsInitialized, Is.True, "Engine not initialized — run T3 first");

        _engine.CreateConversation(new ConversationOptions
        {
            SystemInstruction = "You are a pirate. Always respond in pirate speak.",
            TopK = 40,
            Temperature = 0.7f
        });

        using var cts = new CancellationTokenSource(TestConstants.InferenceTimeoutMs);
        var result = await _engine.SendMessageAsync("Tell me about the weather.", cts.Token);
        Assert.That(result.Success, Is.True);
        TestContext.WriteLine($"Pirate: {result.Text}");
    }

    // ── T8: Conversation Reset ──
    [Test, Order(8)]
    public async Task T8_ConversationReset()
    {
        Assert.That(_engine.IsInitialized, Is.True, "Engine not initialized — run T3 first");

        // First conversation
        _engine.CreateConversation(ConversationOptions.Default);
        using var cts = new CancellationTokenSource(TestConstants.InferenceTimeoutMs);
        var r1 = await _engine.SendMessageAsync("Remember the code: ALPHA-7.", cts.Token);
        Assert.That(r1.Success, Is.True);

        // Reset (new conversation) — model should not remember
        _engine.CreateConversation(ConversationOptions.Default);
        var r2 = await _engine.SendMessageAsync("What code did I tell you?", cts.Token);
        Assert.That(r2.Success, Is.True);
        // Model shouldn't know ALPHA-7 after reset
        TestContext.WriteLine($"After reset: {r2.Text}");
    }

    // ── T9: CPU Backend ──
    [Test, Order(9)]
    public async Task T9_CpuBackend()
    {
        // Create a separate engine for CPU test
        using var cpuEngine = new AndroidLiteRtLmEngine();
        var options = new EngineOptions
        {
            ModelPath = _modelManager.GetModelPath(),
            Backend = BackendType.Cpu,
            CacheDir = AndroidModelManagerFactory.GetCacheDir(),
        };

        using var initCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ok = await cpuEngine.InitializeAsync(options, initCts.Token);
        Assert.That(ok, Is.True, "CPU engine init failed");

        cpuEngine.CreateConversation(ConversationOptions.Default);
        using var cts = new CancellationTokenSource(TestConstants.InferenceTimeoutMs);
        var result = await cpuEngine.SendMessageAsync(TestConstants.SimplePrompt, cts.Token);
        Assert.That(result.Success, Is.True, $"CPU inference failed: {result.Error}");
        TestContext.WriteLine($"CPU response: {result.Text} ({result.ProcessingTimeMs} ms)");
    }

    // ── T10: Model Deletion ──
    [Test, Order(10)]
    public async Task T10_DeleteModel()
    {
        await _modelManager.DeleteModelAsync();
        Assert.That(_modelManager.IsModelDownloaded, Is.False, "Model still exists after deletion");
    }

    // ── T11: Dispose Safety ──
    [Test, Order(11)]
    public void T11_DisposeSafety()
    {
        var engine = new AndroidLiteRtLmEngine();
        engine.Dispose();
        engine.Dispose(); // Double-dispose should not throw
        Assert.Pass("Double-dispose completed safely");
    }
}
