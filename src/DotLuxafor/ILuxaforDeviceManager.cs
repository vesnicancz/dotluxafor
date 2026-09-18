namespace DotLuxafor;

/// <summary>
/// Discovers and opens Luxafor HID devices.
/// </summary>
public interface ILuxaforDeviceManager
{
	/// <summary>
	/// Tries to find and open the first connected Luxafor device.
	/// Returns <c>null</c> if no device is found or cannot be opened.
	/// </summary>
	/// <remarks>Use <see cref="Open"/> to find out which of the two happened.</remarks>
	ILuxaforDevice? TryOpen();

	/// <summary>
	/// Tries to find and open the first connected Luxafor device, reporting why the attempt failed.
	/// </summary>
	DeviceOpenResult Open();

	/// <summary>
	/// Opens all connected Luxafor devices.
	/// </summary>
	IReadOnlyList<ILuxaforDevice> OpenAll();

	/// <summary>
	/// Gets whether any Luxafor device is currently connected (without opening it).
	/// </summary>
	bool IsDevicePresent();
}