using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface IDiscoveryService
{
    /// <summary>
    /// Scan one or more CIDR blocks. Each result is tagged with its SourceSubnet
    /// so the ViewModel can group results by subnet / location.
    ///
    /// cidrList accepts:
    ///   • Single CIDR         "192.168.1.0/24"
    ///   • Comma-separated     "192.168.1.0/24, 10.10.5.0/24"
    ///   • Labeled entries     "Floor 1=192.168.1.0/24, Floor 2=192.168.2.0/24"
    ///
    /// Each DiscoveredHost.SourceSubnet is set to the raw CIDR (without label).
    /// </summary>
    IAsyncEnumerable<DiscoveredHost> DiscoverAsync(
        string cidrList,
        IEnumerable<string> communities,
        int timeoutMs = 1500,
        CancellationToken ct = default);
}
