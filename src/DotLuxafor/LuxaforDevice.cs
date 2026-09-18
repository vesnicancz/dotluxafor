using System.Runtime.CompilerServices;
using System.Threading.Channels;

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

	/// <summary>
	/// How many events <see cref="ObserveAsync"/> buffers before the read loop has to wait for the consumer.
	/// </summary>
	private const int EventBufferCapacity = 64;

	private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);
	private readonly object _stateLock = new object();
	private readonly IHidStreamAdapter _stream;
	private DeviceInfo? _deviceInfo;
	private LuxaforColor? _lastColor;
	private int _disposed;
	private int _monitoring;

	internal LuxaforDevice(IHidStreamAdapter stream, LuxaforDeviceDescriptor? descriptor = null)
	{
		_stream = stream;
		Descriptor = descriptor;
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
	public LuxaforDeviceDescriptor? Descriptor { get; }

	/// <inheritdoc />
	/// <remarks>
	/// Guarded by a lock: the read loop writes this from a background thread, and the value is a
	/// nullable struct larger than a word, so an unsynchronized read could tear.
	/// </remarks>
	public DeviceInfo? DeviceInfo
	{
		get
		{
			lock (_stateLock)
			{
				return _deviceInfo;
			}
		}
		private set
		{
			lock (_stateLock)
			{
				_deviceInfo = value;
			}
		}
	}

	/// <inheritdoc />
	/// <remarks>Guarded by the same lock as <see cref="DeviceInfo"/>, and for the same reason.</remarks>
	public LuxaforColor? LastColor
	{
		get
		{
			lock (_stateLock)
			{
				return _lastColor;
			}
		}
		private set
		{
			lock (_stateLock)
			{
				_lastColor = value;
			}
		}
	}

	#region Commands

	/// <inheritdoc />
	public async Task SetColorAsync(LuxaforColor color, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
	{
		await SendReportAsync(CommandStaticColor, (byte)target, color.R, color.G, color.B, 0x00, 0x00, cancellationToken).ConfigureAwait(false);
		RecordRestingColor(color, target);
	}

	/// <inheritdoc />
	public async Task FadeToAsync(LuxaforColor color, byte speed, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
	{
		await SendReportAsync(CommandFade, (byte)target, color.R, color.G, color.B, speed, 0x00, cancellationToken).ConfigureAwait(false);

		// The fade is still running on the device, but the colour it is heading for is where it
		// comes to rest, and that is what LastColor is about.
		RecordRestingColor(color, target);
	}

	/// <inheritdoc />
	public async Task StrobeAsync(LuxaforColor color, byte speed, byte repeat, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
	{
		await SendReportAsync(CommandStrobe, (byte)target, color.R, color.G, color.B, speed, repeat, cancellationToken).ConfigureAwait(false);
		LastColor = null;
	}

	/// <inheritdoc />
	public async Task WaveAsync(WaveType type, LuxaforColor color, byte speed, byte repeat, CancellationToken cancellationToken = default)
	{
		await SendReportAsync(CommandWave, (byte)type, color.R, color.G, color.B, repeat, speed, cancellationToken).ConfigureAwait(false);
		LastColor = null;
	}

	/// <inheritdoc />
	public async Task PlayPatternAsync(BuiltInPattern pattern, byte repeat, CancellationToken cancellationToken = default)
	{
		await SendReportAsync(CommandPattern, (byte)pattern, repeat, 0x00, 0x00, 0x00, 0x00, cancellationToken).ConfigureAwait(false);
		LastColor = null;
	}

	/// <inheritdoc />
	public Task TurnOffAsync(CancellationToken cancellationToken = default)
		=> SetColorAsync(LuxaforColor.Off, LedTarget.All, cancellationToken);

	/// <summary>
	/// Records the colour the device now rests at — but only for a whole-device command. After a
	/// per-LED command the device as a whole has no one colour, so the answer is "unknown".
	/// </summary>
	private void RecordRestingColor(LuxaforColor color, LedTarget target)
		=> LastColor = target == LedTarget.All ? color : (LuxaforColor?)null;

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
			catch (Exception ex) when (ex is not ObjectDisposedException)
			{
				// GetFeature is not supported by every device/dongle — fall through to the
				// descriptor. Disposal is not a device quirk, so it keeps propagating.
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
	/// <remarks>
	/// Events are buffered 64 deep and delivered in order. A consumer that falls further behind than
	/// that applies backpressure to the read loop rather than having events dropped, so a burst of
	/// mute-button presses is never silently lost. Keep the body of the <c>await foreach</c> short and
	/// hand slow work to another task.
	/// </remarks>
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
			var channel = Channel.CreateBounded<LuxaforEvent>(new BoundedChannelOptions(EventBufferCapacity)
			{
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = true
			});

			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var readTask = StartReadLoop(channel.Writer, cts.Token);

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
	/// </summary>
	/// <remarks>
	/// The semaphore serializes writes and is the only part a caller actually awaits. The write
	/// itself is synchronous: <c>HidStream</c> exposes only a blocking <c>Write</c>, and a report
	/// this small is not worth handing to a thread-pool thread. So the token is honoured up to the
	/// moment the write starts and not afterwards.
	/// </remarks>
	internal async Task SendReportAsync(byte command, byte param1, byte param2, byte param3, byte param4, byte param5, byte param6, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!_stream.CanWrite)
			{
				throw new LuxaforDeviceDisconnectedException(Descriptor, null);
			}

			byte[] report = new byte[] { 0x00, command, param1, param2, param3, param4, param5, param6, 0x00 };

			try
			{
				_stream.Write(report);
			}
			catch (IOException ex)
			{
				// A device unplugged mid-write surfaces as an IOException from the HID stack. The
				// caller should not have to know that: whether it went away just before the write
				// or during it, the device is gone either way, so both report it the same.
				throw new LuxaforDeviceDisconnectedException(Descriptor, ex);
			}
		}
		finally
		{
			_writeSemaphore.Release();
		}
	}

	/// <summary>
	/// Starts the read loop on a dedicated background thread and returns a task that completes when
	/// the loop does.
	/// </summary>
	/// <remarks>
	/// Deliberately not <see cref="Task.Run(Action)"/>. <see cref="ReadLoop"/> blocks for the whole
	/// monitoring session — it is a synchronous read with a half-second timeout in a loop, and it
	/// blocks again inside <see cref="Publish"/> whenever the consumer falls behind. Parking that on
	/// a thread-pool thread costs the pool a thread for hours, and monitoring several devices at
	/// once is a plausible way to starve it. A dedicated thread is what this work actually is.
	/// </remarks>
	private Task StartReadLoop(ChannelWriter<LuxaforEvent> writer, CancellationToken cancellationToken)
	{
		// RunContinuationsAsynchronously so awaiting the task never runs the consumer's
		// continuation on the read thread, which is about to exit.
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		var thread = new Thread(() =>
		{
			try
			{
				ReadLoop(writer, cancellationToken);
				completion.TrySetResult(true);
			}
			catch (OperationCanceledException)
			{
				completion.TrySetCanceled(cancellationToken);
			}
			catch (Exception ex)
			{
				// ReadLoop handles its own errors, so this is the "should not happen" path. Surface
				// it through the task rather than letting it take the process down.
				completion.TrySetException(ex);
			}
		})
		{
			IsBackground = true,
			Name = "DotLuxafor HID read loop"
		};

		thread.Start();
		return completion.Task;
	}

	private void ReadLoop(ChannelWriter<LuxaforEvent> writer, CancellationToken cancellationToken)
	{
		var buffer = new byte[ReportLength];

		try
		{
			if (!_stream.CanRead)
			{
				Publish(writer, new LuxaforEvent.Disconnected(), cancellationToken);
				return;
			}

			_stream.ReadTimeout = MonitoringReadTimeoutMs;

			while (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
			{
				try
				{
					if (!_stream.CanRead)
					{
						Publish(writer, new LuxaforEvent.Disconnected(), cancellationToken);
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
					if (evt != null && !Publish(writer, evt, cancellationToken))
					{
						break;
					}
				}
				catch (ObjectDisposedException)
				{
					break;
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (IOException)
				{
					Publish(writer, new LuxaforEvent.Disconnected(), cancellationToken);
					break;
				}
				catch (Exception ex)
				{
					if (!Publish(writer, new LuxaforEvent.ReadError(ex), cancellationToken))
					{
						break;
					}
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

	/// <summary>
	/// Hands an event to the consumer, waiting for room when the buffer is full.
	/// Returns <c>false</c> once nobody is listening any more, which is the read loop's signal to stop.
	/// </summary>
	/// <remarks>
	/// The read loop is synchronous, so the wait blocks its thread. That is the point: blocking the
	/// reader is what stops a slow consumer from losing events.
	/// </remarks>
	private static bool Publish(ChannelWriter<LuxaforEvent> writer, LuxaforEvent evt, CancellationToken cancellationToken)
	{
		if (writer.TryWrite(evt))
		{
			return true;
		}

		try
		{
			writer.WriteAsync(evt, cancellationToken).AsTask().GetAwaiter().GetResult();
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (ChannelClosedException)
		{
			return false;
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
