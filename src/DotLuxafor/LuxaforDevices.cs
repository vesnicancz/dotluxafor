using Microsoft.Extensions.Logging;

namespace DotLuxafor;

/// <summary>
/// Static convenience entry point for discovering and opening Luxafor devices without DI.
/// </summary>
/// <example>
/// <code>
/// using var device = LuxaforDevices.TryOpen();
/// await device.SetColorAsync(LuxaforColor.Red);
/// </code>
/// </example>
public static class LuxaforDevices
{
	private static readonly LuxaforDeviceManager Manager = new LuxaforDeviceManager();

	/// <summary>
	/// Tries to find and open the first connected Luxafor device.
	/// Returns <c>null</c> if no device is found or cannot be opened.
	/// </summary>
	/// <remarks>Use <see cref="Open()"/> to find out which of the two happened.</remarks>
	public static ILuxaforDevice? TryOpen() => Manager.TryOpen();

	/// <summary>
	/// Tries to find and open the first connected Luxafor device, reporting why the attempt failed.
	/// </summary>
	/// <remarks>
	/// "First" is whatever the platform enumerates first. Use <see cref="Open(string)"/> when it
	/// matters which device you get.
	/// </remarks>
	public static DeviceOpenResult Open() => Manager.Open();

	/// <summary>
	/// Opens the device at a specific path, reporting why the attempt failed.
	/// </summary>
	/// <param name="devicePath">A <see cref="LuxaforDeviceDescriptor.DevicePath"/> from <see cref="List"/>.</param>
	public static DeviceOpenResult Open(string devicePath) => Manager.Open(devicePath);

	/// <summary>
	/// Opens the device a descriptor identifies.
	/// </summary>
	public static DeviceOpenResult Open(LuxaforDeviceDescriptor descriptor) => Manager.Open(descriptor);

	/// <summary>
	/// Opens the first connected Luxafor device, wrapped so that it reopens itself when its handle
	/// goes stale. See <see cref="ReconnectingLuxaforDevice"/> for what is replayed and what is not.
	/// </summary>
	/// <param name="logger">Where each reopen is reported, or <c>null</c> to report nowhere.</param>
	public static DeviceOpenResult OpenReconnecting(ILogger? logger = null) => Manager.OpenReconnecting(logger);

	/// <summary>
	/// Opens a specific Luxafor device, wrapped so that it reopens itself when its handle goes stale.
	/// </summary>
	/// <param name="descriptor">The device to open, from <see cref="List"/>.</param>
	/// <param name="logger">Where each reopen is reported, or <c>null</c> to report nowhere.</param>
	public static DeviceOpenResult OpenReconnecting(LuxaforDeviceDescriptor descriptor, ILogger? logger = null)
		=> Manager.OpenReconnecting(descriptor, logger);

	/// <summary>
	/// Lists the Luxafor devices attached to the machine without opening any of them, so a caller
	/// can offer a choice and then <see cref="Open(string)"/> the one that was picked.
	/// </summary>
	public static IReadOnlyList<LuxaforDeviceDescriptor> List() => Manager.List();

	/// <summary>
	/// Opens all connected Luxafor devices, skipping any that cannot be opened.
	/// </summary>
	/// <remarks>Use <see cref="OpenAllResults"/> to find out which ones were skipped, and why.</remarks>
	public static IReadOnlyList<ILuxaforDevice> OpenAll() => Manager.OpenAll();

	/// <summary>
	/// Opens all connected Luxafor devices, reporting the outcome of every attempt.
	/// </summary>
	public static IReadOnlyList<DeviceOpenResult> OpenAllResults() => Manager.OpenAllResults();

	/// <summary>
	/// Gets whether any Luxafor device is currently connected (without opening it).
	/// </summary>
	public static bool IsDevicePresent() => Manager.IsDevicePresent();

	/// <summary>
	/// Gets whether one particular device is still attached, without opening it.
	/// </summary>
	/// <remarks>
	/// The liveness check <see cref="ILuxaforConnection.IsConnected"/> is not, at the cost of a
	/// device enumeration. See <see cref="ILuxaforDeviceManager.IsPresent"/>.
	/// </remarks>
	public static bool IsPresent(LuxaforDeviceDescriptor descriptor) => Manager.IsPresent(descriptor);

	/// <summary>
	/// Waits until a Luxafor device is attached to the machine, returning immediately if one
	/// already is. The device is not opened — call <see cref="Open()"/> afterwards.
	/// </summary>
	public static Task WaitForDeviceAsync(CancellationToken cancellationToken = default)
		=> Manager.WaitForDeviceAsync(cancellationToken);
}