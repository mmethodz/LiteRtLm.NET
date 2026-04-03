# LiteRtLm.NET

C# bindings for [Google AI Edge LiteRT-LM](https://ai.google.dev/edge/litert-lm) — run Gemma and other LLMs **on-device** from .NET Android (and eventually .NET MAUI) apps.

> **Status:** 0.1.0-alpha — Android support only. API surface may change.

## Why?

The official LiteRT-LM SDK is Kotlin-only. This project provides:

- **`LiteRtLm.NET`** — platform-agnostic interfaces and models (`netstandard2.0`)
- **`LiteRtLm.NET.Android`** — Android implementation that binds the Kotlin SDK via auto-generated Java bindings + a thin Kotlin bridge for `suspend`/`Flow`/`Companion` interop

No cloud API keys. No network dependency at inference time. Everything runs locally on the device GPU, CPU, or NPU.

## Architecture

```
┌──────────────────────────────────────────────────┐
│  Your .NET MAUI / Android App                    │
├──────────────────────────────────────────────────┤
│  LiteRtLm.NET  (netstandard2.0)                 │
│    ILiteRtLmEngine · IModelManager               │
│    EngineOptions · ConversationOptions            │
│    InferenceResult · DeviceCapability             │
├──────────────────────────────────────────────────┤
│  LiteRtLm.NET.Android  (net10.0-android)        │
│    AndroidLiteRtLmEngine (generated bindings)     │
│    LiteRtLmInterop.aar  (Kotlin bridge)          │
│    litertlm-android:0.10.0  (Google Maven)       │
└──────────────────────────────────────────────────┘
```

**Hybrid bridge pattern:** synchronous SDK calls (constructors, configuration) go through .NET Android's auto-generated Java bindings directly. Kotlin-specific async patterns (`suspend initialize`, `suspend sendMessage`, `Flow` streaming, `Contents.Companion.of()`) are handled by a minimalist Kotlin bridge AAR (`LiteRtLmInterop`) that converts coroutines to callback interfaces implemented in C#.

## Supported Models

| Model | Min RAM | DeviceTier |
|-------|---------|------------|
| `gemma-4-E4B-it-int4` | 8 GB | HighEnd |
| `gemma-4-E2B-it-int4` | 6 GB | MidRange |

Use `DeviceCapability.RecommendedModel(totalRamMB)` to pick the right variant at runtime.

## Quick Start

```csharp
// 1. Check device capability
if (!DeviceCapability.CanRunLiteRtLm(totalRamMB))
    return; // fall back to legacy path

// 2. Download model (one-time)
var modelManager = AndroidModelManagerFactory.Create("gemma-4-E2B-it-int4.litertlm");
if (!modelManager.IsModelDownloaded)
{
    await modelManager.DownloadModelAsync(
        "https://storage.googleapis.com/litert-lm/release/gemma-4-E2B-it-int4.litertlm",
        new Progress<double>(p => Console.WriteLine($"Download: {p:P0}")));
}

// 3. Initialize engine
using var engine = new AndroidLiteRtLmEngine();
var ok = await engine.InitializeAsync(new EngineOptions
{
    ModelPath = modelManager.GetModelPath(),
    Backend  = BackendType.Gpu,
    CacheDir = AndroidModelManagerFactory.GetCacheDir(),
});

// 4. Create conversation and chat
engine.CreateConversation(new ConversationOptions
{
    SystemInstruction = "You are a helpful assistant.",
    TopK = 40,
    Temperature = 0.7f
});

var result = await engine.SendMessageAsync("What is 2+2?");
Console.WriteLine(result.Text); // "4"

// 5. Streaming
await foreach (var token in engine.SendMessageStreamingAsync("Tell me a joke."))
{
    Console.Write(token);
}
```

## API Reference

### `ILiteRtLmEngine`

| Member | Description |
|--------|-------------|
| `IsInitialized` | Whether the engine is loaded and ready |
| `InitializeTimeMs` | Wall-clock time for initialization (ms) |
| `InitializeAsync(options, ct)` | Load model weights — takes 5-10s, call off UI thread |
| `CreateConversation(options?)` | Start or reset a conversation |
| `SendMessageAsync(text, ct)` | Send text, get full response |
| `SendMessageAsync(text, imageBytes, ct)` | Multimodal (requires `VisionBackend`) |
| `SendMessageStreamingAsync(text, ct)` | Stream response tokens via `IAsyncEnumerable<string>` |

### `EngineOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ModelPath` | `string` | `""` | Absolute path to `.litertlm` model file |
| `Backend` | `BackendType` | `Cpu` | `Cpu`, `Gpu`, or `Npu` |
| `VisionBackend` | `BackendType?` | `null` | Set for multimodal inference |
| `AudioBackend` | `BackendType?` | `null` | Set for audio inference |
| `CacheDir` | `string?` | `null` | Cache compiled model artifacts (faster 2nd+ init) |
| `NativeLibraryDir` | `string?` | `null` | Required for NPU on some SoCs |

