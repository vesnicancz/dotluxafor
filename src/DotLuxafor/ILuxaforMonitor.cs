namespace DotLuxafor;

/// <summary>
/// Defines input report monitoring for a Luxafor device.
/// </summary>
public interface ILuxaforMonitor
{
	/// <summary>
	/// Starts monitoring the device and yields events as they arrive.
	/// Monitoring begins when enumeration starts and stops when the token is cancelled or the enumerable is disposed.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token to stop monitoring.</param>
	/// <returns>An async enumerable of device events.</returns>
	IAsyncEnumerable<LuxaforEvent> ObserveAsync(CancellationToken cancellationToken = default);
}