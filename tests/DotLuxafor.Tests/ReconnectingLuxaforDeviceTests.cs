using DotLuxafor;
using Microsoft.Extensions.Logging;
using Moq;

namespace DotLuxafor.Tests;

public class ReconnectingLuxaforDeviceTests
{
    private static readonly LuxaforDeviceDescriptor Descriptor =
        new LuxaforDeviceDescriptor("/dev/hidraw0", "LUXAFOR FLAG", "1001");

    private readonly Mock<ILuxaforDeviceManager> _manager = new();
    private readonly RecordingLogger _logger = new();

    /// <summary>
    /// Queues the devices the manager hands out, one per reopen. Anything beyond the queue is a
    /// device that cannot be opened, which is what an unplugged device looks like.
    /// </summary>
    private void Reopens(params FakeLuxaforDevice[] devices)
    {
        var queue = new Queue<FakeLuxaforDevice>(devices);
        _manager.Setup(m => m.Open(Descriptor)).Returns(() => queue.Count > 0
            ? DeviceOpenResult.Opened(queue.Dequeue(), Descriptor)
            : DeviceOpenResult.Failure(DeviceOpenStatus.NotFound, Descriptor, null));
    }

    private ReconnectingLuxaforDevice Wrap(FakeLuxaforDevice device)
        => new ReconnectingLuxaforDevice(_manager.Object, device, _logger);

    private static FakeLuxaforDevice Device() => new FakeLuxaforDevice(Descriptor);

    #region Replay

