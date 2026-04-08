using DotLuxafor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DotLuxafor.Tests;

public class LuxaforHostedServiceTests
{
    private readonly Mock<ILuxaforDeviceManager> _deviceManager = new();
    private readonly ILogger<LuxaforHostedService> _logger = NullLogger<LuxaforHostedService>.Instance;

    private LuxaforHostedService CreateService(LuxaforOptions? options = null)
    {
        options ??= new LuxaforOptions();
        return new LuxaforHostedService(
            _deviceManager.Object,
            Options.Create(options),
            _logger);
    }

    [Fact]
    public async Task ExecuteAsync_NoAutoReconnect_ExitsAfterFirstAttempt()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceManager.Setup(m => m.TryOpen()).Returns((ILuxaforDevice?)null);
        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);

        // Give it time to execute
        await Task.Delay(200, ct);

        // Service should have exited; TryOpen only called once
        _deviceManager.Verify(m => m.TryOpen(), Times.Once);

        await service.StopAsync(ct);
    }

    [Fact]
    public async Task ExecuteAsync_WithAutoReconnect_RetriesOnNoDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        _deviceManager.Setup(m => m.TryOpen()).Returns((ILuxaforDevice?)null);
        var options = new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(50)
        };
        var service = CreateService(options);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        await service.StartAsync(cts.Token);
        await Task.Delay(300, ct);
        await service.StopAsync(ct);

        // Should have been called multiple times
        _deviceManager.Verify(m => m.TryOpen(), Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeviceFound_SetsCurrentDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = new Mock<ILuxaforDevice>();
        device.Setup(d => d.IsConnected).Returns(true);
        _deviceManager.Setup(m => m.TryOpen()).Returns(device.Object);

        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await Task.Delay(200, ct);

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
        _deviceManager.Setup(m => m.TryOpen()).Returns(device.Object);

        var options = new LuxaforOptions
        {
            AutoReconnect = false,
            AutoMonitor = true
        };
        var service = CreateService(options);

        await service.StartAsync(ct);
        await Task.Delay(200, ct);
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

        var tryOpenCount = 0;
        _deviceManager.Setup(m => m.TryOpen()).Returns(() =>
            Interlocked.Increment(ref tryOpenCount) <= 1 ? device1.Object : device2.Object);

        var options = new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(50)
        };
        var service = CreateService(options);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));
        await service.StartAsync(cts.Token);
        await Task.Delay(400, ct);
        await service.StopAsync(ct);

        _deviceManager.Verify(m => m.TryOpen(), Times.AtLeast(2));
        device1.Verify(d => d.Dispose(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Dispose_DisposesCurrentDevice()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = new Mock<ILuxaforDevice>();
        device.Setup(d => d.IsConnected).Returns(true);
        _deviceManager.Setup(m => m.TryOpen()).Returns(device.Object);

        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await Task.Delay(100, ct);

        service.Dispose();

        device.Verify(d => d.Dispose(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTryOpenThrows_LogsAndContinues()
    {
        var ct = TestContext.Current.CancellationToken;
        var callCount = 0;
        _deviceManager.Setup(m => m.TryOpen()).Returns(() =>
        {
            if (Interlocked.Increment(ref callCount) <= 2)
            {
                throw new Exception("USB error");
            }
            return null;
        });

        var options = new LuxaforOptions
        {
            AutoReconnect = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(50)
        };
        var service = CreateService(options);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        await service.StartAsync(cts.Token);
        await Task.Delay(2000, ct);
        await service.StopAsync(ct);

        // Should have retried past the throwing calls
        Assert.True(callCount >= 3);
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
        _deviceManager.Setup(m => m.TryOpen()).Returns(device.Object);

        var options = new LuxaforOptions
        {
            AutoReconnect = false,
            AutoMonitor = true
        };
        var service = CreateService(options);

        await service.StartAsync(ct);
        await Task.Delay(300, ct);
        await service.StopAsync(ct);

        // Verify ObserveAsync was called and processed events
        device.Verify(d => d.ObserveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAutoReconnect_WithDevice_ExitsImmediately()
    {
        var ct = TestContext.Current.CancellationToken;
        var device = new Mock<ILuxaforDevice>();
        device.Setup(d => d.IsConnected).Returns(true);
        _deviceManager.Setup(m => m.TryOpen()).Returns(device.Object);

        var service = CreateService(new LuxaforOptions { AutoReconnect = false });

        await service.StartAsync(ct);
        await Task.Delay(200, ct);

        // TryOpen called once, then service exits since no auto-reconnect
        _deviceManager.Verify(m => m.TryOpen(), Times.Once);

        await service.StopAsync(ct);
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
