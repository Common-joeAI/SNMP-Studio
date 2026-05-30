namespace SNMP.Core.Models;

/// <summary>
/// Lightweight summary of a loaded MIB module — shown in the MIB Manager panel.
/// </summary>
public sealed class MibModuleInfo
{
    public string ModuleName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;  // original file path (empty if from cache)
    public int    NodeCount  { get; set; }
    public bool   FromCache  { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.Now;

    public string Summary => $"{NodeCount} nodes{(FromCache ? " (cached)" : "")}";
}
