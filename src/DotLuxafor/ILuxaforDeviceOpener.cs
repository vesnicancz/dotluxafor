namespace DotLuxafor;

/// <summary>
/// Opens a Luxafor device, without any say in which one.
/// </summary>
/// <remarks>
/// Most of <see cref="ILuxaforDeviceManager"/> exists to choose between devices — list them, open a
/// named one, wait for one to arrive. Application code that only wants something to light up never
/// makes that choice, and depending on the whole manager makes every test double implement nine
/// members to exercise one. Take this instead: <see cref="LuxaforDeviceManager"/> implements it, the
/// DI extensions register it, and a fake is one method.
/// </remarks>
public interface ILuxaforDeviceOpener
{
	/// <summary>
	/// Tries to find and open a connected Luxafor device, reporting why the attempt failed.
	/// </summary>
	/// <remarks>
	/// Which device you get is up to the implementation. <see cref="ILuxaforDeviceManager"/> gives
	/// you whatever the platform enumerates first, which is not guaranteed to be stable across
	/// replugs; use <see cref="ILuxaforDeviceManager.Open(string)"/> when it matters.
	/// </remarks>
	DeviceOpenResult Open();
}
