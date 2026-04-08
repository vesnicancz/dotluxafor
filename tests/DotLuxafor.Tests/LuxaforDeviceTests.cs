using System.IO;
using DotLuxafor;
using Moq;

namespace DotLuxafor.Tests;

public class LuxaforDeviceTests
{
    private readonly Mock<IHidStreamAdapter> _stream = new();

    private LuxaforDevice CreateDevice()
    {
        _stream.Setup(s => s.CanWrite).Returns(true);
        _stream.Setup(s => s.CanRead).Returns(true);
        return new LuxaforDevice(_stream.Object);
    }

    #region IsConnected

    [Fact]
    public void IsConnected_WhenStreamCanWrite_ReturnsTrue()
    {
        _stream.Setup(s => s.CanWrite).Returns(true);
        var device = new LuxaforDevice(_stream.Object);

        Assert.True(device.IsConnected);
    }

    [Fact]
    public void IsConnected_WhenStreamCannotWrite_ReturnsFalse()
    {
        _stream.Setup(s => s.CanWrite).Returns(false);
        var device = new LuxaforDevice(_stream.Object);

        Assert.False(device.IsConnected);
    }

    [Fact]
    public void IsConnected_AfterDispose_ReturnsFalse()
    {
        var device = CreateDevice();

        device.Dispose();

        Assert.False(device.IsConnected);
    }

    [Fact]
    public void IsConnected_WhenStreamThrowsObjectDisposed_ReturnsFalse()
    {
        _stream.Setup(s => s.CanWrite).Throws(new ObjectDisposedException("stream"));
        var device = new LuxaforDevice(_stream.Object);

        Assert.False(device.IsConnected);
    }

    #endregion

    #region SendReport Format

    [Fact]
    public async Task SetColorAsync_SendsCorrectReport()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        byte[]? sent = null;
        _stream.Setup(s => s.Write(It.IsAny<byte[]>())).Callback<byte[]>(b => sent = (byte[])b.Clone());

        await device.SetColorAsync(new LuxaforColor(0xAA, 0xBB, 0xCC), LedTarget.TopSide, ct);

