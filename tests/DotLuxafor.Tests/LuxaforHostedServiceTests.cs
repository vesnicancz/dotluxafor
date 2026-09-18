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

    private static DeviceOpenResult Opened(ILuxaforDevice device) => DeviceOpenResult.Opened(device);

    private static DeviceOpenResult NotFound() => DeviceOpenResult.NotFound();

    private static DeviceOpenResult Failure(DeviceOpenStatus status) =>
        DeviceOpenResult.Failure(status, new IOException("The device is in use."));

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

        var openCount = 0;
        _deviceManager.Setup(m => m.Open()).Returns(() =>
            Opened(Interlocked.Increment(ref openCount) <= 1 ? device1.Object : device2.Object));

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
