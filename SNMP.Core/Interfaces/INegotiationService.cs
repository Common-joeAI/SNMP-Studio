using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface INegotiationService
{
    /// <summary>
    /// Probe a host across v2c → v1 → v3 (noAuthNoPriv) to find a working configuration.
    /// Returns a report with the winning version and community, or a friendly failure reason.
    /// </summary>
    Task<NegotiationReport> NegotiateAsync(string host, int port = 161,
        IEnumerable<string>? communitiesToTry = null, CancellationToken ct = default);
}
