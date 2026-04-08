using HidSharp;

namespace DotLuxafor;

/// <summary>
/// Production implementation that discovers HID devices via <see cref="DeviceList.Local"/>.
/// </summary>
internal sealed class HidDeviceListProvider : IHidDeviceListProvider
{
    public IEnumerable<HidDevice> GetDevices(int vendorId, int productId)
        => DeviceList.Local.GetHidDevices(vendorId, productId);
}
