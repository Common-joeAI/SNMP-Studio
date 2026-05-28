using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Services;

public sealed class ExportService : IExportService
{
    public async Task ExportCsvAsync(IEnumerable<SnmpResult> results, string filePath, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("OID,SymbolicName,Description,RawValue,HumanValue,Type,IsWritable");

        foreach (var r in results)
        {
            ct.ThrowIfCancellationRequested();
            sb.AppendLine($"{Q(r.Oid)},{Q(r.SymbolicName)},{Q(r.Description)},{Q(r.RawValue)},{Q(r.HumanValue)},{Q(r.Type)},{r.IsWritable}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
    }

    public async Task ExportJsonAsync(IEnumerable<SnmpResult> results, string filePath, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8, ct);
    }

    public async Task<string> ExportDiagnosticsBundleAsync(
        IEnumerable<SnmpResult> results,
        IEnumerable<LogEntry> logs,
        SnmpTarget target,
        string outputDir,
        CancellationToken ct = default)
    {
        var ts       = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipPath  = Path.Combine(outputDir, $"SNMP_Diagnostics_{ts}.zip");
        var tmpDir   = Path.Combine(Path.GetTempPath(), $"snmp_diag_{ts}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            // Results CSV
            await ExportCsvAsync(results, Path.Combine(tmpDir, "results.csv"), ct);

            // Logs text
            var logText = string.Join(Environment.NewLine, logs.Select(l => l.ToString()));
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "session.log"), logText, ct);

            // Sanitized config — NO secrets
            var cfg = new
            {
                Host          = target.Host,
                Port          = target.Port,
                Version       = target.Version.ToString(),
                Community     = "[REDACTED]",
                WriteCommunity = "[REDACTED]",
                SecurityName  = target.SecurityName,   // username is not a secret
                AuthProtocol  = target.AuthProtocol.ToString(),
                PrivProtocol  = target.PrivProtocol.ToString(),
                AuthPassword  = "[REDACTED]",
                PrivPassword  = "[REDACTED]",
                ContextName   = target.ContextName,
                ExportedAt    = DateTime.UtcNow
            };
            var cfgJson = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(tmpDir, "config.json"), cfgJson, ct);

            // ZIP everything
            ZipFile.CreateFromDirectory(tmpDir, zipPath);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }

        return zipPath;
    }

    private static string Q(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
}
