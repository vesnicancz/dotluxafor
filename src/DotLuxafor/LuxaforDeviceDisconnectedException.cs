namespace DotLuxafor;

/// <summary>
/// Thrown when a command cannot reach a Luxafor device because the device is no longer there.
/// </summary>
/// <remarks>
/// <para>
/// Unplugging is the usual cause. A device the operating system has taken away for any other
/// reason looks the same from here, and so does one that vanished midway through a write — the
/// HID error that gave it away, when there was one, is in <see cref="Exception.InnerException"/>.
/// </para>
/// <para>
/// It derives from <see cref="InvalidOperationException"/>, which is what this library threw
/// before the type existed, so code already catching that keeps working. Catch this type instead
/// to tell a device that went away from a misuse of the API — a second
/// <see cref="ILuxaforMonitor.ObserveAsync"/> consumer, say — which still reports the plain
/// <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// A device disposed by its owner is not this: that is an <see cref="ObjectDisposedException"/>,
/// because it says the caller let go of the device rather than that the hardware left.
/// </para>
/// </remarks>
public sealed class LuxaforDeviceDisconnectedException : InvalidOperationException
{
	private const string DefaultMessage = "The Luxafor device is no longer connected.";

	/// <summary>
	/// Initializes a new instance with a default message.
	/// </summary>
	public LuxaforDeviceDisconnectedException()
		: base(DefaultMessage)
	{
	}

	/// <summary>
	/// Initializes a new instance with the specified message.
	/// </summary>
	/// <param name="message">The message that describes the error.</param>
	public LuxaforDeviceDisconnectedException(string message)
		: base(message)
	{
	}

	/// <summary>
	/// Initializes a new instance with the specified message and inner exception.
	/// </summary>
	/// <param name="message">The message that describes the error.</param>
	/// <param name="innerException">The exception that caused this one, or <c>null</c>.</param>
	public LuxaforDeviceDisconnectedException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}

	/// <summary>
	/// Initializes a new instance naming the device that went away.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the one the library itself throws, and it is public so that a caller can build the
	/// same exception — which is what it takes to test handling of a disconnect, the very thing the
	/// documentation tells callers to write. The other constructors leave
	/// <see cref="Descriptor"/> <c>null</c>, so an exception from them does not stand in for a real
	/// one: the descriptor is missing and the message does not name the device.
	/// </para>
	/// <para>
	/// Passing two bare <c>null</c> literals is ambiguous with the
	/// <see cref="LuxaforDeviceDisconnectedException(string, Exception)"/> overload; name the
	/// argument (<c>descriptor: null</c>) or use the parameterless constructor, which is the same
	/// thing.
	/// </para>
	/// </remarks>
	/// <param name="descriptor">
	/// What identified the device that went away, or <c>null</c> when it was not opened through
	/// <see cref="ILuxaforDeviceManager"/>. It is named in <see cref="Exception.Message"/>.
	/// </param>
	/// <param name="innerException">
	/// The error the HID stack reported, or <c>null</c> when the device was already known to be gone.
	/// </param>
	public LuxaforDeviceDisconnectedException(LuxaforDeviceDescriptor? descriptor, Exception? innerException)
		: base(BuildMessage(descriptor), innerException)
	{
		Descriptor = descriptor;
	}

	/// <summary>
	/// Gets what identified the device that went away, or <c>null</c> when it was not opened
	/// through <see cref="ILuxaforDeviceManager"/>.
	/// </summary>
	/// <remarks>
	/// This is the same descriptor as <see cref="ILuxaforConnection.Descriptor"/>, so it carries
	/// the <see cref="LuxaforDeviceDescriptor.DevicePath"/> needed to reopen the device once it
	/// comes back — which is what makes it worth catching this over handling several devices'
	/// failures as one.
	/// </remarks>
	public LuxaforDeviceDescriptor? Descriptor { get; }

	private static string BuildMessage(LuxaforDeviceDescriptor? descriptor)
		=> descriptor is null
			? DefaultMessage
			: $"The Luxafor device '{descriptor}' is no longer connected.";
}
