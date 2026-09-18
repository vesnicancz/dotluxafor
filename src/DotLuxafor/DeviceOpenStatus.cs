namespace DotLuxafor;

/// <summary>
/// Describes the outcome of an attempt to open a Luxafor device.
/// </summary>
public enum DeviceOpenStatus
{
	/// <summary>The device was opened successfully.</summary>
	Opened,

	/// <summary>No Luxafor device is connected.</summary>
	NotFound,

	/// <summary>
	/// A device is connected, but the operating system denied access to it.
	/// Usually a missing permission: Input Monitoring on macOS, a udev rule on Linux.
	/// </summary>
	AccessDenied,

	/// <summary>A device is connected, but another application is holding it open.</summary>
	InUse,

	/// <summary>A device is connected, but opening it failed for an unrecognized reason.</summary>
	Failed
}
