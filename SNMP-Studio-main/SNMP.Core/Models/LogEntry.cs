using SNMP.Core.Enums;

namespace SNMP.Core.Models;

public sealed class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] [{Level,-5}] {Message}{(Details != null ? $"\n  {Details}" : "")}";
}
