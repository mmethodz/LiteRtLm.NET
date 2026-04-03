namespace LiteRtLm.NET.Tests;

/// <summary>Shared constants for test runs.</summary>
public static class TestConstants
{
    // ── model download URLs ──────────────────────────
    public const string Gemma4E2BUrl =
        "https://storage.googleapis.com/litert-lm/release/gemma-4-E2B-it-int4.litertlm";

    public const string Gemma4E4BUrl =
        "https://storage.googleapis.com/litert-lm/release/gemma-4-E4B-it-int4.litertlm";

    // ── file names ───────────────────────────────────
    public const string Gemma4E2BFileName = "gemma-4-E2B-it-int4.litertlm";
    public const string Gemma4E4BFileName = "gemma-4-E4B-it-int4.litertlm";

    // ── inference defaults ───────────────────────────
    public const int InferenceTimeoutMs = 120_000;
    public const string SimplePrompt = "What is 2+2? Answer with just the number.";
}
