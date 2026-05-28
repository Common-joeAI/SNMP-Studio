using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface IDiscoveryService
{
    IAsyncEnumerable<DiscoveredHost> DiscoverAsync(
        string cidr,
        IEnumerable<string> communities,
        int timeoutMs = 1500,
        CancellationToken ct = default);
}
