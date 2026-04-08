using HidSharp;

namespace DotLuxafor;

/// <summary>
/// Internal abstraction over HID device discovery for testability.
/// Production code uses <see cref="HidDeviceListProvider"/>; tests supply a mock.
/// </summary>
internal interface IHidDeviceListProvider
{
    IEnumerable<HidDevice> GetDevices(int vendorId, int productId);
}
