// SampleTest.cs
// ─────────────────────────────────────────────────────────────────────────────
// Smoke test: confirms the test project compiles and xUnit discovers tests.
// No Godot types used anywhere in this file or its imports.
//
// Delete or replace this file once real Logic tests exist.
// ─────────────────────────────────────────────────────────────────────────────

using Xunit;

namespace Raptor.Tests.Logic;

/// <summary>
/// Confirms the test harness is wired up correctly.
/// All assertions use only System types — no Godot dependency.
/// </summary>
public class SampleTest
{
    [Fact]
    public void TrueIsTrue()
    {
        // Trivial: if this fails, xUnit itself is broken.
        Assert.True(true);
    }

    [Fact]
    public void DotNetArithmeticIsCorrect()
    {
        // Slightly less trivial: confirms .NET 8 runtime is present.
        int result = 6 * 7;
        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(0,   0,   0)]
    [InlineData(1,   1,   2)]
    [InlineData(10, -3,   7)]
    [InlineData(-5, -5, -10)]
    public void IntAdditionIsCommutative(int a, int b, int expected)
    {
        // Theory test: confirms xUnit data-driven tests work.
        Assert.Equal(expected, a + b);
        Assert.Equal(expected, b + a);
    }
}
