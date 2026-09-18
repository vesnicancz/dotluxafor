using System.Diagnostics;

namespace DotLuxafor;

/// <summary>
/// Host-driven animations built on top of the plain color commands.
/// </summary>
/// <remarks>
/// <para>
/// These run on the computer, not on the device: each one sends a stream of static-color reports at
/// roughly 40 Hz for as long as it is running. That buys colors the hardware cannot produce on its
/// own — a fade between two arbitrary colors, a brightness envelope — at the cost of having to stay
/// awake to drive it.
/// </para>
/// <para>
/// Prefer the hardware commands where they suffice: <see cref="ILuxaforCommands.FadeToAsync"/> and
/// <see cref="ILuxaforCommands.StrobeAsync"/> run on the device and keep going after the process
/// stops awaiting them.
/// </para>
/// </remarks>
public static class LuxaforAnimations
{
	/// <summary>
	/// How long to wait between frames — about 40 per second, which is smooth to the eye and still
	/// a modest number of USB reports.
	/// </summary>
	private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(25);

	/// <summary>
	/// Fades from one color to another over a wall-clock duration, in software.
	/// </summary>
	/// <remarks>
	/// The position is taken from the clock rather than from a frame counter, so the fade takes the
	/// time it was asked for even when the device is slow to accept reports — it drops frames
	/// instead of running long. It always finishes by setting <paramref name="to"/> exactly.
	/// </remarks>
	/// <param name="device">The device to drive.</param>
	/// <param name="from">The color to start at.</param>
	/// <param name="to">The color to end at.</param>
	/// <param name="duration">How long the fade should take. Zero or less sets <paramref name="to"/> at once.</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token. Cancelling leaves the LEDs mid-fade.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="device"/> is <c>null</c>.</exception>
	/// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
	public static async Task FadeOverAsync(
		this ILuxaforCommands device,
		LuxaforColor from,
		LuxaforColor to,
		TimeSpan duration,
		LedTarget target = LedTarget.All,
		CancellationToken cancellationToken = default)
	{
		if (device is null)
		{
			throw new ArgumentNullException(nameof(device));
		}

		var elapsed = Stopwatch.StartNew();

		while (elapsed.Elapsed < duration)
		{
			var amount = elapsed.Elapsed.TotalMilliseconds / duration.TotalMilliseconds;
			await device.SetColorAsync(LuxaforColor.Lerp(from, to, amount), target, cancellationToken).ConfigureAwait(false);

			var remaining = duration - elapsed.Elapsed;
			var delay = remaining < FrameInterval ? remaining : FrameInterval;
			if (delay > TimeSpan.Zero)
			{
				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
			}
		}

		// Land on the destination exactly, whatever the frame timing did.
		await device.SetColorAsync(to, target, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Fades from the device's current color to another over a wall-clock duration, in software.
	/// </summary>
	/// <remarks>
	/// Starts from <see cref="ILuxaforConnection.LastColor"/>, falling back to
	/// <see cref="LuxaforColor.Off"/> when the resting color is not known — after a pattern, say.
	/// Pass the starting color explicitly when you need to be sure of it.
	/// </remarks>
	/// <param name="device">The device to drive.</param>
	/// <param name="to">The color to end at.</param>
	/// <param name="duration">How long the fade should take. Zero or less sets <paramref name="to"/> at once.</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token. Cancelling leaves the LEDs mid-fade.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="device"/> is <c>null</c>.</exception>
	/// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
	public static Task FadeOverAsync(
		this ILuxaforDevice device,
		LuxaforColor to,
		TimeSpan duration,
		LedTarget target = LedTarget.All,
		CancellationToken cancellationToken = default)
	{
		if (device is null)
		{
			throw new ArgumentNullException(nameof(device));
		}

		return FadeOverAsync(device, device.LastColor ?? LuxaforColor.Off, to, duration, target, cancellationToken);
	}

	/// <summary>
	/// Pulses a color by riding its brightness up and down, in software.
	/// </summary>
	/// <remarks>
	/// One cycle goes dark, up to full <paramref name="color"/>, and back to dark, following a
	/// raised cosine so there is no visible corner at either end. When the requested cycles are
	/// done the LEDs are left showing <paramref name="color"/> steadily, so a pulse can be used to
	/// draw attention to a status that then stays put. Cancelling leaves them wherever the last
	/// frame put them.
	/// </remarks>
	/// <param name="device">The device to drive.</param>
	/// <param name="color">The color at the peak of each pulse.</param>
	/// <param name="period">How long one dark-to-bright-to-dark cycle takes.</param>
	/// <param name="cycles">How many cycles to run. Zero or less pulses until the token is cancelled.</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="device"/> is <c>null</c>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period"/> is not positive.</exception>
	/// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
	public static async Task PulseAsync(
		this ILuxaforCommands device,
		LuxaforColor color,
		TimeSpan period,
		int cycles = 0,
		LedTarget target = LedTarget.All,
		CancellationToken cancellationToken = default)
	{
		if (device is null)
		{
			throw new ArgumentNullException(nameof(device));
		}

		if (period <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(period), period, "Pulse period must be positive.");
		}

		TimeSpan? total = cycles > 0 ? TimeSpan.FromTicks(period.Ticks * cycles) : null;
		var elapsed = Stopwatch.StartNew();

		while (total is null || elapsed.Elapsed < total.Value)
		{
			var phase = elapsed.Elapsed.TotalMilliseconds % period.TotalMilliseconds / period.TotalMilliseconds;

			// Raised cosine: 0 at both ends of the cycle, 1 in the middle.
			var brightness = (1 - Math.Cos(phase * 2 * Math.PI)) / 2;

			await device.SetColorAsync(color.WithBrightness(brightness), target, cancellationToken).ConfigureAwait(false);
			await Task.Delay(FrameInterval, cancellationToken).ConfigureAwait(false);
		}

		await device.SetColorAsync(color, target, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Sets a color for the duration of a scope, then puts back the color the device was resting at.
	/// </summary>
	/// <remarks>
	/// The color to restore is read from <see cref="ILuxaforConnection.LastColor"/> before the new
	/// one is set, falling back to <see cref="LuxaforColor.Off"/> when the resting color is not
	/// known. Use the overload that takes <c>restoreTo</c> when the caller knows better — after a
	/// pattern or a per-LED command, for instance, or when something outside this instance has
	/// driven the device.
	/// </remarks>
	/// <example>
	/// <code>
	/// await using (await device.SetColorScopedAsync(LuxaforColor.Red, cancellationToken: ct))
	/// {
	///     await RunTheBuildAsync(ct);
	/// }
	/// // the LED is back to whatever it was showing before
	/// </code>
	/// </example>
	/// <param name="device">The device to drive.</param>
	/// <param name="color">The color to show inside the scope.</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token for setting the color. Restoring it is not cancellable.</param>
	/// <returns>A handle that restores the previous color when disposed.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="device"/> is <c>null</c>.</exception>
	public static Task<IAsyncDisposable> SetColorScopedAsync(
		this ILuxaforDevice device,
		LuxaforColor color,
		LedTarget target = LedTarget.All,
		CancellationToken cancellationToken = default)
	{
		if (device is null)
		{
			throw new ArgumentNullException(nameof(device));
		}

		return SetColorScopedAsync(device, color, device.LastColor ?? LuxaforColor.Off, target, cancellationToken);
	}

	/// <summary>
	/// Sets a color for the duration of a scope, then sets an explicitly chosen one.
	/// </summary>
	/// <param name="device">The device to drive.</param>
	/// <param name="color">The color to show inside the scope.</param>
	/// <param name="restoreTo">The color to set when the scope is disposed.</param>
	/// <param name="target">Which LED(s) to target. Defaults to all LEDs.</param>
	/// <param name="cancellationToken">Cancellation token for setting the color. Restoring it is not cancellable.</param>
	/// <returns>A handle that sets <paramref name="restoreTo"/> when disposed.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="device"/> is <c>null</c>.</exception>
	public static async Task<IAsyncDisposable> SetColorScopedAsync(
		this ILuxaforCommands device,
		LuxaforColor color,
		LuxaforColor restoreTo,
		LedTarget target = LedTarget.All,
		CancellationToken cancellationToken = default)
	{
		if (device is null)
		{
			throw new ArgumentNullException(nameof(device));
		}

		await device.SetColorAsync(color, target, cancellationToken).ConfigureAwait(false);
		return new ColorScope(device, restoreTo, target);
	}

	private sealed class ColorScope : IAsyncDisposable
	{
		private readonly ILuxaforCommands _device;
		private readonly LuxaforColor _restoreTo;
		private readonly LedTarget _target;
		private int _disposed;

		public ColorScope(ILuxaforCommands device, LuxaforColor restoreTo, LedTarget target)
		{
			_device = device;
			_restoreTo = restoreTo;
			_target = target;
		}

		public async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 1)
			{
				return;
			}

			try
			{
				// CancellationToken.None on purpose: a scope that ends because its token was
				// cancelled is exactly the case where the light still has to be put back.
				await _device.SetColorAsync(_restoreTo, _target, CancellationToken.None).ConfigureAwait(false);
			}
			catch (ObjectDisposedException)
			{
				// The device was disposed inside the scope; there is nothing left to restore, and
				// throwing here would mask whatever the scope body was doing.
			}
			catch (InvalidOperationException)
			{
				// Same for a device that has since been unplugged.
			}
		}
	}
}
