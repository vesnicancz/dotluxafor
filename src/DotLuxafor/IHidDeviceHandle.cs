namespace DotLuxafor;

/// <summary>
/// Internal abstraction over one discovered HID device, for testability.
/// Production code uses <see cref="HidDeviceHandle"/>; tests supply a fake.
/// </summary>
/// <remarks>
/// This exists because HidSharp's <c>HidDevice</c> cannot be faked — its <c>TryOpen</c> is
/// non-virtual — which left every open path in <see cref="LuxaforDeviceManager"/> untestable.
/// </remarks>
internal interface IHidDeviceHandle
{
	/// <summary>
	/// Describes the device without opening it. Reading USB string descriptors can be costly on
	/// some platforms, so implementations compute this on demand rather than up front.
	/// </summary>
	LuxaforDeviceDescriptor Descriptor { get; }

	/// <summary>
	/// Tries to open the device, keeping the exception rather than reducing it to a bool.
	/// </summary>
	bool TryOpen(out IHidStreamAdapter? stream, out Exception? error);
}
