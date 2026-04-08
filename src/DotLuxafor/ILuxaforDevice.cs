namespace DotLuxafor;

/// <summary>
/// Unified interface for a Luxafor LED device providing commands, connection lifecycle, and monitoring.
/// </summary>
public interface ILuxaforDevice : ILuxaforCommands, ILuxaforConnection, ILuxaforMonitor
{
}