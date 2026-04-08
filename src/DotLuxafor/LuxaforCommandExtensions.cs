namespace DotLuxafor;

/// <summary>
/// Convenience extension methods for <see cref="ILuxaforCommands"/> providing RGB byte overloads.
/// </summary>
public static class LuxaforCommandExtensions
{
	/// <summary>
	/// Sets the specified LED(s) to a solid color using RGB byte values.
	/// </summary>
	public static Task SetColorAsync(this ILuxaforCommands device, byte r, byte g, byte b, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
		=> device.SetColorAsync(new LuxaforColor(r, g, b), target, cancellationToken);

	/// <summary>
	/// Fades the specified LED(s) to a color using RGB byte values.
	/// </summary>
	public static Task FadeToAsync(this ILuxaforCommands device, byte r, byte g, byte b, byte speed, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
		=> device.FadeToAsync(new LuxaforColor(r, g, b), speed, target, cancellationToken);

	/// <summary>
	/// Strobes the specified LED(s) using RGB byte values.
	/// </summary>
	public static Task StrobeAsync(this ILuxaforCommands device, byte r, byte g, byte b, byte speed, byte repeat, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
		=> device.StrobeAsync(new LuxaforColor(r, g, b), speed, repeat, target, cancellationToken);

	/// <summary>
	/// Plays a wave animation using RGB byte values.
	/// </summary>
	public static Task WaveAsync(this ILuxaforCommands device, WaveType type, byte r, byte g, byte b, byte speed, byte repeat, CancellationToken cancellationToken = default)
		=> device.WaveAsync(type, new LuxaforColor(r, g, b), speed, repeat, cancellationToken);
}