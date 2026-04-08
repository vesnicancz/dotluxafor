namespace DotLuxafor;

/// <summary>
/// Configuration options for the Luxafor device integration.
/// </summary>
public sealed class LuxaforOptions
{
    /// <summary>
    /// Whether to automatically reconnect when the device is disconnected.
    /// Default is <c>false</c>.
    /// </summary>
    public bool AutoReconnect { get; set; }

    /// <summary>
    /// Delay between reconnection attempts. Must be positive.
    /// Default is 3 seconds.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Whether to start monitoring input reports automatically when the device connects.
    /// Default is <c>false</c>.
    /// </summary>
    public bool AutoMonitor { get; set; }
}
