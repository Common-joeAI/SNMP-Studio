using SNMP.Core.Enums;

namespace SNMP.Core.Models;

/// <summary>Bookmarked device — persisted to %APPDATA%\SNMPStudio\targets.json.</summary>
public sealed class SavedTarget
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Label       { get; set; } = string.Empty;   // friendly name
    public string Host        { get; set; } = string.Empty;
    public int    Port        { get; set; } = 161;
    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;
    public string Community   { get; set; } = "public";
    public string SecurityName { get; set; } = string.Empty;  // v3
    public AuthProtocol AuthProtocol { get; set; } = AuthProtocol.None;
    public PrivProtocol PrivProtocol { get; set; } = PrivProtocol.None;
    public string ContextName { get; set; } = string.Empty;
    public DateTime LastUsed  { get; set; } = DateTime.Now;
    public string Icon        { get; set; } = "🖧";            // emoji icon
}
