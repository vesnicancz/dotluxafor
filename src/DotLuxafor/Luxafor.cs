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
	public static ILuxaforDevice? TryOpen() => Manager.TryOpen();

	/// <summary>
	/// Opens all connected Luxafor devices.
	/// </summary>
	public static IReadOnlyList<ILuxaforDevice> OpenAll() => Manager.OpenAll();

	/// <summary>
	/// Gets whether any Luxafor device is currently connected (without opening it).
	/// </summary>
	public static bool IsDevicePresent() => Manager.IsDevicePresent();
}