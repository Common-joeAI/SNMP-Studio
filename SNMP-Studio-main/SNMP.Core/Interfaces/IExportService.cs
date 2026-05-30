using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface IExportService
{
    Task ExportCsvAsync(IEnumerable<SnmpResult> results, string filePath, CancellationToken ct = default);
    Task ExportJsonAsync(IEnumerable<SnmpResult> results, string filePath, CancellationToken ct = default);

    /// <summary>
    /// Bundle logs + results + sanitized config (no secrets) into a ZIP for support/diagnostics.
    /// </summary>
    Task<string> ExportDiagnosticsBundleAsync(IEnumerable<SnmpResult> results,
        IEnumerable<LogEntry> logs, SnmpTarget target, string outputDir, CancellationToken ct = default);
}