        Assert.NotNull(sent);
        Assert.Equal(LuxaforDevice.ReportLength, sent.Length);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x41, 0xAA, 0xBB, 0xCC, 0x00, 0x00, 0x00 }, sent);
    }

    [Fact]
    public async Task FadeToAsync_SendsCorrectReport()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        byte[]? sent = null;
        _stream.Setup(s => s.Write(It.IsAny<byte[]>())).Callback<byte[]>(b => sent = (byte[])b.Clone());

        await device.FadeToAsync(new LuxaforColor(0x10, 0x20, 0x30), speed: 50, LedTarget.Led3, ct);

        Assert.NotNull(sent);
        Assert.Equal(new byte[] { 0x00, 0x02, 0x03, 0x10, 0x20, 0x30, 50, 0x00, 0x00 }, sent);
    }

    [Fact]
    public async Task StrobeAsync_SendsCorrectReport()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        byte[]? sent = null;
        _stream.Setup(s => s.Write(It.IsAny<byte[]>())).Callback<byte[]>(b => sent = (byte[])b.Clone());

        await device.StrobeAsync(LuxaforColor.Red, speed: 10, repeat: 5, LedTarget.BottomSide, ct);

        Assert.NotNull(sent);
        Assert.Equal(new byte[] { 0x00, 0x03, 0x42, 255, 0, 0, 10, 5, 0x00 }, sent);
    }

    [Fact]
    public async Task WaveAsync_SendsCorrectReport()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        byte[]? sent = null;
        _stream.Setup(s => s.Write(It.IsAny<byte[]>())).Callback<byte[]>(b => sent = (byte[])b.Clone());

        await device.WaveAsync(WaveType.Smooth, LuxaforColor.Green, speed: 20, repeat: 3, ct);

        Assert.NotNull(sent);
        // Wave: [0x00, 0x04, waveType, R, G, B, repeat, speed, 0x00]
        Assert.Equal(new byte[] { 0x00, 0x04, 5, 0, 255, 0, 3, 20, 0x00 }, sent);
    }

    [Fact]
    public async Task PlayPatternAsync_SendsCorrectReport()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        byte[]? sent = null;
        _stream.Setup(s => s.Write(It.IsAny<byte[]>())).Callback<byte[]>(b => sent = (byte[])b.Clone());

        await device.PlayPatternAsync(BuiltInPattern.Police, repeat: 7, ct);

        Assert.NotNull(sent);
        Assert.Equal(new byte[] { 0x00, 0x06, 5, 7, 0x00, 0x00, 0x00, 0x00, 0x00 }, sent);
    }

    [Fact]
    public async Task TurnOffAsync_SendsSetColorWithZeros()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        byte[]? sent = null;
        _stream.Setup(s => s.Write(It.IsAny<byte[]>())).Callback<byte[]>(b => sent = (byte[])b.Clone());

        await device.TurnOffAsync(ct);

        Assert.NotNull(sent);
        Assert.Equal(new byte[] { 0x00, 0x01, 0xFF, 0, 0, 0, 0x00, 0x00, 0x00 }, sent);
    }

    [Fact]
    public async Task RequestDeviceInfoAsync_UsesGetFeatureWhenAvailable()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        _stream.Setup(s => s.GetFeature(It.IsAny<byte[]>())).Callback<byte[]>(b =>
        {
            var report = new byte[] { 0x00, 0x80, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x00 };
            Array.Copy(report, b, report.Length);
        });

        await device.RequestDeviceInfoAsync(ct);

        Assert.NotNull(device.DeviceInfo);
        Assert.Equal(DeviceType.Standard, device.DeviceInfo.Value.Type);
        Assert.Equal(0x002A, device.DeviceInfo.Value.SerialNumber);
    }

    [Fact]
    public async Task RequestDeviceInfoAsync_FallsBackToDescriptorWhenGetFeatureFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        _stream.Setup(s => s.GetFeature(It.IsAny<byte[]>())).Throws<IOException>();
        _stream.Setup(s => s.GetProductName()).Returns("LUXAFOR BT");
        _stream.Setup(s => s.GetDeviceSerialNumber()).Returns("12345");

        await device.RequestDeviceInfoAsync(ct);

        Assert.NotNull(device.DeviceInfo);
        Assert.Equal(DeviceType.Bluetooth, device.DeviceInfo.Value.Type);
        Assert.Equal(12345L, device.DeviceInfo.Value.SerialNumber);
    }

    [Fact]
    public async Task RequestDeviceInfoAsync_FallsBackToDescriptorWhenGetFeatureReturnsUnrecognizedData()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        // GetFeature returns data that ParseReport doesn't recognize
        _stream.Setup(s => s.GetFeature(It.IsAny<byte[]>()));
        _stream.Setup(s => s.GetProductName()).Returns("LUXAFOR FLAG");
        _stream.Setup(s => s.GetDeviceSerialNumber()).Returns((string?)null);

        await device.RequestDeviceInfoAsync(ct);

        Assert.NotNull(device.DeviceInfo);
        Assert.Equal(DeviceType.Standard, device.DeviceInfo.Value.Type);
        Assert.Equal(0L, device.DeviceInfo.Value.SerialNumber);
    }

    #endregion

    #region Command Error Handling

    [Fact]
    public async Task SendReportAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        device.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct));
    }

    [Fact]
    public async Task SendReportAsync_WhenStreamCannotWrite_ThrowsInvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        _stream.Setup(s => s.CanWrite).Returns(false);
        var device = new LuxaforDevice(_stream.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct));
    }

    [Fact]
    public async Task SendReportAsync_RespectsExpectedCancellation()
    {
        var device = CreateDevice();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            device.SetColorAsync(LuxaforColor.Red, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RequestDeviceInfoAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        device.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            device.RequestDeviceInfoAsync(ct));
    }

    #endregion

    #region ObserveAsync

    [Fact]
    public async Task ObserveAsync_ReceivesMuteButtonEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        var report = new byte[] { 0x00, 0x83, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var readCount = 0;

        _stream.Setup(s => s.Read(It.IsAny<byte[]>(), 0, LuxaforDevice.ReportLength))
            .Returns((byte[] buf, int _, int _) =>
            {
                if (Interlocked.Increment(ref readCount) == 1)
                {
                    Array.Copy(report, buf, report.Length);
                    return LuxaforDevice.ReportLength;
                }
                throw new TimeoutException();
            });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        var events = new List<LuxaforEvent>();

        await foreach (var evt in device.ObserveAsync(cts.Token))
        {
            events.Add(evt);
            if (events.Count >= 1)
            {
                break;
            }
        }

        var mute = Assert.Single(events);
        var muteEvt = Assert.IsType<LuxaforEvent.MuteButtonStateChanged>(mute);
        Assert.True(muteEvt.IsPressed);
    }

    [Fact]
    public async Task ObserveAsync_EmitsDisconnectedOnIOException()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        _stream.Setup(s => s.Read(It.IsAny<byte[]>(), 0, LuxaforDevice.ReportLength))
            .Throws(new IOException("USB disconnected"));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        var events = new List<LuxaforEvent>();

        await foreach (var evt in device.ObserveAsync(cts.Token))
        {
            events.Add(evt);
        }

        var disconnected = Assert.Single(events);
        Assert.IsType<LuxaforEvent.Disconnected>(disconnected);
    }

    [Fact]
    public async Task ObserveAsync_EmitsReadErrorOnUnexpectedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        var readCount = 0;
        var testException = new InvalidOperationException("unexpected");

        _stream.Setup(s => s.Read(It.IsAny<byte[]>(), 0, LuxaforDevice.ReportLength))
            .Returns((byte[] _, int _, int _) =>
            {
                if (Interlocked.Increment(ref readCount) == 1)
                {
                    throw testException;
                }
                throw new TimeoutException();
            });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        var events = new List<LuxaforEvent>();

        await foreach (var evt in device.ObserveAsync(cts.Token))
        {
            events.Add(evt);
            if (events.Count >= 1)
            {
                break;
            }
        }

        var error = Assert.Single(events);
        var readError = Assert.IsType<LuxaforEvent.ReadError>(error);
        Assert.Same(testException, readError.Exception);
    }

    [Fact]
    public async Task ObserveAsync_UpdatesDeviceInfoOnIdentifiedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        // Standard device, serial 0x0102
        var report = new byte[] { 0x00, 0x80, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00 };
        var readCount = 0;

        _stream.Setup(s => s.Read(It.IsAny<byte[]>(), 0, LuxaforDevice.ReportLength))
            .Returns((byte[] buf, int _, int _) =>
            {
                if (Interlocked.Increment(ref readCount) == 1)
                {
                    Array.Copy(report, buf, report.Length);
                    return LuxaforDevice.ReportLength;
                }
                throw new TimeoutException();
            });

        Assert.Null(device.DeviceInfo);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        await foreach (var evt in device.ObserveAsync(cts.Token))
        {
            if (evt is LuxaforEvent.DeviceIdentified)
            {
                break;
            }
        }

        Assert.NotNull(device.DeviceInfo);
        Assert.Equal(DeviceType.Standard, device.DeviceInfo.Value.Type);
        Assert.Equal(258L, device.DeviceInfo.Value.SerialNumber);
    }

    [Fact]
    public async Task ObserveAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        device.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in device.ObserveAsync(ct))
            {
            }
        });
    }

    [Fact]
    public async Task ObserveAsync_SecondConcurrentCall_ThrowsInvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        // Make reads block forever with timeouts
        _stream.Setup(s => s.Read(It.IsAny<byte[]>(), 0, LuxaforDevice.ReportLength))
            .Throws(new TimeoutException());

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        var firstConsumer = Task.Run(async () =>
        {
            await foreach (var _ in device.ObserveAsync(cts.Token))
            {
            }
        }, cts.Token);

        // Give the first consumer time to start the read loop
        await Task.Delay(200, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in device.ObserveAsync(cts.Token))
            {
            }
        });

        cts.Cancel();
        try { await firstConsumer; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ObserveAsync_AfterFirstCompletes_SecondCallSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        _stream.Setup(s => s.Read(It.IsAny<byte[]>(), 0, LuxaforDevice.ReportLength))
            .Throws(new IOException("disconnected"));

        // First consumer
        await foreach (var _ in device.ObserveAsync(ct))
        {
        }

        // Second consumer should work
        var events = new List<LuxaforEvent>();
        await foreach (var evt in device.ObserveAsync(ct))
        {
            events.Add(evt);
        }

        Assert.Single(events);
        Assert.IsType<LuxaforEvent.Disconnected>(events[0]);
    }

    [Fact]
    public async Task ObserveAsync_PartialRead_IsIgnored_ThenReceivesFullReport()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        var muteReport = new byte[] { 0x00, 0x83, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var readCount = 0;

        _stream.Setup(s => s.Read(It.IsAny<byte[]>(), 0, LuxaforDevice.ReportLength))
            .Returns((byte[] buf, int _, int _) =>
            {
                var call = Interlocked.Increment(ref readCount);
                if (call == 1)
                {
                    // Partial read — only 4 bytes
                    return 4;
                }
                if (call == 2)
                {
                    // Full report
                    Array.Copy(muteReport, buf, muteReport.Length);
                    return LuxaforDevice.ReportLength;
                }
                throw new TimeoutException();
            });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        var events = new List<LuxaforEvent>();

        await foreach (var evt in device.ObserveAsync(cts.Token))
        {
            events.Add(evt);
            if (events.Count >= 1)
            {
                break;
            }
        }

        // Partial read was skipped, only the full report came through
        var mute = Assert.Single(events);
        Assert.IsType<LuxaforEvent.MuteButtonStateChanged>(mute);
    }

    [Fact]
    public async Task ObserveAsync_WhenStreamCannotRead_EmitsDisconnectedImmediately()
    {
        var ct = TestContext.Current.CancellationToken;
        _stream.Setup(s => s.CanWrite).Returns(true);
        _stream.Setup(s => s.CanRead).Returns(false);
        var device = new LuxaforDevice(_stream.Object);

        var events = new List<LuxaforEvent>();
        await foreach (var evt in device.ObserveAsync(ct))
        {
            events.Add(evt);
        }

        var disconnected = Assert.Single(events);
        Assert.IsType<LuxaforEvent.Disconnected>(disconnected);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_DisposesStream()
    {
        var device = CreateDevice();

        device.Dispose();

        _stream.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_CalledTwice_OnlyDisposesStreamOnce()
    {
        var device = CreateDevice();

        device.Dispose();
        device.Dispose();

        _stream.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_DisposesStream()
    {
        var device = CreateDevice();

        await device.DisposeAsync();

        _stream.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Dispose_ConcurrentCalls_OnlyOneDisposesStream()
    {
        var device = CreateDevice();
        var barrier = new Barrier(10);

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            device.Dispose();
        })).ToArray();

        await Task.WhenAll(tasks);

        _stream.Verify(s => s.Dispose(), Times.Once);
    }

    #endregion

    #region Thread Safety

    [Fact]
    public async Task ConcurrentCommands_AllSucceed()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = CreateDevice();
        var writeCount = 0;
        _stream.Setup(s => s.Write(It.IsAny<byte[]>())).Callback<byte[]>(_ => Interlocked.Increment(ref writeCount));

        var tasks = Enumerable.Range(0, 20).Select(i =>
            device.SetColorAsync(new LuxaforColor((byte)i, 0, 0), cancellationToken: ct)).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(20, writeCount);
    }

    [Fact]
    #pragma warning disable xUnit1051 // Test intentionally uses separate CancellationTokenSources for semaphore contention scenario
    public async Task SendReportAsync_CancellationAfterSemaphoreAcquire_DoesNotWrite()
    {
        var device = CreateDevice();
        using var cts = new CancellationTokenSource();

        // First call acquires the semaphore, cancel while it holds it
        var semaphoreHeld = new TaskCompletionSource<bool>();
        var proceed = new TaskCompletionSource<bool>();

        _stream.Setup(s => s.Write(It.IsAny<byte[]>())).Callback<byte[]>(_ =>
        {
            semaphoreHeld.SetResult(true);
            proceed.Task.Wait();
        });

        // Start a long-running command that holds the semaphore
        var firstCmd = Task.Run(async () =>
            await device.SetColorAsync(LuxaforColor.Red, cancellationToken: CancellationToken.None));

        await semaphoreHeld.Task;

        // Cancel while waiting for semaphore
        cts.Cancel();
        var secondCmd = device.SetColorAsync(LuxaforColor.Blue, cancellationToken: cts.Token);

        proceed.SetResult(true);
        await firstCmd;

        // Second command should have been cancelled
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondCmd);

        // Only one write should have happened
        _stream.Verify(s => s.Write(It.IsAny<byte[]>()), Times.Once);
    }
    #pragma warning restore xUnit1051

    #endregion
}
