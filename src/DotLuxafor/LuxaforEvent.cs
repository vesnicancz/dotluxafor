namespace DotLuxafor;

/// <summary>
/// Base type for all events received from a Luxafor device during monitoring.
/// Use pattern matching to handle specific event types.
/// </summary>
public abstract record LuxaforEvent
{
	private LuxaforEvent() { }

	/// <summary>
	/// Raised when a device identification report is received (after requesting device info).
	/// Contains the device type and serial number.
	/// </summary>
	public sealed record DeviceIdentified(DeviceInfo Info) : LuxaforEvent;

	/// <summary>
	/// Raised when a dongle status report is received (Bluetooth Pro devices only).
	/// Contains battery level, charging status, RSSI, and device presence.
	/// </summary>
	public sealed record DongleDataReceived(DongleInfo Info) : LuxaforEvent;

	/// <summary>
	/// Raised when a mute button state change is detected (Mute Button devices only).
	/// </summary>
	public sealed record MuteButtonStateChanged(bool IsPressed) : LuxaforEvent;

	/// <summary>
	/// Raised when the device finishes playing a pattern or wave animation.
	/// </summary>
	public sealed record PatternCompleted() : LuxaforEvent;

	/// <summary>
	/// Raised when the device connection is lost.
	/// </summary>
	public sealed record Disconnected() : LuxaforEvent;

	/// <summary>
	/// Raised when an error occurs during input report reading.
	/// </summary>
	public sealed record ReadError(Exception Exception) : LuxaforEvent;
}