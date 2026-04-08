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
	/// Gets the device identification info, available after a <see cref="LuxaforEvent.DeviceIdentified"/> event
	/// is received via <see cref="ILuxaforMonitor.ObserveAsync"/>.
	/// </summary>
	DeviceInfo? DeviceInfo { get; }

	/// <summary>
	/// Sends a request to the device to identify itself.
	/// The response arrives as a <see cref="LuxaforEvent.DeviceIdentified"/> event
	/// via <see cref="ILuxaforMonitor.ObserveAsync"/>.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task RequestDeviceInfoAsync(CancellationToken cancellationToken = default);
}