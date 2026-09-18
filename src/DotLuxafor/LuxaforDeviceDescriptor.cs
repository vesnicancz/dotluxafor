namespace DotLuxafor;

/// <summary>
/// Identifies one Luxafor device attached to the machine, without opening it.
/// </summary>
/// <remarks>
/// <see cref="DevicePath"/> is the handle to keep: it is what <see cref="ILuxaforDeviceManager.Open(string)"/>
/// takes, and it is stable for as long as the device stays plugged into the same port. The other two
/// properties come from USB string descriptors, which some platforms will not hand over without the
/// permission needed to open the device — hence nullable.
/// </remarks>
/// <param name="DevicePath">The operating system's path to the device.</param>
/// <param name="ProductName">The USB product name, or <c>null</c> when it could not be read.</param>
/// <param name="SerialNumber">The USB serial number, or <c>null</c> when it could not be read.</param>
public sealed record LuxaforDeviceDescriptor(string DevicePath, string? ProductName, string? SerialNumber)
{
	/// <summary>
	/// Returns the most identifiable form available: the product name and serial number when the
	/// platform supplied them, and the device path otherwise.
	/// </summary>
	public override string ToString()
	{
		if (ProductName is null && SerialNumber is null)
		{
			return DevicePath;
		}

		var name = ProductName ?? "Luxafor";
		return SerialNumber is null ? name : $"{name} ({SerialNumber})";
	}
}
