using NUnit.Framework;

namespace LiteRtLm.NET.Tests;

/// <summary>
/// Tests for DeviceCapability tier classification.
/// Pure logic — no device, no Android dependency.
/// </summary>
[TestFixture]
public class DeviceCapabilityTests
{
    [TestCase(16384, DeviceTier.HighEnd)]
    [TestCase(12288, DeviceTier.HighEnd)]
    [TestCase(8192, DeviceTier.HighEnd)]
    [TestCase(8000, DeviceTier.MidRange)]
    [TestCase(6144, DeviceTier.MidRange)]
    [TestCase(6000, DeviceTier.LowEnd)]
    [TestCase(4096, DeviceTier.LowEnd)]
    [TestCase(2048, DeviceTier.LowEnd)]
    public void ClassifyTier_VariousRam_ReturnsExpected(long ramMB, DeviceTier expected)
    {
        Assert.That(DeviceCapability.ClassifyTier(ramMB), Is.EqualTo(expected));
    }

    [TestCase(8192, true)]
    [TestCase(6144, true)]
    [TestCase(4096, false)]
    public void CanRunLiteRtLm_VariousRam_ReturnsExpected(long ramMB, bool expected)
    {
        Assert.That(DeviceCapability.CanRunLiteRtLm(ramMB), Is.EqualTo(expected));
    }

    [TestCase(16384, "gemma-4-E4B-it-int4")]
    [TestCase(8192, "gemma-4-E4B-it-int4")]
    [TestCase(6144, "gemma-4-E2B-it-int4")]
    [TestCase(4096, null)]
    public void RecommendedModel_VariousRam_ReturnsExpected(long ramMB, string? expected)
    {
        Assert.That(DeviceCapability.RecommendedModel(ramMB), Is.EqualTo(expected));
    }
}
