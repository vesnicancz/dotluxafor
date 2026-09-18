using System.Diagnostics;
using DotLuxafor;
using Moq;

namespace DotLuxafor.Tests;

public class LuxaforAnimationsTests
{
    private const byte CommandStaticColor = 0x01;

    private readonly Mock<IHidStreamAdapter> _stream = new();
    private readonly List<byte[]> _written = new();

    public LuxaforAnimationsTests()
    {
        _stream.Setup(s => s.CanWrite).Returns(true);
        _stream.Setup(s => s.CanRead).Returns(true);
        _stream.Setup(s => s.Write(It.IsAny<byte[]>())).Callback<byte[]>(report =>
        {
            lock (_written)
            {
                _written.Add((byte[])report.Clone());
            }
        });
    }

    private LuxaforDevice CreateDevice() => new LuxaforDevice(_stream.Object);

    /// <summary>The colors sent by static-color commands, in order.</summary>
    private IReadOnlyList<LuxaforColor> Colors()
    {
        lock (_written)
        {
            return _written
                .Where(report => report[1] == CommandStaticColor)
                .Select(report => new LuxaforColor(report[3], report[4], report[5]))
                .ToList();
        }
    }

    private LuxaforColor LastSent() => Colors()[Colors().Count - 1];

    #region FadeOverAsync

    [Fact]
    public async Task FadeOverAsync_LandsExactlyOnTheTargetColor()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();

        await device.FadeOverAsync(LuxaforColor.Off, LuxaforColor.Red, TimeSpan.FromMilliseconds(150), cancellationToken: ct);

