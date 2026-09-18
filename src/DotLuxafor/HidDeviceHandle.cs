using HidSharp;

namespace DotLuxafor;

/// <summary>
/// Production wrapper around a discovered <see cref="HidDevice"/>.
/// </summary>
internal sealed class HidDeviceHandle : IHidDeviceHandle
{
	private readonly HidDevice _device;
	private LuxaforDeviceDescriptor? _descriptor;

	public HidDeviceHandle(HidDevice device)
	{
		_device = device;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Computed on first use and cached. Reading the USB string descriptors opens the device on
	/// some platforms, which is too much to spend on <see cref="ILuxaforDeviceManager.IsDevicePresent"/>.
	/// Two threads racing here would each build an equal descriptor, so the unsynchronized
	/// assignment is harmless.
	/// </remarks>
	public LuxaforDeviceDescriptor Descriptor => _descriptor ??= Describe(_device);

	/// <inheritdoc />
	public bool TryOpen(out IHidStreamAdapter? stream, out Exception? error)
	{
		if (_device.TryOpen(new OpenConfiguration(), out DeviceStream? deviceStream, out error)
			&& deviceStream is HidStream hidStream)
		{
			stream = new HidStreamAdapter(hidStream);
			return true;
		}

		deviceStream?.Dispose();
		stream = null;
		return false;
	}

	private static LuxaforDeviceDescriptor Describe(HidDevice device)
		=> new LuxaforDeviceDescriptor(device.DevicePath, TryGet(device.GetProductName), TryGet(device.GetSerialNumber));

	/// <summary>
	/// Reads one USB string descriptor, treating a failure as "not available" rather than an error:
	/// a device we cannot name is still a device the caller may want to open.
	/// </summary>
	private static string? TryGet(Func<string> read)
	{
		try
		{
			return read();
		}
		catch
		{
			return null;
		}
	}
}
