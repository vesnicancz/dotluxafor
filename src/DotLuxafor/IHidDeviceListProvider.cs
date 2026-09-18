namespace DotLuxafor;

/// <summary>
/// Internal abstraction over HID device discovery for testability.
/// Production code uses <see cref="HidDeviceListProvider"/>; tests supply a mock.
/// </summary>
internal interface IHidDeviceListProvider
{
	IEnumerable<IHidDeviceHandle> GetDevices(int vendorId, int productId);

	/// <summary>
	/// Subscribes to changes in the set of attached devices. The handler carries no payload:
	/// the underlying notification does not say what changed, so callers re-query.
	/// </summary>
	/// <returns>A subscription; disposing it stops the notifications.</returns>
	IDisposable SubscribeToChanges(Action handler);
}
