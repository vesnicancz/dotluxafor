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
	/// <remarks>Use <see cref="Open()"/> to find out which of the two happened.</remarks>
	ILuxaforDevice? TryOpen();

	/// <summary>
	/// Tries to find and open the first connected Luxafor device, reporting why the attempt failed.
	/// </summary>
	/// <remarks>
	/// "First" is whatever the platform enumerates first, which is not guaranteed to be stable
	/// across replugs. Use <see cref="Open(string)"/> when it matters which device you get.
	/// </remarks>
	DeviceOpenResult Open();

	/// <summary>
	/// Opens the device at a specific path, reporting why the attempt failed.
	/// </summary>
	/// <param name="devicePath">
	/// A <see cref="LuxaforDeviceDescriptor.DevicePath"/> from <see cref="List"/> or from an earlier
	/// <see cref="DeviceOpenResult.Descriptor"/>.
	/// </param>
	/// <returns>
	/// The outcome, with <see cref="DeviceOpenStatus.NotFound"/> when nothing is attached at that
	/// path — which is what a device unplugged since the path was read looks like.
	/// </returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="devicePath"/> is <c>null</c>.</exception>
	DeviceOpenResult Open(string devicePath);

	/// <summary>
	/// Opens the device a descriptor identifies. Equivalent to passing its
	/// <see cref="LuxaforDeviceDescriptor.DevicePath"/> to <see cref="Open(string)"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is <c>null</c>.</exception>
	DeviceOpenResult Open(LuxaforDeviceDescriptor descriptor);

	/// <summary>
	/// Lists the Luxafor devices attached to the machine without opening any of them.
	/// </summary>
	/// <remarks>
	/// This is how you offer a choice of device — a picker in a UI, a <c>--device</c> flag — and how
	/// you get the path to hand back to <see cref="Open(string)"/>. Listing needs no permission to
	/// open the device, so a device that will not open still shows up here (with its name and serial
	/// number likely <c>null</c>).
	/// </remarks>
	/// <returns>One descriptor per attached device, in discovery order; empty when none is attached.</returns>
	IReadOnlyList<LuxaforDeviceDescriptor> List();

	/// <summary>
	/// Opens all connected Luxafor devices, skipping any that cannot be opened.
	/// </summary>
	/// <remarks>Use <see cref="OpenAllResults"/> to find out which ones were skipped, and why.</remarks>
	IReadOnlyList<ILuxaforDevice> OpenAll();

	/// <summary>
	/// Opens all connected Luxafor devices, reporting the outcome of every attempt.
	/// </summary>
	/// <remarks>
	/// One entry per attached device, in discovery order; an empty list means nothing is attached.
	/// Devices that opened are owned by the caller, who is responsible for disposing them — including
	/// when other entries in the list report a failure.
	/// </remarks>
	IReadOnlyList<DeviceOpenResult> OpenAllResults();

	/// <summary>
	/// Gets whether any Luxafor device is currently connected (without opening it).
	/// </summary>
	bool IsDevicePresent();

	/// <summary>
	/// Waits until a Luxafor device is attached to the machine, returning immediately if one
	/// already is. The device is not opened — call <see cref="Open()"/> afterwards.
	/// </summary>
	/// <remarks>
	/// Driven by the operating system's hotplug notifications rather than polling. A device that is
	/// attached but cannot be opened (for example because a permission is missing) still satisfies
	/// this wait, so callers must not retry <see cref="Open()"/> in a tight loop on that result.
	/// </remarks>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="OperationCanceledException">Thrown when the token is cancelled first.</exception>
	Task WaitForDeviceAsync(CancellationToken cancellationToken = default);
}