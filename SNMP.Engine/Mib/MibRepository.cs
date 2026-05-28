using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Mib;

/// <summary>
/// Loads and caches MIB files (RFC-standard ASN.1 text format).
///
/// PERSISTENCE
/// ───────────
/// Parsed nodes are serialised to a JSON cache at:
///   %APPDATA%\SNMPStudio\mib_cache.json
///
/// On subsequent launches LoadCacheAsync() restores the full node set in
/// milliseconds — no re-parsing needed.  Newly imported files are merged and
/// the cache is re-written automatically.
///
/// PARSER SCOPE
/// ────────────
/// Pragmatic regex-based extractor. Covers the DEFINITIONS block style used
/// by RFC MIBs and most vendor MIBs (HP, Cisco, Xerox, Ricoh …).
/// Does NOT resolve multi-file IMPORTS chains; the OidTranslator falls back
/// to its built-in RFC OID table for those edges.
/// </summary>
public sealed class MibRepository : IMibRepository
{
    // ── In-memory stores ──────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, MibNode>       _byOid    = new();
    private readonly ConcurrentDictionary<string, MibNode>       _byName   = new();
    private readonly ConcurrentDictionary<string, MibModuleInfo> _modules  = new();

    // ── Cache path ────────────────────────────────────────────────────────────
    private static readonly string CacheDir  =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SNMPStudio");
    private static readonly string CachePath = Path.Combine(CacheDir, "mib_cache.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented        = false,   // compact — can be large with vendor MIBs
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── Public surface ────────────────────────────────────────────────────────
    public IReadOnlyList<MibNode>       AllNodes      => _byOid.Values.ToList();
    public IReadOnlyList<MibModuleInfo> LoadedModules => _modules.Values.OrderBy(m => m.ModuleName).ToList();
    public int Count => _byOid.Count;

    // ─────────────────────────────────────────────────────────────────────────
    // Cache I/O
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restore nodes from the on-disk JSON cache.
    /// Returns the number of nodes loaded (0 if no cache exists).
    /// </summary>
    public async Task<int> LoadCacheAsync(CancellationToken ct = default)
    {
        if (!File.Exists(CachePath)) return 0;

        try
        {
            await using var fs   = File.OpenRead(CachePath);
            var             data = await JsonSerializer.DeserializeAsync<CacheFile>(fs, JsonOpts, ct);
            if (data?.Nodes == null) return 0;

            foreach (var node in data.Nodes)
            {
                _byOid[node.NumericOid] = node;
                _byName[node.Name]      = node;
            }

            // Rebuild module summaries from cache metadata
            if (data.Modules != null)
            {
                foreach (var m in data.Modules)
                {
                    m.FromCache = true;
                    _modules[m.ModuleName] = m;
                }
            }
            else
            {
                // Legacy cache — no module metadata, reconstruct from nodes
                RebuildModulesFromNodes(fromCache: true);
            }

            return data.Nodes.Count;
        }
        catch
        {
            // Corrupt cache — delete and continue fresh
            try { File.Delete(CachePath); } catch { /* ignore */ }
            return 0;
        }
    }

    /// <summary>
    /// Serialise current nodes + module metadata to disk.
    /// Written atomically via a temp file to avoid corruption on crash.
    /// </summary>
    public async Task SaveCacheAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheDir);

        var payload = new CacheFile
        {
            SavedAt = DateTime.UtcNow,
            Nodes   = _byOid.Values.ToList(),
            Modules = _modules.Values.ToList()
        };

