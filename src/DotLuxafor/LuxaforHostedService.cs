#if NET8_0_OR_GREATER
using DotLuxafor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Background service that manages Luxafor device lifecycle,
/// including auto-reconnection and automatic monitoring.
/// </summary>
public sealed class LuxaforHostedService : BackgroundService
{
    private readonly ILuxaforDeviceManager _deviceManager;
    private readonly LuxaforOptions _options;
    private readonly ILogger<LuxaforHostedService> _logger;
    private ILuxaforDevice? _device;
    private Task? _monitorTask;
    private DeviceOpenStatus? _lastOpenFailure;

    /// <summary>
    /// Initializes a new instance of the <see cref="LuxaforHostedService"/> class.
    /// </summary>
    public LuxaforHostedService(
        ILuxaforDeviceManager deviceManager,
        IOptions<LuxaforOptions> options,
        ILogger<LuxaforHostedService> logger)
    {
        _deviceManager = deviceManager;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Gets the currently connected device, if any.
    /// </summary>
    public ILuxaforDevice? CurrentDevice => _device;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_device == null || !_device.IsConnected)
                {
                    await CleanupDeviceAsync().ConfigureAwait(false);
                    var openResult = _deviceManager.Open();
                    _device = openResult.Device;

                    if (_device != null)
                    {
                        _lastOpenFailure = null;
                        _logger.LogInformation("Luxafor device connected.");

                        if (_options.AutoMonitor)
                        {
                            _monitorTask = MonitorDeviceAsync(_device, stoppingToken);
                        }
                    }
                    else
                    {
                        LogOpenFailure(openResult);
                    }
                }

                if (!_options.AutoReconnect)
                {
                    return;
                }

                await Task.Delay(_options.ReconnectDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Luxafor hosted service.");
                await Task.Delay(_options.ReconnectDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Logs why the device could not be opened. Reconnect attempts repeat on a timer,
    /// so only a change of reason is logged to keep the log readable.
    /// </summary>
    private void LogOpenFailure(DeviceOpenResult result)
    {
        if (_lastOpenFailure == result.Status)
        {
            return;
        }

        _lastOpenFailure = result.Status;

        if (result.Status == DeviceOpenStatus.NotFound)
        {
            _logger.LogDebug("{Reason}", result.Description);
        }
        else
        {
            _logger.LogWarning(result.Error, "{Reason}", result.Description);
        }
    }

    private async Task MonitorDeviceAsync(ILuxaforDevice device, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var evt in device.ObserveAsync(stoppingToken).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case LuxaforEvent.Disconnected:
                        _logger.LogWarning("Luxafor device disconnected.");
                        return;

                    case LuxaforEvent.DeviceIdentified identified:
                        _logger.LogInformation("Luxafor device identified: {DeviceType}, Serial: {Serial}", identified.Info.Type, identified.Info.SerialNumber);
                        break;

                    case LuxaforEvent.ReadError error:
                        _logger.LogWarning(error.Exception, "Luxafor device read error.");
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in Luxafor device monitoring.");
        }
    }

    private async Task CleanupDeviceAsync()
    {
        if (_monitorTask != null)
        {
            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
            _monitorTask = null;
        }

        _device?.Dispose();
        _device = null;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _device?.Dispose();
        base.Dispose();
    }
}
#endif
