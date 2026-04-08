namespace DotLuxafor;

/// <summary>
/// Defines LED command operations for a Luxafor device.
/// </summary>
public interface ILuxaforCommands
{
	/// <summary>
	/// Sets the specified LED(s) to a solid color.
	/// </summary>
	/// <param name="color">The color to set.</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task SetColorAsync(LuxaforColor color, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default);

	/// <summary>
	/// Fades the specified LED(s) to a color at the given speed.
	/// </summary>
	/// <param name="color">The target color.</param>
	/// <param name="speed">Fade speed (0 = instant, 255 = slowest).</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task FadeToAsync(LuxaforColor color, byte speed, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default);

	/// <summary>
	/// Strobes (flashes) the specified LED(s) with the specified color.
	/// </summary>
	/// <param name="color">The strobe color.</param>
	/// <param name="speed">Flash speed (0 = fastest, 255 = slowest).</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task StrobeAsync(LuxaforColor color, byte speed, byte repeat, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default);

	/// <summary>
	/// Plays a wave animation with the specified color.
	/// </summary>
	/// <param name="type">Wave animation type.</param>
	/// <param name="color">The wave color.</param>
	/// <param name="speed">Wave speed (0 = fastest, 255 = slowest).</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task WaveAsync(WaveType type, LuxaforColor color, byte speed, byte repeat, CancellationToken cancellationToken = default);

	/// <summary>
	/// Plays a built-in animation pattern on the device.
	/// </summary>
	/// <param name="pattern">The pattern to play.</param>
	/// <param name="repeat">Number of repetitions (0 = repeat indefinitely until next command).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task PlayPatternAsync(BuiltInPattern pattern, byte repeat, CancellationToken cancellationToken = default);

	/// <summary>
	/// Turns off all LEDs.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task TurnOffAsync(CancellationToken cancellationToken = default);
}