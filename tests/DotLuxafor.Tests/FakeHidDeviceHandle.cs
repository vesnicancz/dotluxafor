using DotLuxafor;

namespace DotLuxafor.Tests;

/// <summary>
/// A discovered device the manager can enumerate and try to open, standing in for HidSharp's
/// <c>HidDevice</c> — which cannot be mocked, because its <c>TryOpen</c> is not virtual.
/// </summary>
internal sealed class FakeHidDeviceHandle : IHidDeviceHandle
{
	private readonly IHidStreamAdapter? _stream;
	private readonly Exception? _error;

	private FakeHidDeviceHandle(LuxaforDeviceDescriptor descriptor, IHidStreamAdapter? stream, Exception? error)
	{
		Descriptor = descriptor;
		_stream = stream;
		_error = error;
	}

	public LuxaforDeviceDescriptor Descriptor { get; }

	/// <summary>A device that opens successfully.</summary>
	public static FakeHidDeviceHandle Openable(string devicePath, string? productName = null, string? serialNumber = null)
		=> new FakeHidDeviceHandle(
			new LuxaforDeviceDescriptor(devicePath, productName, serialNumber),
			new FakeHidStreamAdapter(),
			null);

	/// <summary>A device that is attached but refuses to open.</summary>
	public static FakeHidDeviceHandle Blocked(string devicePath, Exception? error = null, string? productName = null, string? serialNumber = null)
		=> new FakeHidDeviceHandle(
			new LuxaforDeviceDescriptor(devicePath, productName, serialNumber),
			null,
			error ?? new UnauthorizedAccessException("Permission denied."));

	public bool TryOpen(out IHidStreamAdapter? stream, out Exception? error)
	{
		if (_stream != null)
		{
			stream = _stream;
			error = null;
			return true;
		}

		stream = null;
		error = _error;
		return false;
	}

	/// <summary>
	/// The bare minimum for a device the manager hands back: it never actually talks to hardware.
	/// </summary>
	private sealed class FakeHidStreamAdapter : IHidStreamAdapter
	{
		public bool CanWrite => true;
		public bool CanRead => true;
		public int ReadTimeout { get; set; }
		public void Write(byte[] buffer) { }
		public int Read(byte[] buffer, int offset, int count) => throw new TimeoutException();
		public void SetFeature(byte[] buffer) { }
		public void GetFeature(byte[] buffer) { }
		public string? GetProductName() => null;
		public string? GetDeviceSerialNumber() => null;
		public void Dispose() { }
	}
}
