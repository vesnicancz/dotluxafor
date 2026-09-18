using Avalonia.Threading;
using DotLuxafor;

namespace DotLuxafor.ControlPanel.Services;

public sealed class DeviceService : IDisposable
{
    private readonly ILuxaforDeviceManager _manager = new LuxaforDeviceManager();
    private ILuxaforDevice? _device;
    private CancellationTokenSource? _monitorCts;
    private Timer? _reconnectTimer;
    private bool _autoReconnect;
    private bool _disposed;
    private DeviceOpenStatus? _lastOpenFailure;

    /// <summary>
    /// Whether the app currently holds a device.
    /// </summary>
    /// <remarks>
    /// Deliberately the cheap check. <see cref="ILuxaforConnection.IsConnected"/> only says the
    /// handle was not closed, so on its own it would go on claiming a device that was unplugged —
    /// but everything here that finds out the device is gone (the monitor's
    /// <see cref="LuxaforEvent.Disconnected"/>, a command that throws, the reconnect timer) drops
    /// the handle, which is what makes this answer honest. Probing the device list on every state
    /// change would put a device enumeration on the UI thread for no gain.
    /// </remarks>
    public bool IsConnected => _device?.IsConnected == true;

    public event Action<string>? LogMessage;
    public event Action? StateChanged;

    public DeviceInfo? DeviceInfo { get; private set; }
    public DongleInfo? DongleInfo { get; private set; }

    public bool Connect()
    {
        // Not IsConnected: pressing Connect is exactly what someone does after unplugging and
        // replugging the device, and the stale handle would make this a no-op that reconnects
        // nothing. Here the enumeration it costs is free — this runs on a button press.
        if (IsDeviceLive())
        {
            return true;
        }

        DropDevice();
        var openResult = _manager.Open();
        _device = openResult.Device;

        if (_device is null)
        {
            _lastOpenFailure = openResult.Status;
            Log(openResult.Description);
            return false;
        }

        _lastOpenFailure = null;
        StartMonitoring(_device);

        _ = RequestDeviceInfoAsync(_device);

        _autoReconnect = true;
        StopReconnectTimer();
        Log("Connected");
        OnStateChanged();
        return true;
    }

    public void Disconnect()
    {
        _autoReconnect = false;
        StopReconnectTimer();
        CloseDevice();
        DeviceInfo = null;
        DongleInfo = null;
        Log("Disconnected");
        OnStateChanged();
    }

    public void SetColor(LedTarget target, byte r, byte g, byte b)
    {
        ExecuteCommandAsync(d => d.SetColorAsync(new LuxaforColor(r, g, b), target));
    }

    public void FadeTo(LedTarget target, byte r, byte g, byte b, byte speed)
    {
        ExecuteCommandAsync(d => d.FadeToAsync(new LuxaforColor(r, g, b), speed, target));
    }

    public void Strobe(LedTarget target, byte r, byte g, byte b, byte speed, byte repeat)
    {
        ExecuteCommandAsync(d => d.StrobeAsync(new LuxaforColor(r, g, b), speed, repeat, target));
    }

    public void Wave(WaveType type, byte r, byte g, byte b, byte speed, byte repeat)
    {
        ExecuteCommandAsync(d => d.WaveAsync(type, new LuxaforColor(r, g, b), speed, repeat));
    }

    public void PlayPattern(BuiltInPattern pattern, byte repeat)
    {
        ExecuteCommandAsync(d => d.PlayPatternAsync(pattern, repeat));
    }

    public void TurnOff()
    {
        ExecuteCommandAsync(d => d.TurnOffAsync());
    }

