#if NET8_0_OR_GREATER
namespace DotLuxafor;

/// <summary>
/// Gives application code access to the device held open by the Luxafor background service.
/// Registered by <c>AddLuxaforHostedService</c>; inject it wherever you need to drive the LED.
/// </summary>
/// <remarks>
/// The background service owns the device and disposes it on disconnect and on shutdown, so do not
/// dispose it and do not cache it in a field — read <see cref="Current"/> each time instead.
/// </remarks>
/// <example>
/// <code>
/// public sealed class StatusService(ILuxaforDeviceAccessor luxafor)
/// {
///     public Task SetBusyAsync(CancellationToken ct)
///         =&gt; luxafor.Current?.SetColorAsync(LuxaforColor.Red, cancellationToken: ct) ?? Task.CompletedTask;
/// }
/// </code>
/// </example>
public interface ILuxaforDeviceAccessor
{
	/// <summary>
	/// Gets the device the background service currently has open, or <c>null</c> when none is
	/// connected. Safe to read from any thread.
	/// </summary>
	ILuxaforDevice? Current { get; }

	/// <summary>
	/// Waits until the background service has a device open and returns it, returning immediately
	/// if one already is.
	/// </summary>
	/// <remarks>
	/// The returned device may be disconnected and disposed at any time afterwards, so use it
	/// promptly rather than storing it.
	/// </remarks>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="OperationCanceledException">
	/// Thrown when the token is cancelled, or when the background service shuts down while waiting.
	/// </exception>
	Task<ILuxaforDevice> WaitForDeviceAsync(CancellationToken cancellationToken = default);
}
#endif
