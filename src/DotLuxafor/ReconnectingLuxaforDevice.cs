using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotLuxafor;

/// <summary>
/// An <see cref="ILuxaforDevice"/> that reopens the device when its handle goes stale, so a command
/// is not lost to a disconnect the caller could not have seen coming.
/// </summary>
/// <remarks>
/// <para>
/// A handle can stop working while the device is still there — after a suspend, a re-enumeration,
/// or a cable pulled out and put back. Without this, the command that discovers it throws
/// <see cref="LuxaforDeviceDisconnectedException"/> and the LEDs keep showing whatever they showed
/// before, until whatever drives the device next comes round. This wrapper closes the dead handle,
/// opens the device again by its <see cref="LuxaforDeviceDescriptor.DevicePath"/>, and sends the
/// command once more.
/// </para>
/// <para>
/// <b>What it replays.</b> Only commands that describe a <i>state</i>:
/// <see cref="SetColorAsync"/>, <see cref="FadeToAsync"/> and <see cref="TurnOffAsync"/>. Those are
/// idempotent with respect to a delay — whether the second attempt lands 50ms or 2s later, the
/// device ends up where it was asked to be. <see cref="StrobeAsync"/>, <see cref="WaveAsync"/> and
/// <see cref="PlayPatternAsync"/> describe an <i>event in time</i>: replaying "flash three times"
/// after an unknown pause either duplicates an alert the user half saw or delivers it after the
/// thing it announced is over. Those reopen the device — so the next command works — but let the
/// exception reach the caller.
/// </para>
/// <para>
/// <b>What it remembers.</b> A reopened device is a new object with no history, so
/// <see cref="LastColor"/> and <see cref="DeviceInfo"/> are kept here rather than read from it.
/// This matters beyond tidiness: <see cref="LuxaforAnimations.FadeOverAsync(ILuxaforDevice, LuxaforColor, TimeSpan, LedTarget, CancellationToken)"/>
/// and <see cref="LuxaforAnimations.SetColorScopedAsync(ILuxaforDevice, LuxaforColor, LedTarget, CancellationToken)"/>
/// both read <see cref="LastColor"/>, and a fade starting from black or a scope restoring
/// <see cref="LuxaforColor.Off"/> is exactly the silent wrongness this wrapper exists to prevent.
/// The remembered color is not merely carried over — after a reopen it is <i>sent again</i>, because
/// a device that was physically replugged came back dark and carrying the color over would describe
/// a light that is not lit.
/// </para>
/// <para>
/// <b>What it does not do.</b> It reopens only the device at the descriptor it was built with. A
/// device that came back on a different path is not provably the same hardware — serial numbers are
/// <c>null</c> on some platforms — so it is not opened in its place. Monitoring is not resurrected
/// either: <see cref="ObserveAsync"/> ends on <see cref="LuxaforEvent.Disconnected"/> as it always
/// did, because events between the disconnect and the reopen are simply lost and a consumer would
/// have no way to tell. Start a new enumeration; it runs against whatever device is current then.
/// And after a per-LED command <see cref="LastColor"/> is <c>null</c>, so per-LED state is not
/// restored across a reopen.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var device = manager.OpenReconnecting(logger).Device!;
/// await device.SetColorAsync(LuxaforColor.Red);   // survives a stale handle
/// </code>
/// </example>
public sealed class ReconnectingLuxaforDevice : ILuxaforDevice
{
	private readonly ILuxaforDeviceManager _manager;
	private readonly ILogger _logger;

	/// <summary>Serializes reopening, so several failing commands produce one new device, not several.</summary>
	private readonly SemaphoreSlim _reopenGate = new SemaphoreSlim(1, 1);

	private readonly object _stateLock = new object();
	private ILuxaforDevice? _inner;
	private LuxaforColor? _lastColor;
	private DeviceInfo? _deviceInfo;
	private int _disposed;

	/// <summary>
	/// Wraps an open device, taking ownership of it.
	/// </summary>
	/// <param name="manager">Used to reopen the device; normally the one that opened it.</param>
	/// <param name="device">
	/// The open device. Its <see cref="ILuxaforConnection.Descriptor"/> is what will be reopened, so
	/// it must have one — a device opened outside discovery cannot be found again.
	/// </param>
	/// <param name="logger">Where each reopen is reported, or <c>null</c> to report nowhere.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="manager"/> or <paramref name="device"/> is <c>null</c>.</exception>
	/// <exception cref="ArgumentException">Thrown when the device has no descriptor.</exception>
	public ReconnectingLuxaforDevice(ILuxaforDeviceManager manager, ILuxaforDevice device, ILogger? logger = null)
	{
		if (manager == null)
		{
			throw new ArgumentNullException(nameof(manager));
		}

		if (device == null)
		{
			throw new ArgumentNullException(nameof(device));
		}

		if (device.Descriptor == null)
		{
			throw new ArgumentException(
				"The device has no descriptor, so it cannot be reopened. Open it through ILuxaforDeviceManager.",
				nameof(device));
		}

		_manager = manager;
		_logger = logger ?? NullLogger.Instance;
		Descriptor = device.Descriptor;
		_inner = device;
		_lastColor = device.LastColor;
		_deviceInfo = device.DeviceInfo;
	}