        var tmp = CachePath + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, payload, JsonOpts, ct);

        File.Move(tmp, CachePath, overwrite: true);
    }

    /// <summary>Wipe all nodes and delete the cache file.</summary>
    public async Task ClearAllAsync()
    {
        _byOid.Clear();
        _byName.Clear();
        _modules.Clear();
        await Task.Run(() =>
        {
            if (File.Exists(CachePath)) File.Delete(CachePath);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Import
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<(int Loaded, int Failed, List<string> Errors)> ImportAsync(
        IEnumerable<string> paths, CancellationToken ct = default)
    {
        var loaded = 0;
        var failed = 0;
        var errors = new List<string>();

        var allFiles = ExpandPaths(paths).ToList();

        // Per-file results collected safely across threads
        var perFile = new ConcurrentBag<(string Module, string Path, List<MibNode> Nodes)>();

        await Parallel.ForEachAsync(allFiles,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 },
            async (file, token) =>
            {
                try
                {
                    var text   = await File.ReadAllTextAsync(file, token);
                    var module = Path.GetFileNameWithoutExtension(file);
                    var nodes  = ParseMib(text, module);
                    perFile.Add((module, file, nodes));
                    Interlocked.Add(ref loaded, nodes.Count);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    lock (errors) errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            });

        // Merge into dictionaries and update module info
        foreach (var (module, path, nodes) in perFile)
        {
            foreach (var node in nodes)
            {
                _byOid[node.NumericOid] = node;
                _byName[node.Name]      = node;
            }

            _modules[module] = new MibModuleInfo
            {
                ModuleName = module,
                SourcePath = path,
                NodeCount  = nodes.Count,
                FromCache  = false,
                ImportedAt = DateTime.Now
            };
        }

        // Persist cache after every successful import
        if (loaded > 0)
            await SaveCacheAsync(ct);

        return (loaded, failed, errors);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Lookup
    // ─────────────────────────────────────────────────────────────────────────

    public MibNode? Lookup(string numericOid) =>
        _byOid.TryGetValue(NormalizeOid(numericOid), out var n) ? n : null;

    /// <summary>
    /// Finds the MIB node whose numeric OID is the longest prefix of the queried OID.
    /// e.g. querying .1.3.6.1.2.1.1.1.0 returns sysDescr.
    /// </summary>
    public MibNode? LongestPrefixMatch(string numericOid)
    {
        var normalized = NormalizeOid(numericOid);
        MibNode? best  = null;
        var bestLen    = 0;

        foreach (var kv in _byOid)
        {
            if (normalized.StartsWith(kv.Key, StringComparison.Ordinal)
                && kv.Key.Length > bestLen)
            {
                best    = kv.Value;
                bestLen = kv.Key.Length;
            }
        }
        return best;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Module management
    // ─────────────────────────────────────────────────────────────────────────

    public async Task RemoveModuleAsync(string moduleName)
    {
        // Remove all nodes from this module
        var toRemove = _byOid.Values.Where(n => n.ModuleName == moduleName).ToList();
        foreach (var n in toRemove)
        {
            _byOid.TryRemove(n.NumericOid, out _);
            _byName.TryRemove(n.Name, out _);
        }
        _modules.TryRemove(moduleName, out _);

        await SaveCacheAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Parser
    // ─────────────────────────────────────────────────────────────────────────

    private static List<MibNode> ParseMib(string text, string moduleName)
    {
        var nodes = new List<MibNode>();

        // OBJECT IDENTIFIER assignments — build parent→OID map
        var oidPattern = new Regex(
            @"(\w+)\s+OBJECT\s+IDENTIFIER\s*::=\s*\{\s*(\w+)\s+(\d+)\s*\}",
            RegexOptions.IgnoreCase);

        // Seed with well-known roots + common vendor OID parents
        var nameToOid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["iso"]          = "1",
            ["org"]          = "1.3",
            ["dod"]          = "1.3.6",
            ["internet"]     = "1.3.6.1",
            ["mgmt"]         = "1.3.6.1.2",
            ["mib-2"]        = "1.3.6.1.2.1",
            ["transmission"] = "1.3.6.1.2.1.10",
            ["enterprises"]  = "1.3.6.1.4.1",
            ["experimental"] = "1.3.6.1.3",
            ["private"]      = "1.3.6.1.4",
            // HP / Hewlett-Packard
            ["hp"]           = "1.3.6.1.4.1.11",
            ["nm"]           = "1.3.6.1.4.1.11.2",
            ["hpnpSNMP"]     = "1.3.6.1.4.1.11.2.3.9.4",
            // Cisco
            ["cisco"]        = "1.3.6.1.4.1.9",
            // Xerox
            ["xerox"]        = "1.3.6.1.4.1.253",
            // Ricoh
            ["ricoh"]        = "1.3.6.1.4.1.367",
            // Kyocera
            ["kyocera"]      = "1.3.6.1.4.1.1347",
            // Brother
            ["brother"]      = "1.3.6.1.4.1.2435",
            // Lexmark
            ["lexmark"]      = "1.3.6.1.4.1.641",
            // Sharp
            ["sharp"]        = "1.3.6.1.4.1.822",
        };

        foreach (Match m in oidPattern.Matches(text))
        {
            var name   = m.Groups[1].Value;
            var parent = m.Groups[2].Value;
            var id     = m.Groups[3].Value;
            if (nameToOid.TryGetValue(parent, out var parentOid))
                nameToOid[name] = $"{parentOid}.{id}";
        }

        // OBJECT-TYPE definitions
        var objPattern = new Regex(
            @"(\w+)\s+OBJECT-TYPE\s+.*?SYNTAX\s+(?<syntax>[^\r\n]+).*?" +
            @"(?:MAX-ACCESS|ACCESS)\s+(?<access>[^\r\n]+).*?" +
            @"(?:DESCRIPTION\s+""(?<desc>(?:[^""]|"""")*)""\s*)?" +
            @"::=\s*\{\s*(?<parent>\w+)\s+(?<id>\d+)\s*\}",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

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
                NumericOid  = numericOid,
                Name        = name,
                ModuleName  = moduleName,
                Description = desc.Length > 400 ? desc[..400] + "…" : desc,
                Syntax      = syntax,
                Access      = access
            };

            // Extract INTEGER enum values: { up(1) down(2) ... }
            var enumMatch = Regex.Match(text,
                $@"{Regex.Escape(name)}\s+OBJECT-TYPE.*?SYNTAX\s+INTEGER\s*\{{(?<enums>[^}}]+)\}}",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (enumMatch.Success)
            {
                foreach (Match em in Regex.Matches(
                    enumMatch.Groups["enums"].Value, @"(\w+)\((\d+)\)"))
                    node.Enumerations[int.Parse(em.Groups[2].Value)] = em.Groups[1].Value;
            }

            nodes.Add(node);
            nameToOid[name] = numericOid;   // allow later nodes in same file to reference this one
        }

        return nodes;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                                           .Where(f => !f.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)))
                    yield return f;
            else if (File.Exists(p))
                yield return p;
        }
    }

    private void RebuildModulesFromNodes(bool fromCache)
    {
        var groups = _byOid.Values.GroupBy(n => n.ModuleName);
        foreach (var g in groups)
        {
            _modules[g.Key] = new MibModuleInfo
            {
                ModuleName = g.Key,
                NodeCount  = g.Count(),
                FromCache  = fromCache,
                ImportedAt = DateTime.Now
            };
        }
    }

    private static string NormalizeOid(string oid) =>
        oid.StartsWith('.') ? oid[1..] : oid;

    // ─────────────────────────────────────────────────────────────────────────
    // Cache file DTO
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class CacheFile
    {
        public DateTime          SavedAt { get; set; }
        public List<MibNode>     Nodes   { get; set; } = new();
        public List<MibModuleInfo> Modules { get; set; } = new();
    }
}
