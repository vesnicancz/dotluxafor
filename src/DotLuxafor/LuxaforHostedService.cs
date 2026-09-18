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
/// <remarks>
/// Application code reaches the device it holds open through <see cref="ILuxaforDeviceAccessor"/>,
/// which <c>AddLuxaforHostedService</c> registers alongside this service.
/// </remarks>
public sealed class LuxaforHostedService : BackgroundService, ILuxaforDeviceAccessor
{
	private readonly ILuxaforDeviceManager _deviceManager;
	private readonly LuxaforOptions _options;
	private readonly ILogger<LuxaforHostedService> _logger;
	private readonly object _deviceLock = new object();
	private ILuxaforDevice? _device;
	private TaskCompletionSource<ILuxaforDevice> _deviceReady = NewDeviceReadySource();
	private Task? _monitorTask;
	private DeviceOpenStatus? _lastOpenFailure;
	private OpenFailure? _lastFailureKind;

	/// <summary>
	/// Picks the configured device out of the attached ones, or <c>null</c> when no device was
	/// configured and the first one found will do.
	/// </summary>
	private readonly Func<IReadOnlyList<LuxaforDeviceDescriptor>, LuxaforDeviceDescriptor?>? _selectDevice;

	/// <summary>Names <see cref="_selectDevice"/> in a log message; <c>null</c> alongside it.</summary>
	private readonly string? _selectorDescription;

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
		(_selectDevice, _selectorDescription) = BuildSelector(_options);
	}

	/// <summary>
	/// Why the last attempt did not produce a device. Drives how long to wait before the next one
	/// and how loudly to log, which the <see cref="DeviceOpenStatus"/> alone cannot: "nothing is
	/// plugged in" and "the wrong thing is plugged in" are both <see cref="DeviceOpenStatus.NotFound"/>
	/// and want opposite handling.
	/// </summary>
	private enum OpenFailure
	{
		/// <summary>No Luxafor is attached. Normal, and worth sleeping on the hotplug notification.</summary>
		NoDeviceAttached,

		/// <summary>Devices are attached, but none is the one <see cref="LuxaforOptions"/> asked for.</summary>
		NoDeviceMatched,

		/// <summary>The chosen device is attached but would not open.</summary>
		OpenFailed
	}

	/// <summary>
	/// Turns the device-selection options into one predicate, plus the phrase that names it in a
	/// log message. Returns <c>(null, null)</c> when none was configured, which means "whatever the
	/// platform enumerates first".
	/// </summary>
	/// <remarks>
	/// Built once rather than per attempt, and only ever one of the three: the options validator
	/// rejects a combination, so the order they are tested in here never decides anything.
	/// </remarks>
	private static (Func<IReadOnlyList<LuxaforDeviceDescriptor>, LuxaforDeviceDescriptor?>?, string?) BuildSelector(LuxaforOptions options)
	{
		if (options.SelectDevice != null)
		{
			return (options.SelectDevice, $"the {nameof(LuxaforOptions.SelectDevice)} callback");
		}

		if (options.DevicePath != null)
		{
			var path = options.DevicePath;

			// Ordinal, because this is matched against a path the platform produced, and a device
			// path is not text to be compared by any culture's rules.
			return (
				devices => devices.FirstOrDefault(d => string.Equals(d.DevicePath, path, StringComparison.Ordinal)),
				$"device path '{path}'");
		}

		if (options.SerialNumber != null)
		{
			var serial = options.SerialNumber;

			// Ignoring case, because a serial number gets copied out of a label or a log by hand
			// and its hex digits are as likely to arrive in one case as the other.
			return (
				devices => devices.FirstOrDefault(d => string.Equals(d.SerialNumber, serial, StringComparison.OrdinalIgnoreCase)),
				$"serial number '{serial}'");
		}

		return (null, null);
	}

	/// <summary>
	/// Gets the currently connected device, if any. Safe to read from any thread.
	/// </summary>
	public ILuxaforDevice? CurrentDevice
	{
		get
		{
			lock (_deviceLock)
			{
				return _device;
			}
		}
	}

	/// <inheritdoc />
	ILuxaforDevice? ILuxaforDeviceAccessor.Current => CurrentDevice;

	/// <inheritdoc />
	public Task<ILuxaforDevice> WaitForDeviceAsync(CancellationToken cancellationToken = default)
	{
		Task<ILuxaforDevice> ready;
		lock (_deviceLock)
		{
			ready = _deviceReady.Task;
		}

		return ready.WaitAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				var device = CurrentDevice;
				if (device == null || !device.IsConnected)
				{
					await CleanupDeviceAsync().ConfigureAwait(false);
					var openResult = OpenConfiguredDevice(out var failure);

					if (openResult.Device != null)
					{
						_lastOpenFailure = null;
						_lastFailureKind = null;
						SetDevice(openResult.Device);
						_logger.LogInformation("Luxafor device connected.");

						if (_options.AutoMonitor)
						{
							_monitorTask = MonitorDeviceAsync(openResult.Device, stoppingToken);
						}
					}
					else
					{
						LogOpenFailure(openResult, failure);
					}
				}

				if (!_options.AutoReconnect)
				{
					return;
				}

				await WaitBeforeRetryAsync(stoppingToken).ConfigureAwait(false);
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
	/// Opens the device <see cref="LuxaforOptions"/> asks for: the one the configured selector
	/// picks, or whatever the platform enumerates first when none is configured.
	/// </summary>
	/// <param name="failure">
	/// Why the attempt did not produce a device. Meaningful only when the result carries none.
	/// </param>
	/// <remarks>
	/// With a selector configured the attached devices are listed first, so that a device that is
	/// present but not the wanted one can be told from nothing being present at all — the two need
	/// different waits, and mistaking the first for the second spins the loop at full speed.
	/// </remarks>
	private DeviceOpenResult OpenConfiguredDevice(out OpenFailure failure)
	{
		if (_selectDevice is null)
		{
			var first = _deviceManager.Open();
			failure = first.Status == DeviceOpenStatus.NotFound ? OpenFailure.NoDeviceAttached : OpenFailure.OpenFailed;
			return first;
		}

		var attached = _deviceManager.List();
		if (attached.Count == 0)
		{
			failure = OpenFailure.NoDeviceAttached;
			return DeviceOpenResult.NotFound();
		}

		var chosen = _selectDevice(attached);
		if (chosen is null)
		{
			failure = OpenFailure.NoDeviceMatched;
			return DeviceOpenResult.NotMatched(_selectorDescription!, attached);
		}

		// Devices are attached, so this is never NoDeviceAttached however it turns out — not even
		// when the chosen one reports NotFound because it was unplugged since the listing. The
		// others are still here, so a hotplug wait would return at once and spin.
		failure = OpenFailure.OpenFailed;
		return _deviceManager.Open(chosen);
	}

	/// <summary>
	/// Waits before the next connection attempt.
	/// </summary>
	/// <remarks>
	/// When the last attempt found nothing plugged in, this sleeps on the operating system's hotplug
	/// notification so plugging a device in is picked up at once instead of up to
	/// <see cref="LuxaforOptions.ReconnectDelay"/> later. Every other outcome — a device that is
	/// present but refuses to open, or one that is present but is not the configured one — falls
	/// back to the plain timer, because a device that is already attached would satisfy the hotplug
	/// wait immediately and spin the loop.
	/// </remarks>
	private Task WaitBeforeRetryAsync(CancellationToken stoppingToken)
		=> _lastFailureKind == OpenFailure.NoDeviceAttached
			? WaitForDeviceArrivalAsync(stoppingToken)
			: Task.Delay(_options.ReconnectDelay, stoppingToken);

	/// <summary>
	/// Waits for a device to be plugged in, giving up after <see cref="LuxaforOptions.ReconnectDelay"/>
	/// so the loop still re-checks periodically if a notification is ever missed.
	/// </summary>
	private async Task WaitForDeviceArrivalAsync(CancellationToken stoppingToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
		timeout.CancelAfter(_options.ReconnectDelay);

		try
		{
			await _deviceManager.WaitForDeviceAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
		{
			// Nothing was plugged in within the fallback interval; go round and look again.
		}
	}

	/// <summary>
	/// Logs why the device could not be opened. Reconnect attempts repeat on a timer,
	/// so only a change of reason is logged to keep the log readable.
	/// </summary>
	private void LogOpenFailure(DeviceOpenResult result, OpenFailure failure)
	{
		if (_lastOpenFailure == result.Status && _lastFailureKind == failure)
		{
			return;
		}

		_lastOpenFailure = result.Status;
		_lastFailureKind = failure;

		switch (failure)
		{
			case OpenFailure.NoDeviceAttached:
				// Nothing is plugged in, which is an ordinary state for a service that waits for a
				// device rather than something the user has to hear about.
				_logger.LogDebug("{Reason}", result.Description);
				break;

			case OpenFailure.NoDeviceMatched:
				// A configured selector matching nothing is far more likely a mistake in the
				// configuration than a device someone has yet to plug in, so it is not hidden at
				// Debug. The description names the devices that are attached.
				_logger.LogWarning("{Reason}", result.Description);
				break;

			default:
				_logger.LogWarning(result.Error, "{Reason}", result.Description);
				break;
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

		var device = CurrentDevice;
		ClearDevice();
		device?.Dispose();
	}

	private static TaskCompletionSource<ILuxaforDevice> NewDeviceReadySource()
		=> new TaskCompletionSource<ILuxaforDevice>(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>
	/// Publishes a newly opened device and releases anyone waiting in <see cref="WaitForDeviceAsync"/>.
	/// </summary>
	private void SetDevice(ILuxaforDevice device)
	{
		lock (_deviceLock)
		{
			_device = device;
			_deviceReady.TrySetResult(device);
		}
	}

	/// <summary>
	/// Withdraws the current device and arms a fresh wait, so a caller that arrives after a
	/// disconnect waits for the next device instead of being handed the dead one.
	/// </summary>
	private void ClearDevice()
	{
		lock (_deviceLock)
		{
			_device = null;

			if (_deviceReady.Task.IsCompleted)
			{
				_deviceReady = NewDeviceReadySource();
			}
		}
	}

	/// <inheritdoc />
	public override void Dispose()
	{
		ILuxaforDevice? device;
		lock (_deviceLock)
		{
			device = _device;
			_device = null;

			// Nothing will connect any more, so waiters are released rather than left hanging.
			_deviceReady.TrySetCanceled();
		}

		device?.Dispose();
		base.Dispose();
	}
}
#endif
