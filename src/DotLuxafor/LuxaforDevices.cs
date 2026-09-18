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
	/// <remarks>Use <see cref="Open"/> to find out which of the two happened.</remarks>
	public static ILuxaforDevice? TryOpen() => Manager.TryOpen();

	/// <summary>
	/// Tries to find and open the first connected Luxafor device, reporting why the attempt failed.
	/// </summary>
	public static DeviceOpenResult Open() => Manager.Open();

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
	/// Waits until a Luxafor device is attached to the machine, returning immediately if one
	/// already is. The device is not opened — call <see cref="Open"/> afterwards.
	/// </summary>
	public static Task WaitForDeviceAsync(CancellationToken cancellationToken = default)
		=> Manager.WaitForDeviceAsync(cancellationToken);
}