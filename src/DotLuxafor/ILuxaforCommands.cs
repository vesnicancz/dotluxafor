namespace DotLuxafor;

/// <summary>
/// Defines LED command operations for a Luxafor device.
/// </summary>
/// <remarks>
/// <para>
/// Every command sends one short HID report. These methods are asynchronous because commands are
/// serialized against each other and that wait is worth awaiting — not because the report itself is
/// written asynchronously. <c>HidStream</c> offers only a blocking write, and a nine-byte USB report
/// completes in well under a millisecond, so the write is done inline rather than pushed onto a
/// thread-pool thread.
/// </para>
/// <para>
/// A <c>cancellationToken</c> therefore cancels the wait for the device to become free, not a write
/// that has already begun. Once a command reaches the device it always runs to completion, and it
/// stays in effect until the next command replaces it.
/// </para>
/// <para>
/// A device that has been unplugged fails every command with
/// <see cref="LuxaforDeviceDisconnectedException"/>, whether it went away before the write or
/// during it. One that the caller has disposed throws <see cref="ObjectDisposedException"/>
/// instead, because that says the caller let go of the device rather than that the hardware left.
/// </para>
/// </remarks>
public interface ILuxaforCommands
{
	/// <summary>
	/// Sets the specified LED(s) to a solid color.
	/// </summary>
	/// <param name="color">The color to set.</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="LuxaforDeviceDisconnectedException">Thrown when the device is no longer connected.</exception>
	/// <exception cref="ObjectDisposedException">Thrown when the device has been disposed.</exception>
	Task SetColorAsync(LuxaforColor color, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default);

	/// <summary>
	/// Fades the specified LED(s) to a color at the given speed.
	/// </summary>
	/// <param name="color">The target color.</param>
	/// <param name="speed">Fade speed (0 = instant, 255 = slowest).</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="LuxaforDeviceDisconnectedException">Thrown when the device is no longer connected.</exception>
	/// <exception cref="ObjectDisposedException">Thrown when the device has been disposed.</exception>
	Task FadeToAsync(LuxaforColor color, byte speed, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default);

	/// <summary>
	/// Strobes (flashes) the specified LED(s) with the specified color.
	/// </summary>
	/// <param name="color">The strobe color.</param>
	/// <param name="speed">Flash speed (0 = fastest, 255 = slowest).</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="LuxaforDeviceDisconnectedException">Thrown when the device is no longer connected.</exception>
	/// <exception cref="ObjectDisposedException">Thrown when the device has been disposed.</exception>
	Task StrobeAsync(LuxaforColor color, byte speed, byte repeat, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default);

	/// <summary>
	/// Plays a wave animation with the specified color.
	/// </summary>
	/// <param name="type">Wave animation type.</param>
	/// <param name="color">The wave color.</param>
	/// <param name="speed">Wave speed (0 = fastest, 255 = slowest).</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="LuxaforDeviceDisconnectedException">Thrown when the device is no longer connected.</exception>
	/// <exception cref="ObjectDisposedException">Thrown when the device has been disposed.</exception>
	Task WaveAsync(WaveType type, LuxaforColor color, byte speed, byte repeat, CancellationToken cancellationToken = default);

	/// <summary>
	/// Plays a built-in animation pattern on the device.
	/// </summary>
	/// <param name="pattern">The pattern to play.</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="LuxaforDeviceDisconnectedException">Thrown when the device is no longer connected.</exception>
	/// <exception cref="ObjectDisposedException">Thrown when the device has been disposed.</exception>
	Task PlayPatternAsync(BuiltInPattern pattern, byte repeat, CancellationToken cancellationToken = default);

	/// <summary>
	/// Turns off all LEDs.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="LuxaforDeviceDisconnectedException">Thrown when the device is no longer connected.</exception>
	/// <exception cref="ObjectDisposedException">Thrown when the device has been disposed.</exception>
	Task TurnOffAsync(CancellationToken cancellationToken = default);
}