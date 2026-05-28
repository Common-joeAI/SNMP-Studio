using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Mib;

/// <summary>
/// Loads and caches MIB files (RFC-standard ASN.1 text format).
/// The parser is a pragmatic regex-based extractor — it handles the
/// common DEFINITIONS block style used by RFC MIBs and most vendor MIBs.
///
/// Limitations: does not handle IMPORTS resolution chains or complex TEXTUAL-CONVENTIONs
/// across modules. For those, the OidTranslator falls back to the built-in standard OID table.
/// </summary>
public sealed class MibRepository : IMibRepository
{
    private readonly ConcurrentDictionary<string, MibNode> _byOid  = new();
    private readonly ConcurrentDictionary<string, MibNode> _byName = new();

    public IReadOnlyList<MibNode> AllNodes => _byOid.Values.ToList();
    public int Count => _byOid.Count;

    public async Task<(int Loaded, int Failed, List<string> Errors)> ImportAsync(
        IEnumerable<string> paths, CancellationToken ct = default)
    {
        var loaded = 0; var failed = 0;
        var errors = new List<string>();

        var allFiles = ExpandPaths(paths);

        await Parallel.ForEachAsync(allFiles, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 },
            async (file, token) =>
            {
                try
                {
                    var text = await File.ReadAllTextAsync(file, token);
                    var nodes = ParseMib(text, Path.GetFileNameWithoutExtension(file));
                    foreach (var node in nodes)
                    {
                        _byOid[node.NumericOid] = node;
                        _byName[node.Name]       = node;
                    }
                    Interlocked.Add(ref loaded, nodes.Count);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    lock (errors) errors.Add($"{file}: {ex.Message}");
                }
            });

        return (loaded, failed, errors);
    }

    public MibNode? Lookup(string numericOid) =>
        _byOid.TryGetValue(NormalizeOid(numericOid), out var n) ? n : null;

    /// <summary>
    /// Finds the MIB node whose numeric OID is the longest prefix of the queried OID.
    /// e.g. querying .1.3.6.1.2.1.1.1.0 returns the node for .1.3.6.1.2.1.1.1 (sysDescr).
    /// This lets us resolve instance-qualified OIDs even when only the base OID is in the MIB.
    /// </summary>
    public MibNode? LongestPrefixMatch(string numericOid)
    {
        var normalized = NormalizeOid(numericOid);
        MibNode? best = null;
        var bestLen = 0;

        foreach (var kv in _byOid)
        {
            if (normalized.StartsWith(kv.Key) && kv.Key.Length > bestLen)
            {
                best    = kv.Value;
                bestLen = kv.Key.Length;
            }
        }
        return best;
    }

    // ── Parser ───────────────────────────────────────────────────────────────

    private static List<MibNode> ParseMib(string text, string moduleName)
    {
        var nodes = new List<MibNode>();

        // Match OBJECT-TYPE definitions:
        // <name> OBJECT-TYPE ... DESCRIPTION "..." ... ::= { <parent> <id> }
        var objPattern = new Regex(
            @"(\w+)\s+OBJECT-TYPE\s+.*?SYNTAX\s+(?<syntax>[^\r\n]+).*?" +
            @"(?:MAX-ACCESS|ACCESS)\s+(?<access>[^\r\n]+).*?" +
            @"(?:DESCRIPTION\s+""(?<desc>(?:[^""]|"""")*)""\s*)?" +
            @"::=\s*\{\s*(?<parent>\w+)\s+(?<id>\d+)\s*\}",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Also match OBJECT IDENTIFIER assignments:
        // <name> OBJECT IDENTIFIER ::= { <parent> <id> }
        var oidPattern = new Regex(
            @"(\w+)\s+OBJECT\s+IDENTIFIER\s*::=\s*\{\s*(\w+)\s+(\d+)\s*\}",
            RegexOptions.IgnoreCase);

        // Build a name→oid map from OBJECT IDENTIFIER assignments for parent resolution
        var nameToOid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Seed with well-known roots
            ["iso"]            = "1",
            ["org"]            = "1.3",
            ["dod"]            = "1.3.6",
            ["internet"]       = "1.3.6.1",
            ["mgmt"]           = "1.3.6.1.2",
            ["mib-2"]          = "1.3.6.1.2.1",
            ["enterprises"]    = "1.3.6.1.4.1",
            ["experimental"]   = "1.3.6.1.3",
            ["private"]        = "1.3.6.1.4",
        };

        foreach (Match m in oidPattern.Matches(text))
        {
            var name   = m.Groups[1].Value;
            var parent = m.Groups[2].Value;
            var id     = m.Groups[3].Value;
            if (nameToOid.TryGetValue(parent, out var parentOid))
                nameToOid[name] = $"{parentOid}.{id}";
        }

        foreach (Match m in objPattern.Matches(text))
        {
            var name   = m.Groups[1].Value;
            var parent = m.Groups["parent"].Value;
            var id     = m.Groups["id"].Value;
            var desc   = m.Groups["desc"].Value.Trim().Replace("\"\"", "\"");
            var syntax = m.Groups["syntax"].Value.Trim();
            var access = m.Groups["access"].Value.Trim();

            if (!nameToOid.TryGetValue(parent, out var parentOid)) continue;
            var numericOid = $"{parentOid}.{id}";

            var node = new MibNode
            {
                NumericOid   = numericOid,
                Name         = name,
                ModuleName   = moduleName,
                Description  = desc.Length > 300 ? desc[..300] + "…" : desc,
                Syntax       = syntax,
                Access       = access
            };

            // Extract INTEGER enum values: { up(1) down(2) ... }
            var enumMatch = Regex.Match(text,
                $@"{Regex.Escape(name)}\s+OBJECT-TYPE.*?SYNTAX\s+INTEGER\s*\{{(?<enums>[^}}]+)\}}",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (enumMatch.Success)
            {
                foreach (Match em in Regex.Matches(enumMatch.Groups["enums"].Value, @"(\w+)\((\d+)\)"))
                    node.Enumerations[int.Parse(em.Groups[2].Value)] = em.Groups[1].Value;
            }

            nodes.Add(node);
            nameToOid[name] = numericOid;
        }

        return nodes;
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                                           .Where(f => !f.EndsWith(".gz")))
                    yield return f;
            else if (File.Exists(p))
                yield return p;
        }
    }

    private static string NormalizeOid(string oid) =>
        oid.StartsWith('.') ? oid[1..] : oid;
}
