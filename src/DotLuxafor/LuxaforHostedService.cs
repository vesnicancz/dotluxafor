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
					var openResult = _deviceManager.Open();

					if (openResult.Device != null)
					{
						_lastOpenFailure = null;
						SetDevice(openResult.Device);
						_logger.LogInformation("Luxafor device connected.");

						if (_options.AutoMonitor)
						{
							_monitorTask = MonitorDeviceAsync(openResult.Device, stoppingToken);
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
	/// Waits before the next connection attempt.
	/// </summary>
	/// <remarks>
	/// When the last attempt found nothing plugged in, this sleeps on the operating system's hotplug
	/// notification so plugging a device in is picked up at once instead of up to
	/// <see cref="LuxaforOptions.ReconnectDelay"/> later. Every other outcome — including a device
	/// that is present but refuses to open — falls back to the plain timer, because a device that is
	/// already attached would satisfy the hotplug wait immediately and spin the loop.
	/// </remarks>
	private Task WaitBeforeRetryAsync(CancellationToken stoppingToken)
		=> _lastOpenFailure == DeviceOpenStatus.NotFound
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