    [Fact]
    public async Task StateCommand_WhenTheHandleIsStale_ReopensAndSendsAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        Reopens(second);
        using var device = Wrap(first);
        first.HasGoneAway = true;

        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);

        // The whole point: the command reached a device, and the caller saw no exception.
        Assert.Contains(second.Commands, c => c.StartsWith("SetColor", StringComparison.Ordinal));
        Assert.True(first.IsDisposed);
        Assert.Equal(LuxaforColor.Red, device.LastColor);
    }

    [Theory]
    [InlineData("fade")]
    [InlineData("off")]
    public async Task EveryStateCommand_IsReplayed(string which)
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        Reopens(second);
        using var device = Wrap(first);
        first.HasGoneAway = true;

        if (which == "fade")
        {
            await device.FadeToAsync(LuxaforColor.Green, 30, cancellationToken: ct);
        }
        else
        {
            await device.TurnOffAsync(ct);
        }

        Assert.NotEmpty(second.Commands);
    }

    /// <summary>
    /// "Flash three times" describes an event in time. Replayed after an unknown pause it either
    /// duplicates an alert the user half saw or arrives after the thing it announced is over, so
    /// the caller is told instead — but the device is reopened, so the next command works.
    /// </summary>
    [Fact]
    public async Task Animation_WhenTheHandleIsStale_ReopensButDoesNotReplay()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        Reopens(second);
        using var device = Wrap(first);
        first.HasGoneAway = true;

        await Assert.ThrowsAsync<LuxaforDeviceDisconnectedException>(() =>
            device.StrobeAsync(LuxaforColor.Red, 10, 3, cancellationToken: ct));

        Assert.DoesNotContain(second.Commands, c => c.StartsWith("Strobe", StringComparison.Ordinal));

        // ...and the next command lands without the caller doing anything about it.
        await device.SetColorAsync(LuxaforColor.Blue, cancellationToken: ct);
        Assert.Contains(second.Commands, c => c.StartsWith("SetColor(#0000FF", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StateCommand_WhenTheReplayAlsoFails_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        second.HasGoneAway = true;
        Reopens(second);
        using var device = Wrap(first);
        first.HasGoneAway = true;

        await Assert.ThrowsAsync<LuxaforDeviceDisconnectedException>(() =>
            device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct));

        // Exactly one reopen: a second failure is the device really being gone, and looping on it
        // would hide that from the caller.
        _manager.Verify(m => m.Open(Descriptor), Times.Once);
    }

    #endregion

    #region State across a reopen

    /// <summary>
    /// A replugged device comes back dark, and a handle that merely went stale does not — and there
    /// is no telling which happened. So the remembered color is re-sent rather than assumed, and
    /// <see cref="LuxaforAnimations.FadeOverAsync(ILuxaforDevice, LuxaforColor, TimeSpan, LedTarget, CancellationToken)"/>
    /// (which starts from <c>LastColor</c>) starts from what is actually lit.
    /// </summary>
    [Fact]
    public async Task Reopen_PutsTheLastColorBackOnTheNewDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        Reopens(second);
        using var device = Wrap(first);
        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);
        first.HasGoneAway = true;

        await Assert.ThrowsAsync<LuxaforDeviceDisconnectedException>(() =>
            device.WaveAsync(WaveType.Short, LuxaforColor.Blue, 10, 3, ct));

        Assert.Contains(second.Commands, c => c.StartsWith("SetColor(#FF0000", StringComparison.Ordinal));
        Assert.Equal(LuxaforColor.Red, device.LastColor);
    }

    [Fact]
    public async Task Reopen_BeforeAReplay_DoesNotWriteTheColorTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        Reopens(second);
        using var device = Wrap(first);
        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);
        first.HasGoneAway = true;

        await device.SetColorAsync(LuxaforColor.Green, cancellationToken: ct);

        // The replay writes the state itself, so restoring it first would be a wasted report — and
        // a visible flash of the old color on the way to the new one.
        Assert.Equal(new[] { "SetColor(#00FF00,All)" }, second.Commands.Where(c => c.StartsWith("SetColor", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Reopen_AfterAnAnimation_HasNoColorToPutBack()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        Reopens(second);
        using var device = Wrap(first);
        await device.PlayPatternAsync(BuiltInPattern.Police, 2, ct);
        first.HasGoneAway = true;

        await Assert.ThrowsAsync<LuxaforDeviceDisconnectedException>(() =>
            device.PlayPatternAsync(BuiltInPattern.Police, 2, ct));

        // A pattern leaves no single resting color, so there is nothing to restore — and inventing
        // one would be worse than leaving the device as the hardware left it.
        Assert.Null(device.LastColor);
        Assert.DoesNotContain(second.Commands, c => c.StartsWith("SetColor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reopen_AfterAPerLedCommand_HasNoColorToPutBack()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        Reopens(second);
        using var device = Wrap(first);
        await device.SetColorAsync(LuxaforColor.Red, LedTarget.Led1, ct);
        first.HasGoneAway = true;

        await Assert.ThrowsAsync<LuxaforDeviceDisconnectedException>(() =>
            device.StrobeAsync(LuxaforColor.Red, 10, 3, cancellationToken: ct));

        Assert.Null(device.LastColor);
        Assert.DoesNotContain(second.Commands, c => c.StartsWith("SetColor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reopen_AsksTheNewDeviceToIdentifyItself()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        second.IdentifiesAs = new DeviceInfo(DeviceType.MuteButton, 4242);
        Reopens(second);
        using var device = Wrap(first);
        first.HasGoneAway = true;

        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);

        Assert.Equal(new DeviceInfo(DeviceType.MuteButton, 4242), device.DeviceInfo);
    }

    [Fact]
    public async Task DeviceInfo_SurvivesADeviceThatWillNotIdentifyItself()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        first.IdentifiesAs = new DeviceInfo(DeviceType.Standard, 1001);
        var second = Device();
        second.IdentifyError = new NotSupportedException("No feature report here.");
        Reopens(second);
        using var device = Wrap(first);
        await device.RequestDeviceInfoAsync(ct);
        first.HasGoneAway = true;

        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);

        // Going null under a caller who never asked for a different device would be a worse answer
        // than the last one that was true.
        Assert.Equal(new DeviceInfo(DeviceType.Standard, 1001), device.DeviceInfo);
    }

    [Fact]
    public async Task Descriptor_IsTheSameAcrossAReopen()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        Reopens(Device());
        using var device = Wrap(first);
        first.HasGoneAway = true;

        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);

        Assert.Equal(Descriptor, device.Descriptor);
    }

    #endregion

    #region Which device is reopened

    [Fact]
    public async Task Reopen_AsksForThatDeviceOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        Reopens(Device());
        using var device = Wrap(first);
        first.HasGoneAway = true;

        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);

        // Never Open(): a device that came back on another path is not provably the same hardware,
        // and a command meant for this one must not land on a different Luxafor.
        _manager.Verify(m => m.Open(Descriptor), Times.Once);
        _manager.Verify(m => m.Open(), Times.Never);
        _manager.Verify(m => m.Open(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task WhenTheReopenFails_TheCallerIsTold_AndTheNextCommandTriesAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var later = Device();
        var queue = new Queue<DeviceOpenResult>(new[]
        {
            DeviceOpenResult.Failure(DeviceOpenStatus.NotFound, Descriptor, null),
            DeviceOpenResult.Opened(later, Descriptor)
        });
        _manager.Setup(m => m.Open(Descriptor)).Returns(() => queue.Dequeue());
        using var device = Wrap(first);
        first.HasGoneAway = true;

        await Assert.ThrowsAsync<LuxaforDeviceDisconnectedException>(() =>
            device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct));
        Assert.False(device.IsConnected);

        // The device came back; nobody had to build a retry around the thing whose job is retrying.
        await device.SetColorAsync(LuxaforColor.Blue, cancellationToken: ct);
        Assert.Contains(later.Commands, c => c.StartsWith("SetColor(#0000FF", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentFailingCommands_ReopenOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        Reopens(second);
        using var device = Wrap(first);

        // Both commands are inside the device before either discovers it is dead.
        first.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.HasGoneAway = true;
        var one = device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);
        var two = device.SetColorAsync(LuxaforColor.Green, cancellationToken: ct);
        first.Gate.SetResult(true);
        await Task.WhenAll(one, two);

        // The losing command sees either the disconnect or the ObjectDisposedException from the
        // handle the winner closed under it — both are the same stale handle, and neither is the
        // caller's doing, so both end with the command landing on the new device.
        _manager.Verify(m => m.Open(Descriptor), Times.Once);
        Assert.Equal(2, second.Commands.Count(c => c.StartsWith("SetColor", StringComparison.Ordinal)));
        Assert.False(second.IsDisposed);
    }

    /// <summary>
    /// A command already inside the device when another command's reopen closes that handle sees an
    /// <see cref="ObjectDisposedException"/> — the exception that normally means "you disposed
    /// this". Here nobody outside disposed anything, so it is the same stale handle as any other
    /// and the command is carried over to the new device rather than blamed on the caller.
    /// </summary>
    [Fact]
    public async Task ACommandInFlightWhenAnotherReopens_LandsOnTheNewDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        var second = Device();
        Reopens(second);
        using var device = Wrap(first);

        // One command parked inside the old device, and the world moving on around it.
        first.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.GatedCommands = 1;
        first.HasGoneAway = true;
        var parked = device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);
        await device.SetColorAsync(LuxaforColor.Green, cancellationToken: ct);
        Assert.True(first.IsDisposed);

        first.Gate.SetResult(true);
        await parked;

        Assert.Contains(second.Commands, c => c.StartsWith("SetColor(#FF0000", StringComparison.Ordinal));
        _manager.Verify(m => m.Open(Descriptor), Times.Once);
    }

    /// <summary>
    /// The same thing when the wrapper is gone: disposal really was the caller letting go, and that
    /// has to reach them as itself rather than being retried against a device nobody owns.
    /// </summary>
    [Fact]
    public async Task ACommandInFlightWhenTheWrapperIsDisposed_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        Reopens(Device());
        var device = Wrap(first);

        first.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parked = device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);
        device.Dispose();
        first.Gate.SetResult(true);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => parked);
        _manager.Verify(m => m.Open(Descriptor), Times.Never);
    }

    [Fact]
    public async Task Reopen_IsLogged()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Device();
        Reopens(Device());
        using var device = Wrap(first);
        first.HasGoneAway = true;

        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);

        // Without a line, a device silently swapped underneath the application is invisible.
        Assert.Contains(_logger.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("Reopened", StringComparison.Ordinal));
    }

    #endregion

    #region Monitoring and lifetime

    [Fact]
    public async Task ObserveAsync_YieldsTheCurrentDevicesEvents_AndKeepsDeviceInfo()
    {
        var ct = TestContext.Current.CancellationToken;
        var inner = Device();
        var info = new DeviceInfo(DeviceType.Bluetooth, 7);
        inner.Events.Add(new LuxaforEvent.DeviceIdentified(info));
        inner.Events.Add(new LuxaforEvent.MuteButtonStateChanged(true));
        using var device = Wrap(inner);

        var seen = new List<LuxaforEvent>();
        await foreach (var evt in device.ObserveAsync(ct))
        {
            seen.Add(evt);
        }

        Assert.Equal(2, seen.Count);
        Assert.Equal(info, device.DeviceInfo);
    }

    [Fact]
    public void Dispose_DisposesTheDeviceItHolds()
    {
        var inner = Device();
        var device = Wrap(inner);

        device.Dispose();

        Assert.True(inner.IsDisposed);
        Assert.False(device.IsConnected);
    }

    [Fact]
    public async Task CommandsAfterDispose_Throw()
    {
        var device = Wrap(Device());
        device.Dispose();

        // Disposal is the caller letting go, not the device leaving, so it is not a reconnect.
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            device.SetColorAsync(LuxaforColor.Red, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_RejectsADeviceThatCannotBeFoundAgain()
    {
        // No descriptor means nothing to reopen, and a wrapper that cannot do the one thing it is
        // for should say so where it is built, not at the first disconnect.
        var ex = Assert.Throws<ArgumentException>(() => Wrap(new FakeLuxaforDevice()));

        Assert.Contains("descriptor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Opening

    [Fact]
    public void OpenReconnecting_WrapsTheOpenedDevice()
    {
        var inner = Device();
        _manager.Setup(m => m.Open()).Returns(DeviceOpenResult.Opened(inner, Descriptor));

        var result = _manager.Object.OpenReconnecting(_logger);

        Assert.True(result.IsSuccess);
        var wrapped = Assert.IsType<ReconnectingLuxaforDevice>(result.Device);
        Assert.Equal(Descriptor, result.Descriptor);
        Assert.Equal(Descriptor, wrapped.Descriptor);
    }

    [Fact]
    public void OpenReconnecting_ByDescriptor_WrapsThatDevice()
    {
        Reopens(Device());

        var result = _manager.Object.OpenReconnecting(Descriptor, _logger);

        Assert.IsType<ReconnectingLuxaforDevice>(result.Device);
    }

    [Fact]
    public void OpenReconnecting_PassesAFailureThrough()
    {
        var failure = DeviceOpenResult.Failure(DeviceOpenStatus.AccessDenied, Descriptor, new UnauthorizedAccessException());
        _manager.Setup(m => m.Open()).Returns(failure);

        var result = _manager.Object.OpenReconnecting();

        // There is nothing to reconnect to yet. Wrapping the failure would only move it to the
        // first command, where it is harder to explain.
        Assert.Same(failure, result);
    }

    [Fact]
    public void OpenReconnecting_RejectsNulls()
    {
        Assert.Throws<ArgumentNullException>(() => ((ILuxaforDeviceManager)null!).OpenReconnecting());
        Assert.Throws<ArgumentNullException>(() => _manager.Object.OpenReconnecting((LuxaforDeviceDescriptor)null!));
    }

    #endregion
}