    private async void ExecuteCommandAsync(Func<ILuxaforDevice, Task> action, bool retryOnDisconnect = true)
    {
        var device = _device;
        if (device is null)
        {
            return;
        }

        try
        {
            await action(device).ConfigureAwait(false);
        }
        catch (LuxaforDeviceDisconnectedException ex)
        {
            // The handle went stale — after a suspend, a re-enumeration, or a cable someone pulled
            // and put back. Just logging it would drop the command on the floor and leave the LEDs
            // showing whatever they showed before, so reopen and send it again. Once: a second
            // failure means the device is really gone, and the reconnect timer takes over.
            Dispatcher.UIThread.Post(() =>
            {
                Log($"Command failed: {ex.Message}");

                if (retryOnDisconnect && ReopenAfterDisconnect(device, ex.Descriptor))
                {
                    ExecuteCommandAsync(action, retryOnDisconnect: false);
                }
            });
        }
        catch (ObjectDisposedException ex)
        {
            // We let go of the device ourselves; there is nothing to reopen.
            Dispatcher.UIThread.Post(() => Log($"Command failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Closes the handle a command just failed on and opens the device again, on the UI thread.
    /// Returns whether there is a device to retry the command on.
    /// </summary>
    private bool ReopenAfterDisconnect(ILuxaforDevice failed, LuxaforDeviceDescriptor? descriptor)
    {
        if (!ReferenceEquals(_device, failed))
        {
            // Something got here first — the monitor's Disconnected event, most likely. Whatever it
            // left behind is the current state, and reopening on top of it would fight it.
            return _device is not null;
        }

        DropDevice();

        // The same device first, since that is the one the command was meant for. A device that
        // came back on a different path is not that device, but for an app that only ever opens the
        // first one it is the better answer than nothing.
        var result = descriptor is null ? _manager.Open() : _manager.Open(descriptor);
        if (!result.IsSuccess && descriptor is not null)
        {
            result = _manager.Open();
        }

        _device = result.Device;

        if (_device is null)
        {
            // Same rule as the reconnect timer: a device that is simply not there needs no line of
            // its own — the failed command above already said so — but a device that is there and
            // will not open does.
            if (_lastOpenFailure != result.Status && result.Status != DeviceOpenStatus.NotFound)
            {
                Log(result.Description);
            }

            _lastOpenFailure = result.Status;

            if (_autoReconnect)
            {
                StartReconnectTimer();
            }

            OnStateChanged();
            return false;
        }

        _lastOpenFailure = null;
        StartMonitoring(_device);
        _ = RequestDeviceInfoAsync(_device);
        Log("Reconnected");
        OnStateChanged();
        return true;
    }

    private void StartMonitoring(ILuxaforDevice device)
    {
        StopMonitoring();
        _monitorCts = new CancellationTokenSource();
        var ct = _monitorCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in device.ObserveAsync(ct).ConfigureAwait(false))
                {
                    switch (evt)
                    {
                        case LuxaforEvent.DeviceIdentified identified:
                            Dispatcher.UIThread.Post(() =>
                            {
                                DeviceInfo = identified.Info;
                                Log($"Device identified: {identified.Info.Type} (SN: {identified.Info.SerialNumber})");
                                OnStateChanged();
                            });
                            break;

                        case LuxaforEvent.DongleDataReceived dongle:
                            Dispatcher.UIThread.Post(() =>
                            {
                                DongleInfo = dongle.Info;
                                Log($"Dongle: Present={dongle.Info.IsDevicePresent}, Battery={dongle.Info.BatteryLevel}% {dongle.Info.BatteryStatus}, RSSI={dongle.Info.Rssi} dBm");
                                OnStateChanged();
                            });
                            break;

                        case LuxaforEvent.MuteButtonStateChanged mute:
                            Dispatcher.UIThread.Post(() =>
                                Log($"Mute button {(mute.IsPressed ? "pressed" : "released")}"));
                            break;

                        case LuxaforEvent.PatternCompleted:
                            Dispatcher.UIThread.Post(() => Log("Pattern completed"));
                            break;

                        case LuxaforEvent.Disconnected:
                            Dispatcher.UIThread.Post(() =>
                            {
                                Log("Device disconnected");

                                if (!ReferenceEquals(_device, device))
                                {
                                    // A failed command got here first and already reopened. The
                                    // handle in place now is a different one and not ours to close.
                                    return;
                                }

                                // The handle stays open — nothing closes it when a device is pulled
                                // out — so it has to be let go of here, or the UI goes on showing a
                                // connection that is not there and the reconnect timer below skips
                                // every tick.
                                DropDevice();
                                OnStateChanged();

                                if (_autoReconnect)
                                {
                                    StartReconnectTimer();
                                }
                            });
                            break;

                        case LuxaforEvent.ReadError error:
                            Dispatcher.UIThread.Post(() => Log($"Read error: {error.Exception.Message}"));
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when monitoring is stopped.
            }
        }, ct);
    }

    private void StopMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
    }

    private async Task RequestDeviceInfoAsync(ILuxaforDevice device)
    {
        try
        {
            await device.RequestDeviceInfoAsync().ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                DeviceInfo = device.DeviceInfo;

                if (DeviceInfo is { } info)
                {
                    Log($"Device identified: {info.Type} (SN: {info.SerialNumber})");
                }

                OnStateChanged();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => Log($"RequestDeviceInfo failed: {ex.Message}"));
        }
    }

