using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface IMibRepository
{
    /// <summary>Load and parse one or more MIB files or a folder of MIBs.</summary>
    Task<(int Loaded, int Failed, List<string> Errors)> ImportAsync(IEnumerable<string> paths, CancellationToken ct = default);

    /// <summary>Look up a node by its exact numeric OID.</summary>
    MibNode? Lookup(string numericOid);

    /// <summary>Look up by longest prefix match (e.g. .1.3.6.1.2.1.1.1 → sysDescr).</summary>
    MibNode? LongestPrefixMatch(string numericOid);

    IReadOnlyList<MibNode> AllNodes { get; }

    int Count { get; }
}