        Assert.Equal(LuxaforColor.Red, LastSent());
        Assert.Equal(LuxaforColor.Red, device.LastColor);
    }

    [Fact]
    public async Task FadeOverAsync_PassesThroughIntermediateColors()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();

        // A whole second, because the number of frames is not something the fade promises: it is
        // driven by the clock and drops frames rather than running long, so a loaded machine can
        // legitimately produce only the first and the last. Asserting a frame count would be
        // asserting that the machine is idle. One second leaves room for a delay to overshoot
        // fortyfold and still land a frame in the middle.
        await device.FadeOverAsync(LuxaforColor.Off, LuxaforColor.Red, TimeSpan.FromSeconds(1), cancellationToken: ct);

        var colors = Colors();
        Assert.Contains(colors, c => c.R > 0 && c.R < 255);

        // These hold at any frame rate: nothing is ever sent off the line between the two colors.
        Assert.All(colors, c => Assert.Equal(0, c.G));
        Assert.All(colors, c => Assert.Equal(0, c.B));
    }

    [Fact]
    public async Task FadeOverAsync_RisesMonotonically()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();

        await device.FadeOverAsync(LuxaforColor.Off, LuxaforColor.White, TimeSpan.FromMilliseconds(200), cancellationToken: ct);

        var reds = Colors().Select(c => (int)c.R).ToList();
        Assert.Equal(reds.OrderBy(r => r), reds);
    }

    [Fact]
    public async Task FadeOverAsync_TakesAtLeastTheRequestedTime()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        var duration = TimeSpan.FromMilliseconds(200);

        var elapsed = Stopwatch.StartNew();
        await device.FadeOverAsync(LuxaforColor.Off, LuxaforColor.Red, duration, cancellationToken: ct);
        elapsed.Stop();

        // The fade is driven by the clock, so it never finishes early however fast the writes are.
        Assert.True(elapsed.Elapsed >= duration, $"Fade finished in {elapsed.Elapsed}, expected at least {duration}.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task FadeOverAsync_ZeroOrNegativeDuration_JustSetsTheTargetColor(int milliseconds)
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();

        await device.FadeOverAsync(LuxaforColor.Off, LuxaforColor.Red, TimeSpan.FromMilliseconds(milliseconds), cancellationToken: ct);

        Assert.Equal(LuxaforColor.Red, Assert.Single(Colors()));
    }

    [Fact]
    public async Task FadeOverAsync_HonoursTheTarget()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();

        await device.FadeOverAsync(LuxaforColor.Off, LuxaforColor.Red, TimeSpan.Zero, LedTarget.TopSide, ct);

        lock (_written)
        {
            Assert.All(_written, report => Assert.Equal((byte)LedTarget.TopSide, report[2]));
        }
    }

    [Fact]
    public async Task FadeOverAsync_Cancelled_StopsMidFade()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            device.FadeOverAsync(LuxaforColor.Off, LuxaforColor.Red, TimeSpan.FromSeconds(30), cancellationToken: cts.Token));

        Assert.NotEmpty(Colors());
        Assert.NotEqual(LuxaforColor.Red, LastSent());
    }

    [Fact]
    public async Task FadeOverAsync_FromCurrentColor_StartsAtLastColor()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);

        // Fading red to red: if the start really is LastColor, nothing else is ever sent.
        await device.FadeOverAsync(LuxaforColor.Red, TimeSpan.FromMilliseconds(150), cancellationToken: ct);

        Assert.All(Colors(), c => Assert.Equal(LuxaforColor.Red, c));
    }

    [Fact]
    public async Task FadeOverAsync_FromCurrentColor_FallsBackToOffWhenTheRestingColorIsUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        await device.PlayPatternAsync(BuiltInPattern.Rainbow, repeat: 1, cancellationToken: ct);
        Assert.Null(device.LastColor);

        await device.FadeOverAsync(LuxaforColor.Red, TimeSpan.FromMilliseconds(250), cancellationToken: ct);

        // Started dark rather than jumping straight to the destination.
        Assert.True(Colors()[0].R < 128, $"First frame was {Colors()[0]}, expected it to start near off.");
        Assert.Equal(LuxaforColor.Red, LastSent());
    }

    [Fact]
    public async Task FadeOverAsync_NullDevice_Throws()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((ILuxaforCommands)null!).FadeOverAsync(LuxaforColor.Off, LuxaforColor.Red, TimeSpan.Zero, cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((ILuxaforDevice)null!).FadeOverAsync(LuxaforColor.Red, TimeSpan.Zero, cancellationToken: ct));
    }

    #endregion

    #region PulseAsync

    [Fact]
    public async Task PulseAsync_DimsAndBrightens()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();

        await device.PulseAsync(LuxaforColor.Red, TimeSpan.FromMilliseconds(200), cycles: 1, cancellationToken: ct);

        var reds = Colors().Select(c => (int)c.R).ToList();
        Assert.Contains(reds, r => r <= 60);
        Assert.Contains(reds, r => r >= 200);
        Assert.All(Colors(), c => Assert.Equal(0, c.G));
    }

    [Fact]
    public async Task PulseAsync_LeavesTheColorShowingWhenTheCyclesAreDone()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();

        await device.PulseAsync(LuxaforColor.Red, TimeSpan.FromMilliseconds(100), cycles: 1, cancellationToken: ct);

        Assert.Equal(LuxaforColor.Red, LastSent());
        Assert.Equal(LuxaforColor.Red, device.LastColor);
    }

    [Fact]
    public async Task PulseAsync_MoreCyclesRunLonger()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        var period = TimeSpan.FromMilliseconds(120);

        var elapsed = Stopwatch.StartNew();
        await device.PulseAsync(LuxaforColor.Red, period, cycles: 2, cancellationToken: ct);
        elapsed.Stop();

        Assert.True(elapsed.Elapsed >= TimeSpan.FromTicks(period.Ticks * 2),
            $"Two cycles took {elapsed.Elapsed}, expected at least {TimeSpan.FromTicks(period.Ticks * 2)}.");
    }

    [Fact]
    public async Task PulseAsync_ZeroCycles_RunsUntilCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            device.PulseAsync(LuxaforColor.Red, TimeSpan.FromMilliseconds(100), cycles: 0, cancellationToken: cts.Token));

        Assert.NotEmpty(Colors());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PulseAsync_NonPositivePeriod_Throws(int milliseconds)
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            device.PulseAsync(LuxaforColor.Red, TimeSpan.FromMilliseconds(milliseconds), cancellationToken: ct));
    }

    [Fact]
    public async Task PulseAsync_NullDevice_Throws()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((ILuxaforCommands)null!).PulseAsync(LuxaforColor.Red, TimeSpan.FromMilliseconds(50), cancellationToken: ct));
    }

    #endregion

    #region SetColorScopedAsync

    [Fact]
    public async Task SetColorScopedAsync_SetsTheColorAndPutsBackThePreviousOne()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        await device.SetColorAsync(LuxaforColor.Green, cancellationToken: ct);

        await using (await device.SetColorScopedAsync(LuxaforColor.Red, cancellationToken: ct))
        {
            Assert.Equal(LuxaforColor.Red, LastSent());
        }

        Assert.Equal(LuxaforColor.Green, LastSent());
        Assert.Equal(LuxaforColor.Green, device.LastColor);
    }

    [Fact]
    public async Task SetColorScopedAsync_WithNoKnownRestingColor_RestoresOff()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();

        await using (await device.SetColorScopedAsync(LuxaforColor.Red, cancellationToken: ct))
        {
        }

        Assert.Equal(LuxaforColor.Off, LastSent());
    }

    [Fact]
    public async Task SetColorScopedAsync_ExplicitRestoreColor_IsUsedInsteadOfTheRestingOne()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        await device.SetColorAsync(LuxaforColor.Green, cancellationToken: ct);

        await using (await device.SetColorScopedAsync(LuxaforColor.Red, LuxaforColor.Blue, cancellationToken: ct))
        {
        }

        Assert.Equal(LuxaforColor.Blue, LastSent());
    }

    [Fact]
    public async Task SetColorScopedAsync_RestoresEvenWhenTheScopeEndsBecauseOfCancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        await device.SetColorAsync(LuxaforColor.Green, cancellationToken: ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await using (await device.SetColorScopedAsync(LuxaforColor.Red, cancellationToken: cts.Token))
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }
        });

        // Restoring uses CancellationToken.None precisely so that this case still puts the light back.
        Assert.Equal(LuxaforColor.Green, LastSent());
    }

    [Fact]
    public async Task SetColorScopedAsync_DisposedTwice_RestoresOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        using var device = CreateDevice();
        await device.SetColorAsync(LuxaforColor.Green, cancellationToken: ct);

        var scope = await device.SetColorScopedAsync(LuxaforColor.Red, cancellationToken: ct);
        await scope.DisposeAsync();
        var afterFirst = Colors().Count;
        await scope.DisposeAsync();

        Assert.Equal(afterFirst, Colors().Count);
    }

    [Fact]
    public async Task SetColorScopedAsync_DeviceDisposedInsideTheScope_DoesNotThrowOnExit()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();

        await using (await device.SetColorScopedAsync(LuxaforColor.Red, cancellationToken: ct))
        {
            device.Dispose();
        }
    }

    [Fact]
    public async Task SetColorScopedAsync_NullDevice_Throws()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((ILuxaforDevice)null!).SetColorScopedAsync(LuxaforColor.Red, cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((ILuxaforCommands)null!).SetColorScopedAsync(LuxaforColor.Red, LuxaforColor.Off, cancellationToken: ct));
    }

    #endregion
}
