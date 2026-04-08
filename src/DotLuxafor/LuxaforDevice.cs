using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HidSharp;

namespace DotLuxafor;

/// <summary>
/// Controls a Luxafor LED device (Flag, Bluetooth Pro, Mute Button, Smart Button) via HID.
/// Supports sending commands (color, fade, strobe, wave, pattern) and receiving input reports
/// (battery status, mute button state, pattern completion, device identification).
/// </summary>
/// <remarks>
/// All command methods are thread-safe and can be called while monitoring is active.
/// Only one <see cref="ObserveAsync"/> consumer is supported at a time; calling it while
/// another enumeration is active will throw <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class LuxaforDevice : ILuxaforDevice
{
    /// <summary>Luxafor USB Vendor ID (Microchip).</summary>
    public const int VendorId = 0x04D8;

    /// <summary>Luxafor USB Product ID.</summary>
    public const int ProductId = 0xF372;

    private const byte CommandStaticColor = 0x01;
    private const byte CommandFade = 0x02;
    private const byte CommandStrobe = 0x03;
    private const byte CommandWave = 0x04;
    private const byte CommandPattern = 0x06;

    private const byte ReportDeviceInfo = 0x80;
    private const byte ReportMuteButton = 0x83;
    private const byte ReportDongle = 0x41;

    internal const int ReportLength = 9;
    private const int MonitoringReadTimeoutMs = 500;

    private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);
    private readonly IHidStreamAdapter _stream;
    private int _disposed;
    private int _monitoring;

    internal LuxaforDevice(IHidStreamAdapter stream)
    {
        _stream = stream;
    }

    internal LuxaforDevice(HidStream stream)
        : this(new HidStreamAdapter(stream))
    {
    }

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                return false;
            }

            try
            {
                return _stream.CanWrite;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public DeviceInfo? DeviceInfo { get; private set; }

    #region Commands

    /// <inheritdoc />
    public Task SetColorAsync(LuxaforColor color, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
        => SendReportAsync(CommandStaticColor, (byte)target, color.R, color.G, color.B, 0x00, 0x00, cancellationToken);

    /// <inheritdoc />
    public Task FadeToAsync(LuxaforColor color, byte speed, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
        => SendReportAsync(CommandFade, (byte)target, color.R, color.G, color.B, speed, 0x00, cancellationToken);

    /// <inheritdoc />
    public Task StrobeAsync(LuxaforColor color, byte speed, byte repeat, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
        => SendReportAsync(CommandStrobe, (byte)target, color.R, color.G, color.B, speed, repeat, cancellationToken);

    /// <inheritdoc />
    public Task WaveAsync(WaveType type, LuxaforColor color, byte speed, byte repeat, CancellationToken cancellationToken = default)
        => SendReportAsync(CommandWave, (byte)type, color.R, color.G, color.B, repeat, speed, cancellationToken);

    /// <inheritdoc />
    public Task PlayPatternAsync(BuiltInPattern pattern, byte repeat, CancellationToken cancellationToken = default)
        => SendReportAsync(CommandPattern, (byte)pattern, repeat, 0x00, 0x00, 0x00, 0x00, cancellationToken);

    /// <inheritdoc />
    public Task TurnOffAsync(CancellationToken cancellationToken = default)
        => SetColorAsync(LuxaforColor.Off, LedTarget.All, cancellationToken);

    #endregion Commands

    #region Connection

    /// <inheritdoc />
    public async Task RequestDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Try reading a HID feature report containing device identification.
            try
            {
                byte[] buffer = new byte[ReportLength];
                _stream.GetFeature(buffer);
                ProcessReportAndUpdateState(buffer);

                if (DeviceInfo != null)
                {
                    return;
                }
            }
            catch
            {
                // GetFeature not supported by this device/dongle — fall through.
            }

            // Fall back to USB HID descriptor properties.
            var productName = _stream.GetProductName();
            var serialStr = _stream.GetDeviceSerialNumber();
            var type = ClassifyDeviceFromProductName(productName);
            long serial = 0;
            if (serialStr != null)
            {
                long.TryParse(serialStr, out serial);
            }

            DeviceInfo = new DeviceInfo(type, serial);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    #endregion Connection

    #region Monitoring

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when another consumer is already monitoring this device.</exception>
    public async IAsyncEnumerable<LuxaforEvent> ObserveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (Interlocked.CompareExchange(ref _monitoring, 1, 0) != 0)
        {
            throw new InvalidOperationException("Only one ObserveAsync consumer is supported at a time.");
        }

        try
        {
            var channel = Channel.CreateBounded<LuxaforEvent>(new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var readTask = Task.Run(() => ReadLoop(channel.Writer, cts.Token), cts.Token);

            try
            {
                while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var evt))
                    {
                        yield return evt;
                    }
                }
            }
            finally
            {
                cts.Cancel();

                try
                {
                    await readTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested.
                }
            }
        }
        finally
        {
            Volatile.Write(ref _monitoring, 0);
        }
    }

    #endregion Monitoring

    #region Dispose

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _stream.Dispose();
        _writeSemaphore.Dispose();
    }

    #endregion Dispose

    /// <summary>
    /// Sends a 9-byte HID output report to the device.
    /// The semaphore serializes writes; the actual I/O is synchronous because
    /// HidSharp does not expose an async write API.
    /// </summary>
    internal async Task SendReportAsync(byte command, byte param1, byte param2, byte param3, byte param4, byte param5, byte param6, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_stream.CanWrite)
            {
                throw new InvalidOperationException("Device is not connected.");
            }

            byte[] report = new byte[] { 0x00, command, param1, param2, param3, param4, param5, param6, 0x00 };
            _stream.Write(report);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    private void ReadLoop(ChannelWriter<LuxaforEvent> writer, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReportLength];

        try
        {
            if (!_stream.CanRead)
            {
                writer.TryWrite(new LuxaforEvent.Disconnected());
                writer.TryComplete();
                return;
            }

            _stream.ReadTimeout = MonitoringReadTimeoutMs;

            while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                try
                {
                    if (!_stream.CanRead)
                    {
                        writer.TryWrite(new LuxaforEvent.Disconnected());
                        break;
                    }

                    int bytesRead;
                    try
                    {
                        bytesRead = _stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }

                    if (bytesRead < ReportLength)
                    {
                        continue;
                    }

                    var evt = ProcessReportAndUpdateState(buffer);
                    if (evt != null)
                    {
                        writer.TryWrite(evt);
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (IOException)
                {
                    writer.TryWrite(new LuxaforEvent.Disconnected());
                    break;
                }
                catch (Exception ex)
                {
                    writer.TryWrite(new LuxaforEvent.ReadError(ex));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when monitoring is stopped via cancellation.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private LuxaforEvent? ProcessReportAndUpdateState(byte[] buffer)
    {
        var evt = ParseReport(buffer);
        if (evt is LuxaforEvent.DeviceIdentified identified)
        {
            DeviceInfo = identified.Info;
        }
        return evt;
    }

    /// <summary>
    /// Parses a raw HID input report buffer into a strongly-typed <see cref="LuxaforEvent"/>.
    /// Returns <c>null</c> if the report is not recognized.
    /// </summary>
    internal static LuxaforEvent? ParseReport(byte[] buffer)
    {
        // Dongle status report (battery, RSSI, device presence)
        if (buffer[1] == ReportDongle)
        {
            var devicePresent = Convert.ToBoolean(buffer[2]);
            var rssi = (int)(sbyte)buffer[3];
            var batteryLevel = Convert.ToInt32(buffer[6]);
            var batteryStatus = buffer[7] switch
            {
                1 => BatteryStatus.Charging,
                2 => BatteryStatus.Full,
                _ => BatteryStatus.NotConnected
            };

            return new LuxaforEvent.DongleDataReceived(new DongleInfo(devicePresent, rssi, batteryLevel, batteryStatus));
        }

        // Mute button state report
        if (buffer[1] == ReportMuteButton)
        {
            var isPressed = buffer[2] == 1;
            return new LuxaforEvent.MuteButtonStateChanged(isPressed);
        }

        // Device identification report (serial number + device type)
        if (buffer[1] == ReportDeviceInfo)
        {
            var deviceType = ClassifyDevice(buffer[2]);
            var serialNumber = ExtractSerialNumber(buffer, deviceType);
            var info = new DeviceInfo(deviceType, serialNumber);
            return new LuxaforEvent.DeviceIdentified(info);
        }

        // Pattern completion report
        if ((buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 1) ||
            (buffer[0] == 0 && buffer[1] == 1 && buffer[2] == 0))
        {
            return new LuxaforEvent.PatternCompleted();
        }

        return null;
    }

    /// <summary>
    /// Classifies a device type from the USB HID product name string.
    /// </summary>
    internal static DeviceType ClassifyDeviceFromProductName(string? productName)
    {
        if (productName is null || productName.Length == 0)
        {
            return DeviceType.Standard;
        }

        var upper = productName.ToUpperInvariant();

        if (upper.Contains("MUTE"))
        {
            return DeviceType.MuteButton;
        }

        if (upper.Contains("SMART"))
        {
            return DeviceType.SmartButton;
        }

        if (upper.Contains("BT") || upper.Contains("BLUETOOTH"))
        {
            return DeviceType.Bluetooth;
        }

        if (upper.Contains("COLORBLIND"))
        {
            return DeviceType.Colorblind;
        }

        return DeviceType.Standard;
    }

    /// <summary>
    /// Classifies a device type from the type byte in a device identification report.
    /// </summary>
    internal static DeviceType ClassifyDevice(byte typeByte) => typeByte switch
    {
        4 => DeviceType.Colorblind,
        30 => DeviceType.MuteButton,
        50 => DeviceType.SmartButton,
        >= 10 and < 30 => DeviceType.Bluetooth,
        _ => DeviceType.Standard
    };

    /// <summary>
    /// Extracts the serial number from a device identification report buffer.
    /// </summary>
    internal static long ExtractSerialNumber(byte[] buffer, DeviceType type)
    {
        return type switch
        {
            DeviceType.Bluetooth or DeviceType.MuteButton or DeviceType.SmartButton =>
                ((long)buffer[3] << 40) | ((long)buffer[4] << 32) |
                ((long)buffer[5] << 24) | ((long)buffer[6] << 16) |
                ((long)buffer[7] << 8) | buffer[8],

            _ => ((long)buffer[3] << 8) | buffer[4]
        };
    }

    private void ThrowIfDisposed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
#else
        if (Volatile.Read(ref _disposed) == 1)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
#endif
    }
}
