using SNMP.Core.Enums;

namespace SNMP.Core.Models;

/// <summary>A host found during subnet discovery.</summary>
public sealed class DiscoveredHost
{
    public string      IpAddress    { get; set; } = string.Empty;
    public string      Hostname     { get; set; } = string.Empty;
    public string      SysDescr     { get; set; } = string.Empty;
    public string      SysName      { get; set; } = string.Empty;
    public SnmpVersion BestVersion  { get; set; }
    public string      BestCommunity { get; set; } = string.Empty;
    public bool        Reachable    { get; set; }
    public string      DeviceType   { get; set; } = "Unknown";  // Printer, Switch, Router…
    public int         ResponseMs   { get; set; }
}