    private void StartReconnectTimer()
    {
        StopReconnectTimer();
        _reconnectTimer = new Timer(_ =>
        {
            if (_disposed)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!_autoReconnect || IsDeviceLive())
                {
                    return;
                }

                DropDevice();

                var openResult = _manager.Open();
                _device = openResult.Device;

                if (_device is not null)
                {
                    _lastOpenFailure = null;
                    StartMonitoring(_device);
                    _ = RequestDeviceInfoAsync(_device);
                    StopReconnectTimer();
                    Log("Reconnected");
                    OnStateChanged();
                }
                else if (_lastOpenFailure != openResult.Status)
                {
                    // The timer retries every 2s, so only a change of reason is worth
                    // a log line. A device that is simply unplugged is not worth one at all.
                    _lastOpenFailure = openResult.Status;

                    if (openResult.Status != DeviceOpenStatus.NotFound)
                    {
                        Log(openResult.Description);
                    }
                }
            });
        }, null, 2000, 2000);
    }

    private void StopReconnectTimer()
    {
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
    }

    /// <summary>
    /// Whether the handle we are holding still has a device behind it.
    /// </summary>
    /// <remarks>
    /// Costs a device enumeration, so this is for the places where a wrong answer costs more: the
    /// reconnect timer, which only runs while something is wrong anyway, and the Connect button.
    /// </remarks>
    private bool IsDeviceLive()
    {
        var device = _device;
        if (device?.IsConnected != true)
        {
            return false;
        }

        var descriptor = device.Descriptor;
        return descriptor is null || _manager.IsPresent(descriptor);
    }

    /// <summary>
    /// Stops monitoring and lets go of the device, without trying to talk to it. For a device that
    /// is already gone, where <see cref="CloseDevice"/>'s parting "turn the LEDs off" has nothing
    /// to reach.
    /// </summary>
    private void DropDevice()
    {
        StopMonitoring();
        _device?.Dispose();
        _device = null;
        DeviceInfo = null;
        DongleInfo = null;
    }

    private void CloseDevice()
    {
        if (_device is null)
        {
            return;
        }

        StopMonitoring();

        try
        {
            if (_device.IsConnected)
            {
                _device.TurnOffAsync().GetAwaiter().GetResult();
            }
        }
        catch
        {
            // best effort
        }

        _device.Dispose();
        _device = null;
    }

    private void Log(string message) => LogMessage?.Invoke(message);
    private void OnStateChanged() => StateChanged?.Invoke();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _autoReconnect = false;
        StopReconnectTimer();
        CloseDevice();
    }
}
