using SNMP.Core.Enums;

namespace SNMP.Core.Models;

/// <summary>
/// A host found during subnet discovery.
/// SourceSubnet is populated by DiscoveryService with the CIDR block that
/// produced this host — used by the ViewModel to group results by location.
/// </summary>
public sealed class DiscoveredHost
{
    public string      IpAddress     { get; set; } = string.Empty;
    public string      Hostname      { get; set; } = string.Empty;
    public string      SysDescr      { get; set; } = string.Empty;
    public string      SysName       { get; set; } = string.Empty;
    public SnmpVersion BestVersion   { get; set; }
    public string      BestCommunity { get; set; } = string.Empty;
    public bool        Reachable     { get; set; }
    public string      DeviceType    { get; set; } = "Unknown";
    public int         ResponseMs    { get; set; }

    /// <summary>
    /// The CIDR block this host was discovered in (e.g. "192.168.10.0/24").
    /// Set by DiscoveryService so the ViewModel can group by subnet / location.
    /// </summary>
    public string SourceSubnet { get; set; } = string.Empty;
}
