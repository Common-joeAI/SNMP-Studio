using SNMP.Core.Enums;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Services;

public sealed class LogService : ILogService
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public event Action<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> Entries { get { lock (_lock) return _entries.ToList(); } }

    public void Log(LogLevel level, string message, string? details = null)
    {
        var entry = new LogEntry { Level = level, Message = message, Details = details };
        Action<LogEntry>? handler;
        lock (_lock)
        {
            _entries.Add(entry);
            handler = EntryAdded;   // snapshot delegate inside lock
        }
        // Invoke outside lock to avoid deadlock if handler re-enters Log()
        handler?.Invoke(entry);
    }

    public void Info(string msg, string? details = null)  => Log(LogLevel.Info,  msg, details);
    public void Warn(string msg, string? details = null)  => Log(LogLevel.Warn,  msg, details);
    public void Error(string msg, string? details = null) => Log(LogLevel.Error, msg, details);
    public void Debug(string msg, string? details = null) => Log(LogLevel.Debug, msg, details);

    public void Clear() { lock (_lock) _entries.Clear(); }
}