### `ConversationOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SystemInstruction` | `string?` | `null` | System prompt prepended to conversation |
| `TopK` | `int` | `1` | Top-K sampling (1 = greedy) |
| `TopP` | `float` | `1.0` | Nucleus sampling (1.0 = disabled) |
| `Temperature` | `float` | `0.0` | Randomness (0.0 = deterministic) |

### `InferenceResult`

| Property | Type | Description |
|----------|------|-------------|
| `Success` | `bool` | Whether inference completed |
| `Text` | `string` | Model response text |
| `ProcessingTimeMs` | `int` | Wall-clock inference time |
| `Error` | `string?` | Error message if `Success` is `false` |

### `DeviceCapability`

```csharp
DeviceTier ClassifyTier(long totalRamMB)    // HighEnd (8+GB), MidRange (6-8GB), LowEnd (<6GB)
bool CanRunLiteRtLm(long totalRamMB)        // true if MidRange or HighEnd
string? RecommendedModel(long totalRamMB)   // model name or null
```

### `NullLiteRtLmEngine`

No-op implementation for unsupported platforms. `InitializeAsync` returns `false`, `SendMessageAsync` returns `Success = false`. Use for graceful fallback on iOS/desktop until platform support lands.

## Project Structure

```
LiteRtLm.NET/                  Core library (netstandard2.0)
  ILiteRtLmEngine.cs             Main engine interface
  IModelManager.cs                Model download interface
  ModelManager.cs                 HTTP download + atomic file rename
  EngineOptions.cs                Engine configuration record
  ConversationOptions.cs          Conversation parameters record
  InferenceResult.cs              Response model record
  BackendType.cs                  CPU/GPU/NPU enum
  DeviceCapability.cs             RAM-based tier classification
  NullLiteRtLmEngine.cs           Fallback no-op engine

LiteRtLm.NET.Android/           Android bindings (net10.0-android)
  AndroidLiteRtLmEngine.cs        ILiteRtLmEngine implementation
  AndroidModelManagerFactory.cs   Android-specific paths helper
  Transforms/Metadata.xml         Java binding metadata
  libs/LiteRtLmInterop-release.aar  Kotlin bridge

LiteRtLmBridge/                 Kotlin bridge (Gradle)
  src/main/kotlin/.../LiteRtLmInterop.kt
                                  Bridges suspend/Flow/Companion → callbacks

LiteRtLm.NET.Tests/             Test project (net10.0-android)
  DeviceCapabilityTests.cs        Pure logic tests (run anywhere)
  NullLiteRtLmEngineTests.cs      Null engine tests
  LiteRtLmPortValidation.cs       On-device T1-T11 validation suite
```

## Building

### Prerequisites

- .NET 10 Preview SDK
- JDK 17+ (for Gradle bridge build)
- Android SDK (API 26+)

### Build the Kotlin bridge (one-time or after changes)

```powershell
cd LiteRtLmBridge
.\gradlew.bat assembleRelease
copy build\outputs\aar\LiteRtLmInterop-release.aar ..\LiteRtLm.NET.Android\libs\
```

### Build the .NET solution

```powershell
dotnet build -c Release
```

### Run tests

Pure logic tests (`DeviceCapabilityTests`, `NullLiteRtLmEngineTests`) can verify compilation but require an Android target. On-device tests (`LiteRtLmPortValidation`) require a physical Android device — deploy via Visual Studio with the `Explicit` attribute filter.

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [PolySharp](https://github.com/Sergio0694/PolySharp) | 1.15.0 | C# 9+ polyfills for netstandard2.0 |
| [Microsoft.Bcl.AsyncInterfaces](https://www.nuget.org/packages/Microsoft.Bcl.AsyncInterfaces) | 9.0.5 | `IAsyncEnumerable<T>` for netstandard2.0 |
| [litertlm-android](https://ai.google.dev/edge/litert-lm) | 0.10.0 | Google AI Edge LiteRT-LM SDK (Maven) |
| [GoogleGson](https://www.nuget.org/packages/GoogleGson) | 2.13.2 | JSON serialization (SDK dependency) |
| [Xamarin.Kotlin.Reflect](https://www.nuget.org/packages/Xamarin.Kotlin.Reflect) | 2.2.21 | Kotlin runtime bindings |
| [Xamarin.KotlinX.Coroutines.Android](https://www.nuget.org/packages/Xamarin.KotlinX.Coroutines.Android) | 1.9.0 | Coroutines support for bridge |

## License

Apache License 2.0 — see [LICENSE](LICENSE).
