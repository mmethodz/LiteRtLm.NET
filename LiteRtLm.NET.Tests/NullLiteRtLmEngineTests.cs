using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LiteRtLm.NET.Tests;

/// <summary>
/// Tests for NullLiteRtLmEngine — no device, no Android dependency.
/// </summary>
[TestFixture]
public class NullLiteRtLmEngineTests
{
    [Test]
    public void IsInitialized_ReturnsFalse()
    {
        using var engine = new NullLiteRtLmEngine();
        Assert.That(engine.IsInitialized, Is.False);
    }

    [Test]
    public async Task InitializeAsync_ReturnsFalse()
    {
        using var engine = new NullLiteRtLmEngine();
        var result = await engine.InitializeAsync(new EngineOptions());
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task SendMessageAsync_ReturnsFailure()
    {
        using var engine = new NullLiteRtLmEngine();
        var result = await engine.SendMessageAsync("hello");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.Not.Null);
        });
    }

    [Test]
    public async Task SendMessageStreamingAsync_YieldsNothing()
    {
        using var engine = new NullLiteRtLmEngine();
        var tokens = new System.Collections.Generic.List<string>();
        await foreach (var token in engine.SendMessageStreamingAsync("hello"))
        {
            tokens.Add(token);
        }
        Assert.That(tokens, Is.Empty);
    }
}
