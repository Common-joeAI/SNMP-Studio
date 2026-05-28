using SNMP.Core.Enums;
using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface ILogService
{
    event Action<LogEntry>? EntryAdded;

    void Log(LogLevel level, string message, string? details = null);
    void Info(string msg, string? details = null);
    void Warn(string msg, string? details = null);
    void Error(string msg, string? details = null);
    void Debug(string msg, string? details = null);

    IReadOnlyList<LogEntry> Entries { get; }
    void Clear();
}
