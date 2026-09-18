using HidSharp;

namespace DotLuxafor;

/// <summary>
/// Default implementation that discovers and opens Luxafor HID devices.
/// </summary>
public sealed class LuxaforDeviceManager : ILuxaforDeviceManager
{
	private readonly IHidDeviceListProvider _deviceListProvider;

	/// <summary>
	/// Initializes a new instance using the default HID device list.
	/// </summary>
	public LuxaforDeviceManager()
		: this(new HidDeviceListProvider())
	{
	}

	internal LuxaforDeviceManager(IHidDeviceListProvider deviceListProvider)
	{
		_deviceListProvider = deviceListProvider;
	}

	/// <inheritdoc />
	public ILuxaforDevice? TryOpen() => Open().Device;

	/// <inheritdoc />
	public DeviceOpenResult Open()
	{
		var device = _deviceListProvider.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId).FirstOrDefault();
		if (device == null)
		{
			return DeviceOpenResult.NotFound();
		}

		return OpenDevice(device);
	}

	/// <inheritdoc />
	public IReadOnlyList<ILuxaforDevice> OpenAll()
	{
		var devices = new List<ILuxaforDevice>();
		foreach (var result in OpenAllResults())
		{
			if (result.Device != null)
			{
				devices.Add(result.Device);
			}
		}
		return devices;
	}

	/// <inheritdoc />
	public IReadOnlyList<DeviceOpenResult> OpenAllResults()
	{
		var results = new List<DeviceOpenResult>();
		foreach (var hidDevice in _deviceListProvider.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
		{
			results.Add(OpenDevice(hidDevice));
		}
		return results;
	}

	/// <summary>
	/// Opens a single HID device, keeping the exception HidSharp would otherwise swallow.
	/// <see cref="HidDevice.TryOpen(out HidStream)"/> reports only a bool, which is why a
	/// device blocked by the OS was indistinguishable from no device at all.
	/// </summary>
	private static DeviceOpenResult OpenDevice(HidDevice hidDevice)
	{
		if (hidDevice.TryOpen(new OpenConfiguration(), out DeviceStream? deviceStream, out Exception? error)
			&& deviceStream is HidStream hidStream)
		{
			return DeviceOpenResult.Opened(new LuxaforDevice(hidStream));
		}

		deviceStream?.Dispose();
		return DeviceOpenResult.Failure(HidOpenFailure.Classify(error), error);
	}

	/// <inheritdoc />
	public bool IsDevicePresent()
	{
		return _deviceListProvider.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId).Any();
	}

	/// <inheritdoc />
	public async Task WaitForDeviceAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (IsDevicePresent())
		{
			return;
		}

		// Continuations run off the notification thread so a caller cannot stall the HID stack.
		var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		void OnDeviceListChanged()
		{
			// The notification does not say what changed, and it fires for every HID device
			// on the machine, so re-query for a Luxafor specifically.
			if (IsDevicePresent())
			{
				arrived.TrySetResult(true);
			}
		}

		using (_deviceListProvider.SubscribeToChanges(OnDeviceListChanged))
		{
			// A device may have arrived between the check above and the subscription going live.
			if (IsDevicePresent())
			{
				return;
			}

			using (cancellationToken.Register(() => arrived.TrySetCanceled(cancellationToken)))
			{
				await arrived.Task.ConfigureAwait(false);
			}
		}
	}
}
