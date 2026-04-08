using HidSharp;

namespace DotLuxafor;

/// <summary>
/// Default implementation that discovers and opens Luxafor HID devices.
/// </summary>
public sealed class LuxaforDeviceManager : ILuxaforDeviceManager
{
    private readonly IHidDeviceListProvider _deviceListProvider;

    /// <summary>
    /// Initializes a new instance using the default HID device list.
    /// </summary>
    public LuxaforDeviceManager()
        : this(new HidDeviceListProvider())
    {
    }

    internal LuxaforDeviceManager(IHidDeviceListProvider deviceListProvider)
    {
        _deviceListProvider = deviceListProvider;
    }

    /// <inheritdoc />
    public ILuxaforDevice? TryOpen()
    {
        var device = _deviceListProvider.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId).FirstOrDefault();
        if (device == null)
        {
            return null;
        }

        if (!device.TryOpen(out var stream))
        {
            return null;
        }

        return new LuxaforDevice(stream);
    }

    /// <inheritdoc />
    public IReadOnlyList<ILuxaforDevice> OpenAll()
    {
        var devices = new List<ILuxaforDevice>();
        foreach (var hidDevice in _deviceListProvider.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
        {
            if (hidDevice.TryOpen(out var stream))
            {
                devices.Add(new LuxaforDevice(stream));
            }
        }
        return devices;
    }

    /// <inheritdoc />
    public bool IsDevicePresent()
    {
        return _deviceListProvider.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId).Any();
    }
}
