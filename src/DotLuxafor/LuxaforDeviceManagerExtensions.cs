using Microsoft.Extensions.Logging;

namespace DotLuxafor;

/// <summary>
/// Opening a device that puts itself back together after a disconnect.
/// </summary>
public static class LuxaforDeviceManagerExtensions
{
	/// <summary>
	/// Opens a device wrapped in <see cref="ReconnectingLuxaforDevice"/>, so a command is not lost
	/// to a handle that went stale.
	/// </summary>
	/// <param name="manager">The manager to open with, and to reopen with afterwards.</param>
	/// <param name="logger">Where each reopen is reported, or <c>null</c> to report nowhere.</param>
	/// <returns>
	/// The outcome of the initial open, with a reconnecting device in
	/// <see cref="DeviceOpenResult.Device"/>. A failure is passed through unchanged: there is
	/// nothing to reconnect to yet, and hiding that behind a wrapper would only move the problem to
	/// the first command.
	/// </returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="manager"/> is <c>null</c>.</exception>
	public static DeviceOpenResult OpenReconnecting(this ILuxaforDeviceManager manager, ILogger? logger = null)
	{
		if (manager == null)
		{
			throw new ArgumentNullException(nameof(manager));
		}

		return Wrap(manager, manager.Open(), logger);
	}

	/// <summary>
	/// Opens a specific device wrapped in <see cref="ReconnectingLuxaforDevice"/>.
	/// </summary>
	/// <param name="manager">The manager to open with, and to reopen with afterwards.</param>
	/// <param name="descriptor">The device to open, from <see cref="ILuxaforDeviceManager.List"/>.</param>
	/// <param name="logger">Where each reopen is reported, or <c>null</c> to report nowhere.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="manager"/> or <paramref name="descriptor"/> is <c>null</c>.</exception>
	public static DeviceOpenResult OpenReconnecting(this ILuxaforDeviceManager manager, LuxaforDeviceDescriptor descriptor, ILogger? logger = null)
	{
		if (manager == null)
		{
			throw new ArgumentNullException(nameof(manager));
		}

		if (descriptor == null)
		{
			throw new ArgumentNullException(nameof(descriptor));
		}

		return Wrap(manager, manager.Open(descriptor), logger);
	}

	private static DeviceOpenResult Wrap(ILuxaforDeviceManager manager, DeviceOpenResult result, ILogger? logger)
	{
		if (!result.IsSuccess)
		{
			return result;
		}

		var device = new ReconnectingLuxaforDevice(manager, result.Device!, logger);
		return DeviceOpenResult.Opened(device, result.Descriptor!);
	}
}
