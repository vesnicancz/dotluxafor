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