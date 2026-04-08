using Avalonia.Threading;
using DotLuxafor;

namespace DotLuxafor.ControlPanel.Services;

public sealed class DeviceService : IDisposable
{
    private ILuxaforDevice? _device;
    private CancellationTokenSource? _monitorCts;
    private Timer? _reconnectTimer;
    private bool _autoReconnect;
    private bool _disposed;

    public bool IsConnected => _device?.IsConnected == true;

    public event Action<string>? LogMessage;
    public event Action? StateChanged;

    public DeviceInfo? DeviceInfo { get; private set; }
    public DongleInfo? DongleInfo { get; private set; }

    public bool Connect()
    {
        if (_device?.IsConnected == true)
        {
            return true;
        }

        StopMonitoring();
        _device?.Dispose();
        _device = new LuxaforDeviceManager().TryOpen();

        if (_device is null)
        {
            Log("No Luxafor device found");
            return false;
        }

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

    private async void ExecuteCommandAsync(Func<ILuxaforDevice, Task> action)
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            await action(_device).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Dispatcher.UIThread.Post(() => Log($"Command failed: {ex.Message}"));
        }
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
                                DeviceInfo = null;
                                DongleInfo = null;
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
                if (!_autoReconnect || _device?.IsConnected == true)
                {
                    return;
                }

                StopMonitoring();
                _device?.Dispose();
                _device = new LuxaforDeviceManager().TryOpen();

                if (_device is not null)
                {
                    StartMonitoring(_device);
                    _ = RequestDeviceInfoAsync(_device);
                    StopReconnectTimer();
                    Log("Reconnected");
                    OnStateChanged();
                }
            });
        }, null, 2000, 2000);
    }

    private void StopReconnectTimer()
    {
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
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
