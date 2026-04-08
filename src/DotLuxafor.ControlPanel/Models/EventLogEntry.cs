namespace DotLuxafor.ControlPanel.Models;

public record EventLogEntry(DateTime Timestamp, string Message)
{
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
}
