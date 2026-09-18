using HidSharp;

namespace DotLuxafor;

/// <summary>
/// Production implementation that discovers HID devices via <see cref="DeviceList.Local"/>.
/// </summary>
internal sealed class HidDeviceListProvider : IHidDeviceListProvider
{
	public IEnumerable<IHidDeviceHandle> GetDevices(int vendorId, int productId)
		=> DeviceList.Local.GetHidDevices(vendorId, productId).Select(device => new HidDeviceHandle(device));

	public IDisposable SubscribeToChanges(Action handler)
	{
		EventHandler<DeviceListChangedEventArgs> onChanged = (_, _) => handler();
		DeviceList.Local.Changed += onChanged;
		return new Subscription(() => DeviceList.Local.Changed -= onChanged);
	}

	private sealed class Subscription : IDisposable
	{
		private Action? _unsubscribe;

		public Subscription(Action unsubscribe)
		{
			_unsubscribe = unsubscribe;
		}

		public void Dispose()
		{
			// Disposing twice must not detach a handler somebody else has since attached.
			Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
		}
	}
}
