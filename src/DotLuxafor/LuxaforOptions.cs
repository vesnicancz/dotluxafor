namespace DotLuxafor;

/// <summary>
/// Configuration options for the Luxafor device integration.
/// </summary>
public sealed class LuxaforOptions
{
	/// <summary>
	/// Whether to automatically reconnect when the device is disconnected.
	/// Default is <c>false</c>.
	/// </summary>
	public bool AutoReconnect { get; set; }

	/// <summary>
	/// Delay between reconnection attempts. Must be positive.
	/// Default is 3 seconds.
	/// </summary>
	public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(3);

	/// <summary>
	/// Whether to start monitoring input reports automatically when the device connects.
	/// Default is <c>false</c>.
	/// </summary>
	public bool AutoMonitor { get; set; }

	/// <summary>
	/// The <see cref="LuxaforDeviceDescriptor.DevicePath"/> of the device the background service
	/// should open, or <c>null</c> to take whatever the platform enumerates first.
	/// </summary>
	/// <remarks>
	/// Binds from configuration, so a specific device can be pinned in appsettings.json. The path
	/// only holds while the device stays in the same port — across a replug into a different port,
	/// <see cref="SerialNumber"/> is the selector that keeps working.
	/// </remarks>
	public string? DevicePath { get; set; }

	/// <summary>
	/// The USB serial number of the device the background service should open, or <c>null</c> to
	/// take whatever the platform enumerates first. Matched case-insensitively against
	/// <see cref="LuxaforDeviceDescriptor.SerialNumber"/>.
	/// </summary>
	/// <remarks>
	/// The stable way to name one device among several, because unlike
	/// <see cref="DevicePath"/> it survives a replug into another port. It needs the platform to
	/// hand the serial number over, which on some systems takes the same permission as opening the
	/// device — a device that will not open is also one this cannot match.
	/// </remarks>
	public string? SerialNumber { get; set; }

	/// <summary>
	/// Picks the device the background service should open from the attached ones, or <c>null</c>
	/// to take whatever the platform enumerates first.
	/// </summary>
	/// <remarks>
	/// The escape hatch for a rule <see cref="DevicePath"/> and <see cref="SerialNumber"/> do not
	/// cover — matching on the product name, say, or preferring one device but settling for
	/// another. It is called on every connection attempt with the devices attached at that moment,
	/// never with an empty list, and returning <c>null</c> means "none of these", which leaves the
	/// service waiting for the next attempt. Being a delegate it is set in code, not bound from
	/// configuration.
	/// </remarks>
	/// <example>
	/// <code>
	/// services.AddLuxaforHostedService(o =&gt;
	/// {
	///     o.SelectDevice = devices =&gt;
	///         devices.FirstOrDefault(d =&gt; d.ProductName?.Contains("FLAG") == true);
	/// });
	/// </code>
	/// </example>
	public Func<IReadOnlyList<LuxaforDeviceDescriptor>, LuxaforDeviceDescriptor?>? SelectDevice { get; set; }
}
