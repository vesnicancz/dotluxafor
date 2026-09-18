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
	private readonly string? _description;

	private DeviceOpenResult(DeviceOpenStatus status, ILuxaforDevice? device, LuxaforDeviceDescriptor? descriptor, Exception? error, string? description = null)
	{
		Status = status;
		Device = device;
		Descriptor = descriptor;
		Error = error;
		_description = description;
	}

	/// <summary>Gets the outcome of the attempt.</summary>
	public DeviceOpenStatus Status { get; }

	/// <summary>
	/// Gets the opened device, or <c>null</c> when the attempt failed.
	/// Ownership passes to the caller, who is responsible for disposing it.
	/// </summary>
	public ILuxaforDevice? Device { get; }

	/// <summary>
	/// Gets the device this attempt was about, or <c>null</c> when no device was found at all.
	/// Present even when the open failed, so a caller can report or retry the specific device.
	/// </summary>
	public LuxaforDeviceDescriptor? Descriptor { get; }

	/// <summary>Gets the exception reported by the HID stack, or <c>null</c> when there was none.</summary>
	public Exception? Error { get; }

	/// <summary>Gets whether a device was opened.</summary>
	public bool IsSuccess => Status == DeviceOpenStatus.Opened && Device != null;

	/// <summary>
	/// Gets a human-readable explanation of the outcome, including a platform-specific hint
	/// for failures the user can resolve (permissions, another application holding the device).
	/// </summary>
	public string Description => _description ?? HidOpenFailure.Describe(Status, Error);

	internal static DeviceOpenResult Opened(ILuxaforDevice device, LuxaforDeviceDescriptor descriptor)
		=> new DeviceOpenResult(DeviceOpenStatus.Opened, device, descriptor, null);

	internal static DeviceOpenResult NotFound()
		=> new DeviceOpenResult(DeviceOpenStatus.NotFound, null, null, null);

	/// <summary>
	/// Reports that a specific device path is not attached, which is a different thing for the
	/// caller to act on than "no Luxafor is attached at all".
	/// </summary>
	internal static DeviceOpenResult NotFound(string devicePath)
		=> new DeviceOpenResult(
			DeviceOpenStatus.NotFound,
			null,
			null,
			null,
			$"No Luxafor device is attached at '{devicePath}'. The path may be stale — re-read it from List().");

	/// <summary>
	/// Reports that Luxafor devices are attached but none of them is the one that was asked for.
	/// </summary>
	/// <remarks>
	/// Different from "no Luxafor is attached at all" in the way that matters to a caller: waiting
	/// for a device to be plugged in will not help, because the wrong ones are already here. The
	/// attached devices are named in the description so a mistyped selector can be fixed from the
	/// log alone.
	/// </remarks>
	internal static DeviceOpenResult NotMatched(string criteria, IReadOnlyList<LuxaforDeviceDescriptor> attached)
		=> new DeviceOpenResult(
			DeviceOpenStatus.NotFound,
			null,
			null,
			null,
			$"No attached Luxafor device matches {criteria}. Attached: {string.Join(", ", attached)}.");

	internal static DeviceOpenResult Failure(DeviceOpenStatus status, LuxaforDeviceDescriptor? descriptor, Exception? error)
		=> new DeviceOpenResult(status, null, descriptor, error);
}
