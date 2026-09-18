namespace DotLuxafor;

/// <summary>
/// Discovers and opens Luxafor HID devices.
/// </summary>
/// <remarks>
/// Implement or fake <see cref="ILuxaforDeviceOpener"/> instead when all you need is a device to
/// talk to; this interface is for code that chooses which device that is.
/// </remarks>
public interface ILuxaforDeviceManager : ILuxaforDeviceOpener
{
	/// <summary>
	/// Tries to find and open the first connected Luxafor device.
	/// Returns <c>null</c> if no device is found or cannot be opened.
	/// </summary>
	/// <remarks>Use <see cref="ILuxaforDeviceOpener.Open"/> to find out which of the two happened.</remarks>
	ILuxaforDevice? TryOpen();

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
	/// Gets whether one particular device is still attached, without opening it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the liveness check <see cref="ILuxaforConnection.IsConnected"/> is not: it asks the
	/// operating system what is attached right now, so a device unplugged since it was opened
	/// reports <c>false</c> here while the open handle still claims to be fine.
	/// </para>
	/// <para>
	/// It costs a device enumeration, which is why it is a method on the manager rather than a
	/// property on the device — call it when a stale answer would cost you something (before a
	/// command that must not silently do nothing, or on a reconnect timer), not in a loop.
	/// </para>
	/// </remarks>
	/// <param name="descriptor">
	/// The device to look for, normally <see cref="ILuxaforConnection.Descriptor"/> of an open device.
	/// </param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is <c>null</c>.</exception>
	bool IsPresent(LuxaforDeviceDescriptor descriptor);

	/// <summary>
	/// Waits until a Luxafor device is attached to the machine, returning immediately if one
	/// already is. The device is not opened — call <see cref="ILuxaforDeviceOpener.Open"/> afterwards.
	/// </summary>
	/// <remarks>
	/// Driven by the operating system's hotplug notifications rather than polling. A device that is
	/// attached but cannot be opened (for example because a permission is missing) still satisfies
	/// this wait, so callers must not retry <see cref="ILuxaforDeviceOpener.Open"/> in a tight loop on
	/// that result.
	/// </remarks>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="OperationCanceledException">Thrown when the token is cancelled first.</exception>
	Task WaitForDeviceAsync(CancellationToken cancellationToken = default);
}