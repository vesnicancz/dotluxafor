using DotLuxafor;

namespace DotLuxafor.Tests;

/// <summary>
/// A device that can be told to have gone away, and that records what was sent to it.
/// </summary>
/// <remarks>
/// Hand-written rather than mocked because these tests are about state — what
/// <see cref="ILuxaforConnection.LastColor"/> is after a command, and what a freshly opened device
/// reports before anyone asks it anything. That is the behaviour
/// <see cref="ReconnectingLuxaforDevice"/> has to carry across a reopen, so the double has to have
/// it, not just record calls.
/// </remarks>
internal sealed class FakeLuxaforDevice : ILuxaforDevice
{
	public FakeLuxaforDevice(LuxaforDeviceDescriptor? descriptor = null)
	{
		Descriptor = descriptor;
	}

	/// <summary>When set, every command throws as though the device had gone away.</summary>
	public bool HasGoneAway { get; set; }

	/// <summary>When set, commands wait on it before doing anything — for exercising concurrency.</summary>
	public TaskCompletionSource<bool>? Gate { get; set; }

	/// <summary>What the device answers <see cref="RequestDeviceInfoAsync"/> with.</summary>
	public DeviceInfo? IdentifiesAs { get; set; }

	/// <summary>When set, <see cref="RequestDeviceInfoAsync"/> throws it.</summary>
	public Exception? IdentifyError { get; set; }

	/// <summary>Events <see cref="ObserveAsync"/> yields, in order.</summary>
	public List<LuxaforEvent> Events { get; } = new List<LuxaforEvent>();

	/// <summary>Every command that reached the device, in order.</summary>
	public List<string> Commands { get; } = new List<string>();

	public bool IsDisposed { get; private set; }

	public LuxaforDeviceDescriptor? Descriptor { get; }

	public bool IsConnected => !IsDisposed && !HasGoneAway;

	public LuxaforColor? LastColor { get; private set; }

	public DeviceInfo? DeviceInfo { get; private set; }

	public Task SetColorAsync(LuxaforColor color, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
		=> RunAsync($"SetColor({color},{target})", () => RecordRestingColor(color, target));

	public Task FadeToAsync(LuxaforColor color, byte speed, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
		=> RunAsync($"FadeTo({color},{speed},{target})", () => RecordRestingColor(color, target));

	public Task StrobeAsync(LuxaforColor color, byte speed, byte repeat, LedTarget target = LedTarget.All, CancellationToken cancellationToken = default)
		=> RunAsync($"Strobe({color},{speed},{repeat},{target})", () => LastColor = null);

	public Task WaveAsync(WaveType type, LuxaforColor color, byte speed, byte repeat, CancellationToken cancellationToken = default)
		=> RunAsync($"Wave({type},{color},{speed},{repeat})", () => LastColor = null);

	public Task PlayPatternAsync(BuiltInPattern pattern, byte repeat, CancellationToken cancellationToken = default)
		=> RunAsync($"PlayPattern({pattern},{repeat})", () => LastColor = null);

	public Task TurnOffAsync(CancellationToken cancellationToken = default)
		=> SetColorAsync(LuxaforColor.Off, LedTarget.All, cancellationToken);

	public async Task RequestDeviceInfoAsync(CancellationToken cancellationToken = default)
	{
		await RunAsync("RequestDeviceInfo", null).ConfigureAwait(false);

		if (IdentifyError != null)
		{
			throw IdentifyError;
		}

		DeviceInfo = IdentifiesAs;
	}

	public async IAsyncEnumerable<LuxaforEvent> ObserveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		foreach (var evt in Events)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await Task.Yield();
			yield return evt;
		}
	}

	public void Dispose() => IsDisposed = true;

	public ValueTask DisposeAsync()
	{
		Dispose();
		return default;
	}

	/// <summary>Mirrors the real device: only a whole-device command leaves one resting color.</summary>
	private void RecordRestingColor(LuxaforColor color, LedTarget target)
		=> LastColor = target == LedTarget.All ? color : (LuxaforColor?)null;

	private async Task RunAsync(string command, Action? onSuccess)
	{
		if (Gate != null)
		{
			await Gate.Task.ConfigureAwait(false);
		}

		if (IsDisposed)
		{
			throw new ObjectDisposedException(nameof(FakeLuxaforDevice));
		}

		if (HasGoneAway)
		{
			throw new LuxaforDeviceDisconnectedException(Descriptor, new IOException("The device was disconnected."));
		}

		Commands.Add(command);
		onSuccess?.Invoke();
	}
}
