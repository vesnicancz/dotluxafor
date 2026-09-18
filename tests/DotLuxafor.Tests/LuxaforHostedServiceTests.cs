using System.Runtime.CompilerServices;
using DotLuxafor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DotLuxafor.Tests;

public class LuxaforHostedServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Mock<ILuxaforDeviceManager> _deviceManager = new();
    private readonly RecordingLogger _logger = new();

    public LuxaforHostedServiceTests()
    {
        _deviceManager
            .Setup(m => m.WaitForDeviceAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => Task.Delay(System.Threading.Timeout.Infinite, token));
    }

    private static readonly LuxaforDeviceDescriptor Descriptor =
        new LuxaforDeviceDescriptor("/dev/hidraw0", "LUXAFOR FLAG", "42");

    private static DeviceOpenResult Opened(ILuxaforDevice device) => DeviceOpenResult.Opened(device, Descriptor);

    /// <summary>
    /// Creates a connected device and materializes its proxy up front. Moq builds <c>Mock&lt;T&gt;.Object</c>
    /// lazily and without a lock, so leaving it to be created by the background loop and the test thread
    /// at the same time can yield two different proxies for the one mock.
    /// </summary>
    private static ILuxaforDevice ConnectedDevice(out Mock<ILuxaforDevice> mock)
    {
        mock = new Mock<ILuxaforDevice>();
        mock.Setup(d => d.IsConnected).Returns(true);
        return mock.Object;
    }

    private static DeviceOpenResult NotFound() => DeviceOpenResult.NotFound();

    private static DeviceOpenResult Failure(DeviceOpenStatus status) =>
        DeviceOpenResult.Failure(status, Descriptor, new IOException("The device is in use."));

    private LuxaforHostedService CreateService(LuxaforOptions? options = null)
    {
        options ??= new LuxaforOptions();
        return new LuxaforHostedService(
            _deviceManager.Object,
            Options.Create(options),
            _logger);
    }

    /// <summary>
    /// Waits for the background loop to finish on its own. Only valid without
    /// auto-reconnect, where ExecuteAsync returns after the first attempt.
    /// </summary>
    private static Task RunToCompletionAsync(LuxaforHostedService service, CancellationToken ct) =>
        service.ExecuteTask!.WaitAsync(Timeout, ct);

    /// <summary>
    /// Polls until the condition holds. Used where the service keeps looping,
    /// so there is no completion to await.
    /// </summary>
    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken ct,
        [CallerArgumentExpression(nameof(condition))] string? description = null)
    {
        var deadline = Environment.TickCount64 + (long)Timeout.TotalMilliseconds;

        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail($"Timed out after {Timeout.TotalSeconds}s waiting for: {description}");
            }

            await Task.Delay(10, ct);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoAutoReconnect_ExitsAfterFirstAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceManager.Setup(m => m.Open()).Returns(NotFound());
        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        // The loop has exited, so the count can no longer change.
        _deviceManager.Verify(m => m.Open(), Times.Once);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WithAutoReconnect_RetriesOnNoDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        var openCount = 0;
        _deviceManager.Setup(m => m.Open()).Returns(() =>
        {
            Interlocked.Increment(ref openCount);
            return NotFound();
        });

        var options = new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        };
        var service = CreateService(options);

        await service.StartAsync(ct);
        await WaitUntilAsync(() => Volatile.Read(ref openCount) >= 2, ct);
        await service.StopAsync(ct);

        _deviceManager.Verify(m => m.Open(), Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeviceFound_SetsCurrentDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = new Mock<ILuxaforDevice>();
        device.Setup(d => d.IsConnected).Returns(true);
        _deviceManager.Setup(m => m.Open()).Returns(Opened(device.Object));

        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        Assert.Same(device.Object, service.CurrentDevice);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WithAutoMonitor_CallsObserveAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = new Mock<ILuxaforDevice>();
        device.Setup(d => d.IsConnected).Returns(true);
        device.Setup(d => d.ObserveAsync(It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable());
        _deviceManager.Setup(m => m.Open()).Returns(Opened(device.Object));

        var options = new LuxaforOptions
        {
            AutoReconnect = false,
            AutoMonitor = true
        };
        var service = CreateService(options);

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);
        await service.StopAsync(ct);

        device.Verify(d => d.ObserveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DeviceDisconnectsAndReconnects()
    {
        var ct = TestContext.Current.CancellationToken;
        var device1 = new Mock<ILuxaforDevice>();
        var device2 = new Mock<ILuxaforDevice>();
        var device1ConnectedCalls = 0;
        device1.Setup(d => d.IsConnected).Returns(() => Interlocked.Increment(ref device1ConnectedCalls) <= 2);
        device2.Setup(d => d.IsConnected).Returns(true);

        var device1Disposals = 0;
        device1.Setup(d => d.Dispose()).Callback(() => Interlocked.Increment(ref device1Disposals));

        // Materialized here, not inside the Open callback, so the background loop and this thread
        // cannot race Moq into building two proxies for one mock.
        var device1Object = device1.Object;
        var device2Object = device2.Object;

        var openCount = 0;
        _deviceManager.Setup(m => m.Open()).Returns(() =>
            Opened(Interlocked.Increment(ref openCount) <= 1 ? device1Object : device2Object));

        var options = new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        };
        var service = CreateService(options);

        await service.StartAsync(ct);
        await WaitUntilAsync(
            () => Volatile.Read(ref openCount) >= 2 && Volatile.Read(ref device1Disposals) >= 1,
            ct);
        await service.StopAsync(ct);

        _deviceManager.Verify(m => m.Open(), Times.AtLeast(2));
        device1.Verify(d => d.Dispose(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Dispose_DisposesCurrentDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = new Mock<ILuxaforDevice>();
        device.Setup(d => d.IsConnected).Returns(true);
        _deviceManager.Setup(m => m.Open()).Returns(Opened(device.Object));

        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        service.Dispose();

        device.Verify(d => d.Dispose(), Times.AtLeastOnce);
    }

    #region Hotplug

    [Fact]
    public async Task ExecuteAsync_WhenNothingIsPluggedIn_WaitsForArrivalInsteadOfPolling()
    {
        var ct = TestContext.Current.CancellationToken;
        var waits = 0;
        _deviceManager.Setup(m => m.Open()).Returns(NotFound());
        _deviceManager
            .Setup(m => m.WaitForDeviceAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                Interlocked.Increment(ref waits);
                return Task.Delay(System.Threading.Timeout.Infinite, token);
            });

        var service = CreateService(new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        });

        await service.StartAsync(ct);
        await WaitUntilAsync(() => Volatile.Read(ref waits) >= 2, ct);
        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeviceIsPresentButUnopenable_DoesNotWaitForArrival()
    {
        var ct = TestContext.Current.CancellationToken;
        var openCount = 0;
        _deviceManager.Setup(m => m.Open()).Returns(() =>
        {
            Interlocked.Increment(ref openCount);
            return Failure(DeviceOpenStatus.AccessDenied);
        });

        var service = CreateService(new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        });

        await service.StartAsync(ct);
        await WaitUntilAsync(() => Volatile.Read(ref openCount) >= 3, ct);
        await service.StopAsync(ct);

        // The device is already attached, so an arrival wait would return at once and spin the loop.
        _deviceManager.Verify(m => m.WaitForDeviceAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenArrivalIsSignalled_RetriesWithoutWaitingForTheFallback()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = ConnectedDevice(out _);

        var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var devicePluggedIn = false;

        _deviceManager.Setup(m => m.Open()).Returns(() =>
            Volatile.Read(ref devicePluggedIn) ? Opened(device) : NotFound());
        _deviceManager
            .Setup(m => m.WaitForDeviceAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => arrived.Task.WaitAsync(token));

        // A fallback long enough that reaching it would fail the test's own timeout.
        var service = CreateService(new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMinutes(5)
        });

        await service.StartAsync(ct);
        await WaitUntilAsync(
            () => _deviceManager.Invocations.Any(i => i.Method.Name == nameof(ILuxaforDeviceManager.WaitForDeviceAsync)),
            ct);

        Volatile.Write(ref devicePluggedIn, true);
        arrived.SetResult(true);

        await WaitUntilAsync(() => service.CurrentDevice != null, ct);
        Assert.Same(device, service.CurrentDevice);

        await service.StopAsync(ct);
    }

    #endregion

    #region ILuxaforDeviceAccessor

    [Fact]
    public async Task Accessor_Current_IsNullUntilADeviceConnects()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceManager.Setup(m => m.Open()).Returns(NotFound());
        ILuxaforDeviceAccessor service = CreateService(new LuxaforOptions { AutoReconnect = false });

        Assert.Null(service.Current);

        await ((LuxaforHostedService)service).StartAsync(ct);
        await RunToCompletionAsync((LuxaforHostedService)service, ct);

        Assert.Null(service.Current);

        await ((LuxaforHostedService)service).StopAsync(ct);
    }

    [Fact]
    public async Task Accessor_Current_ExposesTheConnectedDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = ConnectedDevice(out _);
        _deviceManager.Setup(m => m.Open()).Returns(Opened(device));

        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        Assert.Same(device, ((ILuxaforDeviceAccessor)service).Current);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task Accessor_WaitForDeviceAsync_CompletesWhenTheDeviceConnects()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = ConnectedDevice(out _);

        var pluggedIn = false;
        _deviceManager.Setup(m => m.Open()).Returns(() =>
            Volatile.Read(ref pluggedIn) ? Opened(device) : NotFound());

        var service = CreateService(new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        });

        // The wait is registered before anything is connected, so it is genuinely pending.
        var waiting = service.WaitForDeviceAsync(ct);
        await service.StartAsync(ct);
        Assert.False(waiting.IsCompleted);

        Volatile.Write(ref pluggedIn, true);

        Assert.Same(device, await waiting.WaitAsync(Timeout, ct));

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task Accessor_WaitForDeviceAsync_WhenAlreadyConnected_CompletesImmediately()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = ConnectedDevice(out _);
        _deviceManager.Setup(m => m.Open()).Returns(Opened(device));

        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        var waiting = service.WaitForDeviceAsync(ct);

        Assert.Same(device, await waiting.WaitAsync(Timeout, ct));

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task Accessor_WaitForDeviceAsync_AfterDisconnect_WaitsForTheNextDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        var device1Mock = new Mock<ILuxaforDevice>();
        var device1ConnectedCalls = 0;
        device1Mock.Setup(d => d.IsConnected).Returns(() => Interlocked.Increment(ref device1ConnectedCalls) <= 1);
        var device1 = device1Mock.Object;
        var device2 = ConnectedDevice(out _);

        var openCount = 0;
        _deviceManager.Setup(m => m.Open()).Returns(() =>
            Opened(Interlocked.Increment(ref openCount) <= 1 ? device1 : device2));

        var service = CreateService(new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        });

        await service.StartAsync(ct);
        await WaitUntilAsync(() => ReferenceEquals(service.CurrentDevice, device2), ct);

        // The dead device must never be handed out again.
        Assert.Same(device2, await service.WaitForDeviceAsync(ct).WaitAsync(Timeout, ct));

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task Accessor_WaitForDeviceAsync_WhenCancelled_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceManager.Setup(m => m.Open()).Returns(NotFound());
        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var waiting = service.WaitForDeviceAsync(cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task Accessor_WaitForDeviceAsync_WhenServiceIsDisposed_ReleasesWaiters()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        var waiting = service.WaitForDeviceAsync(ct);
        Assert.False(waiting.IsCompleted);

        service.Dispose();

        // Nothing will ever connect now, so a waiter is released rather than left hanging.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(Timeout, ct));
    }

    #endregion

    [Fact]
    public async Task ExecuteAsync_WhenOpenThrows_LogsAndContinues()
    {
        var ct = TestContext.Current.CancellationToken;
        var callCount = 0;
        _deviceManager.Setup(m => m.Open()).Returns(() =>
        {
            if (Interlocked.Increment(ref callCount) <= 2)
            {
                throw new Exception("USB error");
            }
            return NotFound();
        });

        var options = new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        };
        var service = CreateService(options);

        await service.StartAsync(ct);

        // Retried past both throwing calls.
        await WaitUntilAsync(() => Volatile.Read(ref callCount) >= 3, ct);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WithAutoMonitor_LogsDeviceIdentifiedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = new Mock<ILuxaforDevice>();
        device.Setup(d => d.IsConnected).Returns(true);
        device.Setup(d => d.ObserveAsync(It.IsAny<CancellationToken>()))
            .Returns(EventsAsyncEnumerable(
                new LuxaforEvent.DeviceIdentified(new DeviceInfo(DeviceType.Bluetooth, 12345)),
                new LuxaforEvent.Disconnected()));
        _deviceManager.Setup(m => m.Open()).Returns(Opened(device.Object));

        var options = new LuxaforOptions
        {
            AutoReconnect = false,
            AutoMonitor = true
        };
        var service = CreateService(options);

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);
        await service.StopAsync(ct);

        device.Verify(d => d.ObserveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAutoReconnect_WithDevice_ExitsImmediately()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = new Mock<ILuxaforDevice>();
        device.Setup(d => d.IsConnected).Returns(true);
        _deviceManager.Setup(m => m.Open()).Returns(Opened(device.Object));

        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        _deviceManager.Verify(m => m.Open(), Times.Once);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeviceCannotBeOpened_LogsReasonOncePerStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var openCount = 0;
        _deviceManager.Setup(m => m.Open()).Returns(() =>
        {
            Interlocked.Increment(ref openCount);
            return Failure(DeviceOpenStatus.AccessDenied);
        });

        var options = new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        };
        var service = CreateService(options);

        await service.StartAsync(ct);
        await WaitUntilAsync(() => Volatile.Read(ref openCount) >= 3, ct);
        await service.StopAsync(ct);

        // The reason does not change between retries, so it is logged once, not once per attempt.
        var warnings = _logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains("denied access", warnings[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoDeviceIsConnected_DoesNotLogWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceManager.Setup(m => m.Open()).Returns(NotFound());

        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);
        await service.StopAsync(ct);

        // An unplugged device is normal, so it stays at debug level.
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Debug);
    }

    private static async IAsyncEnumerable<LuxaforEvent> EmptyAsyncEnumerable()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<LuxaforEvent> EventsAsyncEnumerable(params LuxaforEvent[] events)
    {
        foreach (var evt in events)
        {
            await Task.Yield();
            yield return evt;
        }
    }
}

/// <summary>Captures log entries so tests can assert on what the service reported.</summary>
internal sealed class RecordingLogger : ILogger<LuxaforHostedService>
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
