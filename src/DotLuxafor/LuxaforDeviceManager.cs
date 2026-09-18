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
		var handle = GetHandles().FirstOrDefault();
		if (handle == null)
		{
			return DeviceOpenResult.NotFound();
		}

		return OpenDevice(handle);
	}

	/// <inheritdoc />
	public DeviceOpenResult Open(string devicePath)
	{
		if (devicePath == null)
		{
			throw new ArgumentNullException(nameof(devicePath));
		}

		// Ordinal, because the path being matched is one this library handed out from the same
		// platform API — not something a user typed.
		var handle = GetHandles()
			.FirstOrDefault(h => string.Equals(h.Descriptor.DevicePath, devicePath, StringComparison.Ordinal));

		return handle == null ? DeviceOpenResult.NotFound(devicePath) : OpenDevice(handle);
	}

	/// <inheritdoc />
	public DeviceOpenResult Open(LuxaforDeviceDescriptor descriptor)
	{
		if (descriptor == null)
		{
			throw new ArgumentNullException(nameof(descriptor));
		}

		return Open(descriptor.DevicePath);
	}

	/// <inheritdoc />
	public IReadOnlyList<LuxaforDeviceDescriptor> List()
	{
		var descriptors = new List<LuxaforDeviceDescriptor>();
		foreach (var handle in GetHandles())
		{
			descriptors.Add(handle.Descriptor);
		}
		return descriptors;
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
		foreach (var handle in GetHandles())
		{
			results.Add(OpenDevice(handle));
		}
		return results;
	}

	/// <summary>
	/// Opens a single HID device, keeping the exception HidSharp would otherwise swallow.
	/// Its <c>TryOpen</c> reports only a bool, which is why a device blocked by the OS was
	/// indistinguishable from no device at all.
	/// </summary>
	private static DeviceOpenResult OpenDevice(IHidDeviceHandle handle)
	{
		if (handle.TryOpen(out var stream, out var error) && stream != null)
		{
			return DeviceOpenResult.Opened(new LuxaforDevice(stream, handle.Descriptor), handle.Descriptor);
		}

		return DeviceOpenResult.Failure(HidOpenFailure.Classify(error), handle.Descriptor, error);
	}

	private IEnumerable<IHidDeviceHandle> GetHandles()
		=> _deviceListProvider.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId);

	/// <inheritdoc />
	public bool IsDevicePresent()
	{
		return GetHandles().Any();
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
