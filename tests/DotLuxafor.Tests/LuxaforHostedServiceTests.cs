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

    #region Liveness

    /// <summary>
    /// A device whose cable was pulled does not close the handle, so <c>IsConnected</c> goes on
    /// saying <c>true</c>. With monitoring off nothing else would notice, and before this the
    /// service would sit on the dead handle forever instead of reconnecting.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DeviceVanishedButHandleStillOpen_ReopensIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = ConnectedDeviceAt(Descriptor, out var mock);
        _deviceManager.Setup(m => m.Open()).Returns(DeviceOpenResult.Opened(device, Descriptor));
        _deviceManager.Setup(m => m.IsPresent(Descriptor)).Returns(false);

        var service = CreateService(new LuxaforOptions
        {
            AutoReconnect = true,
            AutoMonitor = false,
            ReconnectDelay = TimeSpan.FromMilliseconds(20)
        });

        await service.StartAsync(ct);
        await WaitUntilAsync(
            () => _deviceManager.Invocations.Count(i => i.Method.Name == nameof(ILuxaforDeviceManager.Open) && i.Arguments.Count == 0) >= 2,
            ct);

        // The handle it gave up on is closed rather than leaked.
        mock.Verify(d => d.Dispose(), Times.AtLeastOnce);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_DeviceStillAttached_IsKept()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = ConnectedDeviceAt(Descriptor, out var mock);
        _deviceManager.Setup(m => m.Open()).Returns(DeviceOpenResult.Opened(device, Descriptor));
        _deviceManager.Setup(m => m.IsPresent(Descriptor)).Returns(true);

        var service = CreateService(new LuxaforOptions
        {
            AutoReconnect = true,
            AutoMonitor = false,
            ReconnectDelay = TimeSpan.FromMilliseconds(20)
        });

        await service.StartAsync(ct);

        // Two liveness checks means two full passes of the loop, both of which kept the device.
        await WaitUntilAsync(
            () => _deviceManager.Invocations.Count(i => i.Method.Name == nameof(ILuxaforDeviceManager.IsPresent)) >= 2,
            ct);

        _deviceManager.Verify(m => m.Open(), Times.Once);
        mock.Verify(d => d.Dispose(), Times.Never);
        Assert.Same(device, service.CurrentDevice);

        await service.StopAsync(ct);
    }

    /// <summary>
    /// A device opened outside discovery has no descriptor, so there is nothing to look for in the
    /// device list and the handle has to be taken at its word — not dropped as if it had vanished.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DeviceWithoutDescriptor_IsKeptWithoutAsking()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = ConnectedDevice(out var mock);
        _deviceManager.Setup(m => m.Open()).Returns(DeviceOpenResult.Opened(device, Descriptor));

        var service = CreateService(new LuxaforOptions
        {
            AutoReconnect = true,
            AutoMonitor = false,
            ReconnectDelay = TimeSpan.FromMilliseconds(20)
        });

        await service.StartAsync(ct);
        await WaitUntilAsync(() => mock.Invocations.Count(i => i.Method.Name == "get_IsConnected") >= 2, ct);

        _deviceManager.Verify(m => m.Open(), Times.Once);
        _deviceManager.Verify(m => m.IsPresent(It.IsAny<LuxaforDeviceDescriptor>()), Times.Never);
        Assert.Same(device, service.CurrentDevice);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_DeviceVanished_IsLogged()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceManager.Setup(m => m.Open()).Returns(DeviceOpenResult.Opened(ConnectedDeviceAt(Descriptor, out _), Descriptor));
        _deviceManager.Setup(m => m.IsPresent(Descriptor)).Returns(false);

        var service = CreateService(new LuxaforOptions
        {
            AutoReconnect = true,
            AutoMonitor = false,
            ReconnectDelay = TimeSpan.FromMilliseconds(20)
        });

        await service.StartAsync(ct);

        // Without this line the reconnect that follows looks like it came out of nowhere, and with
        // monitoring off it is the only report of the disconnect there is.
        await WaitUntilAsync(
            () => _logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("no longer attached", StringComparison.Ordinal)),
            ct);

        await service.StopAsync(ct);
    }

    #endregion

    #region Device selection

    private static readonly LuxaforDeviceDescriptor Other =
        new LuxaforDeviceDescriptor("/dev/hidraw1", "LUXAFOR FLAG", "99");

    /// <summary>
    /// Lets the manager enumerate the given devices and open any of them, so a test only has to
    /// say what is attached and then check which one the service picked.
    /// </summary>
    private void Attach(params LuxaforDeviceDescriptor[] descriptors)
    {
        _deviceManager.Setup(m => m.List()).Returns(descriptors);
        _deviceManager
            .Setup(m => m.Open(It.IsAny<LuxaforDeviceDescriptor>()))
            .Returns((LuxaforDeviceDescriptor d) => DeviceOpenResult.Opened(ConnectedDeviceAt(d), d));

        // The service rechecks that the device it holds is still attached, so a manager that says
        // nothing is would have it drop and reopen a perfectly good device on every tick.
        _deviceManager
            .Setup(m => m.IsPresent(It.IsAny<LuxaforDeviceDescriptor>()))
            .Returns((LuxaforDeviceDescriptor d) => descriptors.Contains(d));
    }

    /// <summary>
    /// A connected device that reports the descriptor it was opened as, which is how a test asks
    /// the service which of several devices it settled on.
    /// </summary>
    private static ILuxaforDevice ConnectedDeviceAt(LuxaforDeviceDescriptor descriptor)
        => ConnectedDeviceAt(descriptor, out _);

    private static ILuxaforDevice ConnectedDeviceAt(LuxaforDeviceDescriptor descriptor, out Mock<ILuxaforDevice> mock)
    {
        var device = ConnectedDevice(out mock);
        mock.Setup(d => d.Descriptor).Returns(descriptor);
        return device;
    }

    [Fact]
    public async Task ExecuteAsync_WithNoSelector_OpensWhicheverComesFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceManager.Setup(m => m.Open()).Returns(Opened(ConnectedDevice(out _)));
        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        // The unfiltered path stays as it was: one call, no listing.
        _deviceManager.Verify(m => m.Open(), Times.Once);
        _deviceManager.Verify(m => m.List(), Times.Never);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WithSerialNumber_OpensThatDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        Attach(Other, Descriptor);
        var service = CreateService(new LuxaforOptions { SerialNumber = "42" });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        Assert.Equal(Descriptor, service.CurrentDevice?.Descriptor);
        _deviceManager.Verify(m => m.Open(), Times.Never);

        await service.StopAsync(ct);
    }

    /// <summary>
    /// A serial number gets copied off a label or out of a log, where its case is nobody's choice.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithSerialNumber_MatchesIgnoringCase()
    {
        var ct = TestContext.Current.CancellationToken;
        var hex = new LuxaforDeviceDescriptor("/dev/hidraw2", "LUXAFOR FLAG", "00ab12CD");
        Attach(hex);
        var service = CreateService(new LuxaforOptions { SerialNumber = "00AB12cd" });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        Assert.Equal(hex, service.CurrentDevice?.Descriptor);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WithDevicePath_OpensThatDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        Attach(Other, Descriptor);
        var service = CreateService(new LuxaforOptions { DevicePath = Descriptor.DevicePath });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        Assert.Equal(Descriptor, service.CurrentDevice?.Descriptor);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WithSelectDevice_OpensWhateverTheCallbackPicks()
    {
        var ct = TestContext.Current.CancellationToken;
        Attach(Descriptor, Other);
        var service = CreateService(new LuxaforOptions
        {
            SelectDevice = devices => devices.LastOrDefault()
        });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        Assert.Equal(Other, service.CurrentDevice?.Descriptor);

        await service.StopAsync(ct);
    }

    /// <summary>
    /// The callback exists to choose among real devices, so it is spared the empty case — which is
    /// also the case the service handles itself, by waiting for something to be plugged in.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithSelectDevice_IsNotCalledWhenNothingIsAttached()
    {
        var ct = TestContext.Current.CancellationToken;
        var called = false;
        _deviceManager.Setup(m => m.List()).Returns(Array.Empty<LuxaforDeviceDescriptor>());
        var service = CreateService(new LuxaforOptions
        {
            SelectDevice = _ =>
            {
                called = true;
                return null;
            }
        });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        Assert.False(called);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoAttachedDeviceMatches_DoesNotOpenAnything()
    {
        var ct = TestContext.Current.CancellationToken;
        Attach(Other);
        var service = CreateService(new LuxaforOptions { SerialNumber = "42" });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);

        // Falling back to the wrong device would be worse than showing nothing at all.
        Assert.Null(service.CurrentDevice);
        _deviceManager.Verify(m => m.Open(It.IsAny<LuxaforDeviceDescriptor>()), Times.Never);
        _deviceManager.Verify(m => m.Open(), Times.Never);

        await service.StopAsync(ct);
    }

    /// <summary>
    /// The wrong device being attached is not the same as nothing being attached: waiting for an
    /// arrival would return at once, because the devices are already here, and spin the loop.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenNoAttachedDeviceMatches_DoesNotWaitForArrival()
    {
        var ct = TestContext.Current.CancellationToken;
        var listCount = 0;
        _deviceManager.Setup(m => m.List()).Returns(() =>
        {
            Interlocked.Increment(ref listCount);
            return new[] { Other };
        });

        var service = CreateService(new LuxaforOptions
        {
            SerialNumber = "42",
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        });

        await service.StartAsync(ct);
        await WaitUntilAsync(() => Volatile.Read(ref listCount) >= 3, ct);
        await service.StopAsync(ct);

        _deviceManager.Verify(m => m.WaitForDeviceAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Nothing attached is still the ordinary case a hotplug wait is for, selector or not.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithSelectorAndNothingAttached_WaitsForArrival()
    {
        var ct = TestContext.Current.CancellationToken;
        var waits = 0;
        _deviceManager.Setup(m => m.List()).Returns(Array.Empty<LuxaforDeviceDescriptor>());
        _deviceManager
            .Setup(m => m.WaitForDeviceAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                Interlocked.Increment(ref waits);
                return Task.Delay(System.Threading.Timeout.Infinite, token);
            });

        var service = CreateService(new LuxaforOptions
        {
            SerialNumber = "42",
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        });

        await service.StartAsync(ct);
        await WaitUntilAsync(() => Volatile.Read(ref waits) >= 2, ct);
        await service.StopAsync(ct);
    }

    /// <summary>
    /// A selector that matches nothing is nearly always a typo in the configuration, so it must not
    /// be buried at Debug the way "nobody has plugged anything in yet" is. The message names what
    /// is attached, which is what makes the typo fixable from the log alone.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenNoAttachedDeviceMatches_WarnsAndNamesTheAttachedDevices()
    {
        var ct = TestContext.Current.CancellationToken;
        Attach(Other);
        var service = CreateService(new LuxaforOptions { SerialNumber = "42" });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);
        await service.StopAsync(ct);

        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("serial number '42'", warning.Message);
        Assert.Contains(Other.ToString(), warning.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithNothingAttached_StaysAtDebug()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceManager.Setup(m => m.List()).Returns(Array.Empty<LuxaforDeviceDescriptor>());
        var service = CreateService(new LuxaforOptions { SerialNumber = "42" });

        await service.StartAsync(ct);
        await RunToCompletionAsync(service, ct);
        await service.StopAsync(ct);

        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Debug);
    }

    /// <summary>
    /// A device that arrives after the wrong one was rejected has to be picked up — the earlier
    /// mismatch must not be remembered as a decision.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTheMatchingDeviceArrivesLater_ConnectsToIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var attached = new[] { Other };
        var listCount = 0;
        _deviceManager.Setup(m => m.List()).Returns(() =>
        {
            Interlocked.Increment(ref listCount);
            return Volatile.Read(ref attached);
        });
        _deviceManager
            .Setup(m => m.Open(It.IsAny<LuxaforDeviceDescriptor>()))
            .Returns((LuxaforDeviceDescriptor d) => DeviceOpenResult.Opened(ConnectedDeviceAt(d), d));

        var service = CreateService(new LuxaforOptions
        {
            SerialNumber = "42",
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(10)
        });

        await service.StartAsync(ct);

        // Let it reject the wrong device a couple of times before the right one shows up.
        await WaitUntilAsync(() => Volatile.Read(ref listCount) >= 2, ct);
        Assert.Null(service.CurrentDevice);

        Volatile.Write(ref attached, new[] { Other, Descriptor });
        await WaitUntilAsync(() => service.CurrentDevice != null, ct);

        Assert.Equal(Descriptor, service.CurrentDevice?.Descriptor);

        await service.StopAsync(ct);
    }

    #endregion

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
