using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface ISnmpClient
{
    Task<(SnmpResult? Result, string? Error)> GetAsync(SnmpTarget target, string oid, CancellationToken ct = default);

    IAsyncEnumerable<SnmpResult> WalkAsync(SnmpTarget target, string rootOid, CancellationToken ct = default);

    Task<(bool Success, string? Error)> SetAsync(SnmpTarget target, string oid, string type, string value, CancellationToken ct = default);
}
