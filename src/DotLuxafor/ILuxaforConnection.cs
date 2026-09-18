namespace DotLuxafor;

/// <summary>
/// Defines connection lifecycle operations for a Luxafor device.
/// </summary>
public interface ILuxaforConnection : IAsyncDisposable, IDisposable
{
	/// <summary>
	/// Gets whether the device connection is still active.
	/// </summary>
	bool IsConnected { get; }

	/// <summary>
	/// Gets what identified this device when it was opened, or <c>null</c> when it was not opened
	/// through <see cref="ILuxaforDeviceManager"/>.
	/// </summary>
	/// <remarks>
	/// This is how you tell two devices from <see cref="ILuxaforDeviceManager.OpenAll"/> apart, and
	/// how you get the path to reopen this same device after a reconnect. Unlike
	/// <see cref="DeviceInfo"/> it needs no round-trip to the device — it is known from discovery.
	/// </remarks>
	LuxaforDeviceDescriptor? Descriptor { get; }

	/// <summary>
	/// Gets the color this library last set the whole device to, or <c>null</c> when that is not
	/// known.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Set by <see cref="ILuxaforCommands.SetColorAsync"/> and
	/// <see cref="ILuxaforCommands.FadeToAsync"/> when they target <see cref="LedTarget.All"/>.
	/// It is <c>null</c> before the first such command, after a command that targeted individual
	/// LEDs, and after a strobe, wave or pattern — in each of those cases the device as a whole has
	/// no single resting color.
	/// </para>
	/// <para>
	/// This is what the library last sent, not a reading from the device: the hardware cannot be
	/// asked what it is showing, and anything that drives the device from outside this instance will
	/// not be reflected here.
	/// </para>
	/// </remarks>
	LuxaforColor? LastColor { get; }

	/// <summary>
	/// Gets the device identification info, or <c>null</c> until it has been retrieved.
	/// Populated by <see cref="RequestDeviceInfoAsync"/>, and refreshed whenever a
	/// <see cref="LuxaforEvent.DeviceIdentified"/> report arrives via <see cref="ILuxaforMonitor.ObserveAsync"/>.
	/// </summary>
	/// <remarks>Reading this property is thread-safe; monitoring updates it from a background thread.</remarks>
	DeviceInfo? DeviceInfo { get; }

	/// <summary>
	/// Asks the device to identify itself and stores the answer in <see cref="DeviceInfo"/>,
	/// which is non-<c>null</c> once this method completes successfully.
	/// </summary>
	/// <remarks>
	/// The info comes from a HID feature report when the device supports one, and from the USB HID
	/// descriptor otherwise. This method does not raise a <see cref="LuxaforEvent.DeviceIdentified"/>
	/// event; read <see cref="DeviceInfo"/> once it returns.
	/// </remarks>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task RequestDeviceInfoAsync(CancellationToken cancellationToken = default);
}