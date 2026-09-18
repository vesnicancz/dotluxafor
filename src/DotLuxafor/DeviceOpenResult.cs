namespace DotLuxafor;

/// <summary>
/// The outcome of an attempt to open a Luxafor device, including the reason it failed.
/// </summary>
/// <remarks>
/// Use this instead of <see cref="ILuxaforDeviceManager.TryOpen"/> when the caller needs to tell
/// "no device is plugged in" apart from "a device is plugged in but the OS would not let us open it".
/// </remarks>
public sealed class DeviceOpenResult
{
	private DeviceOpenResult(DeviceOpenStatus status, ILuxaforDevice? device, Exception? error)
	{
		Status = status;
		Device = device;
		Error = error;
	}

	/// <summary>Gets the outcome of the attempt.</summary>
	public DeviceOpenStatus Status { get; }

	/// <summary>
	/// Gets the opened device, or <c>null</c> when the attempt failed.
	/// Ownership passes to the caller, who is responsible for disposing it.
	/// </summary>
	public ILuxaforDevice? Device { get; }

	/// <summary>Gets the exception reported by the HID stack, or <c>null</c> when there was none.</summary>
	public Exception? Error { get; }

	/// <summary>Gets whether a device was opened.</summary>
	public bool IsSuccess => Status == DeviceOpenStatus.Opened && Device != null;

	/// <summary>
	/// Gets a human-readable explanation of the outcome, including a platform-specific hint
	/// for failures the user can resolve (permissions, another application holding the device).
	/// </summary>
	public string Description => HidOpenFailure.Describe(Status, Error);

	internal static DeviceOpenResult Opened(ILuxaforDevice device)
		=> new DeviceOpenResult(DeviceOpenStatus.Opened, device, null);

	internal static DeviceOpenResult NotFound()
		=> new DeviceOpenResult(DeviceOpenStatus.NotFound, null, null);

	internal static DeviceOpenResult Failure(DeviceOpenStatus status, Exception? error)
		=> new DeviceOpenResult(status, null, error);
}