	/// <summary>
	/// Gets the device this wrapper reopens. Fixed when it was built, and the same across every
	/// reopen — which is the point of not opening a different device in its place.
	/// </summary>
	public LuxaforDeviceDescriptor Descriptor { get; }

	/// <inheritdoc />
	/// <remarks>
	/// <c>false</c> while there is no open handle, which is the state left by a reopen that failed.
	/// It is not a liveness check any more than <see cref="ILuxaforConnection.IsConnected"/> is
	/// anywhere else — use <see cref="ILuxaforDeviceManager.IsPresent"/> for that.
	/// </remarks>
	public bool IsConnected => Current?.IsConnected == true;

	/// <inheritdoc />
	/// <remarks>Remembered here, so it survives the device underneath being replaced.</remarks>
	public LuxaforColor? LastColor
	{
		get
		{
			lock (_stateLock)
			{
				return _lastColor;
			}
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Remembered here. After a reopen the new device is asked to identify itself again; if it will
	/// not, the last known answer stands rather than becoming <c>null</c> under a caller who never
	/// asked for a different device.
	/// </remarks>
	public DeviceInfo? DeviceInfo
	{
		get
		{
			lock (_stateLock)
			{
				return _deviceInfo;
			}
		}
	}

	private ILuxaforDevice? Current
	{
		get
		{
			lock (_stateLock)
			{
				return _inner;
			}
		}
	}

	#region Commands

	/// <inheritdoc />
	public Task SetColorAsync(LuxaforColor color, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
		=> ExecuteAsync(d => d.SetColorAsync(color, target, cancellationToken), replay: true, cancellationToken);

	/// <inheritdoc />
	public Task FadeToAsync(LuxaforColor color, byte speed, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
		=> ExecuteAsync(d => d.FadeToAsync(color, speed, target, cancellationToken), replay: true, cancellationToken);

	/// <inheritdoc />
	public Task TurnOffAsync(CancellationToken cancellationToken = default)
		=> ExecuteAsync(d => d.TurnOffAsync(cancellationToken), replay: true, cancellationToken);

	/// <inheritdoc />
	/// <remarks>Reopens the device but does not replay: see the note on this class about events in time.</remarks>
	public Task StrobeAsync(LuxaforColor color, byte speed, byte repeat, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
		=> ExecuteAsync(d => d.StrobeAsync(color, speed, repeat, target, cancellationToken), replay: false, cancellationToken);

	/// <inheritdoc />
	/// <remarks>Reopens the device but does not replay: see the note on this class about events in time.</remarks>
	public Task WaveAsync(WaveType type, LuxaforColor color, byte speed, byte repeat, CancellationToken cancellationToken = default)
		=> ExecuteAsync(d => d.WaveAsync(type, color, speed, repeat, cancellationToken), replay: false, cancellationToken);

	/// <inheritdoc />
	/// <remarks>Reopens the device but does not replay: see the note on this class about events in time.</remarks>
	public Task PlayPatternAsync(BuiltInPattern pattern, byte repeat, CancellationToken cancellationToken = default)
		=> ExecuteAsync(d => d.PlayPatternAsync(pattern, repeat, cancellationToken), replay: false, cancellationToken);

	/// <inheritdoc />
	/// <remarks>Asking twice is harmless, so this is retried like a state command.</remarks>
	public Task RequestDeviceInfoAsync(CancellationToken cancellationToken = default)
		=> ExecuteAsync(d => d.RequestDeviceInfoAsync(cancellationToken), replay: true, cancellationToken);

	#endregion Commands

	/// <inheritdoc />
	/// <remarks>
	/// Runs against the device that is current when enumeration starts, and ends when that device
	/// does — a reopen elsewhere does not move a running enumeration onto the new device. Events
	/// that arrive between a disconnect and a reopen are lost either way, and silently continuing
	/// would hide that. <see cref="LuxaforEvent.DeviceIdentified"/> seen here does update
	/// <see cref="DeviceInfo"/>.
	/// </remarks>
	public async IAsyncEnumerable<LuxaforEvent> ObserveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		var device = Current
			?? await ReopenAsync(null, restoreColor: true, cancellationToken).ConfigureAwait(false)
			?? throw new LuxaforDeviceDisconnectedException(Descriptor, null);

		await foreach (var evt in device.ObserveAsync(cancellationToken).ConfigureAwait(false))
		{
			if (evt is LuxaforEvent.DeviceIdentified identified)
			{
				lock (_stateLock)
				{
					_deviceInfo = identified.Info;
				}
			}

			yield return evt;
		}
	}

	/// <summary>
	/// Runs a command, reopening the device and — for a state command — sending it again when the
	/// handle turns out to be dead.
	/// </summary>
	/// <remarks>
	/// Exactly one replay. A second failure means the device really is gone, and saying so is more
	/// use to the caller than a loop that hides it.
	/// </remarks>
	private async Task ExecuteAsync(Func<ILuxaforDevice, Task> command, bool replay, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		// Nothing open means an earlier reopen failed. Try again here rather than making the caller
		// write a retry around the thing whose job is retrying.
		var device = Current
			?? await ReopenAsync(null, restoreColor: true, cancellationToken).ConfigureAwait(false)
			?? throw new LuxaforDeviceDisconnectedException(Descriptor, null);

		try
		{
			await command(device).ConfigureAwait(false);
			Capture(device);
		}
		catch (LuxaforDeviceDisconnectedException)
		{
			// A command that will be replayed writes the remembered color itself, so restoring it
			// first would only mean sending two reports where one will do.
			var reopened = await ReopenAsync(device, restoreColor: !replay, cancellationToken).ConfigureAwait(false);

			if (!replay || reopened == null)
			{
				throw;
			}

			await command(reopened).ConfigureAwait(false);
			Capture(reopened);
		}
	}

	/// <summary>
	/// Closes a dead handle and opens the device again. Returns the new device, or <c>null</c> when
	/// it could not be opened.
	/// </summary>
	/// <param name="failed">
	/// The handle the caller found dead, or <c>null</c> when there was none. Used to tell "reopen
	/// this" from "another command already reopened while I waited".
	/// </param>
	/// <param name="restoreColor">Whether to put <see cref="LastColor"/> back on the new device.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	private async Task<ILuxaforDevice?> ReopenAsync(ILuxaforDevice? failed, bool restoreColor, CancellationToken cancellationToken)
	{
		await _reopenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var current = Current;
			if (!ReferenceEquals(current, failed))
			{
				// Someone reopened while this command waited for the gate. Their device is the
				// current one, and throwing it away to open another would be worse than useless.
				return current;
			}

			failed?.Dispose();
			SetCurrent(null);

			// By path only. Falling back to "any Luxafor" could hand the caller a different device
			// than the one their command was for.
			var result = _manager.Open(Descriptor);
			if (!result.IsSuccess)
			{
				_logger.LogDebug("Luxafor device {Device} could not be reopened: {Reason}", Descriptor, result.Description);
				return null;
			}

			var opened = result.Device!;
			SetCurrent(opened);
			_logger.LogInformation("Reopened Luxafor device {Device}.", Descriptor);

			if (!await RestoreAsync(opened, restoreColor, cancellationToken).ConfigureAwait(false))
			{
				// It went away again between opening and restoring. Let go of it, so the next
				// command opens rather than writing to a handle already known to be dead.
				opened.Dispose();
				SetCurrent(null);
				return null;
			}

			return opened;
		}
		finally
		{
			_reopenGate.Release();
		}
	}

	/// <summary>
	/// Brings a freshly opened device up to the state this wrapper describes. Returns whether it is
	/// still there afterwards.
	/// </summary>
	private async Task<bool> RestoreAsync(ILuxaforDevice device, bool restoreColor, CancellationToken cancellationToken)
	{
		try
		{
			await device.RequestDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (LuxaforDeviceDisconnectedException)
		{
			return false;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// Not every device answers, and DeviceInfo is not worth failing a reopen over — the
			// remembered value stands.
			_logger.LogDebug(ex, "Luxafor device {Device} did not identify itself after reopening.", Descriptor);
		}

		lock (_stateLock)
		{
			_deviceInfo = device.DeviceInfo ?? _deviceInfo;
		}

		if (!restoreColor)
		{
			return true;
		}

		// Sent rather than assumed: a device that was physically replugged came back dark, and
		// there is no way to tell that from a handle that merely went stale. Null means there is no
		// single color to put back — after a strobe, a wave, a pattern, or a per-LED command.
		var color = LastColor;
		if (color == null)
		{
			return true;
		}

		try
		{
			await device.SetColorAsync(color.Value, LedTarget.All, cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch (LuxaforDeviceDisconnectedException)
		{
			return false;
		}
	}

	/// <summary>
	/// Takes the state the device now describes. <see cref="ILuxaforConnection.LastColor"/> is read
	/// from it rather than worked out here, so what counts as a resting color is decided in one
	/// place — the device.
	/// </summary>
	private void Capture(ILuxaforDevice device)
	{
		lock (_stateLock)
		{
			_lastColor = device.LastColor;
			_deviceInfo = device.DeviceInfo ?? _deviceInfo;
		}
	}

	private void SetCurrent(ILuxaforDevice? device)
	{
		lock (_stateLock)
		{
			_inner = device;
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		ILuxaforDevice? device;
		lock (_stateLock)
		{
			device = _inner;
			_inner = null;
		}

		device?.Dispose();
		_reopenGate.Dispose();
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		Dispose();
		return default;
	}

	private void ThrowIfDisposed()
	{
#if NET8_0_OR_GREATER
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
#else
		if (Volatile.Read(ref _disposed) == 1)
		{
			throw new ObjectDisposedException(GetType().FullName);
		}
#endif
	}
}
