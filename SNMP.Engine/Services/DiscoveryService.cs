using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using SNMP.Core.Enums;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Services;

/// <summary>
/// Scans one or more CIDR subnets, pings each host, then probes SNMP.
/// Results are tagged with SourceSubnet so the ViewModel can group them
/// by location / building / floor.
///
/// Input format (cidrList parameter):
///   Single:    "192.168.1.0/24"
///   Multi:     "192.168.1.0/24, 10.10.5.0/24"
///   Labeled:   "Floor 1=192.168.1.0/24, Floor 2=192.168.2.0/24"
///   Mixed:     "Lobby=192.168.10.0/24, 10.0.0.0/24"
///
/// Results stream in as they complete — no waiting for a full scan.
/// </summary>
public sealed class DiscoveryService : IDiscoveryService
{
    private static readonly string[] ProbeOids =
    {
        "1.3.6.1.2.1.1.1.0",   // sysDescr
        "1.3.6.1.2.1.1.5.0",   // sysName
    };

    // ── Public entry point ───────────────────────────────────────────────────

    public async IAsyncEnumerable<DiscoveredHost> DiscoverAsync(
        string cidrList,
        IEnumerable<string> communities,
        int timeoutMs = 1500,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var subnets       = ParseCidrList(cidrList);
        var communityList = communities.ToList();
        if (communityList.Count == 0) communityList.Add("public");

        // Unbounded channel — results stream in as they arrive
        var channel = Channel.CreateUnbounded<DiscoveredHost>(
            new UnboundedChannelOptions { SingleReader = true });

        // Fan-out: scan all subnets concurrently
        var scanTask = Task.Run(async () =>
        {
            // Each subnet gets its own parallel scan; all run simultaneously
            var subnetTasks = subnets.Select(entry =>
                ScanSubnetAsync(entry.Cidr, communityList, timeoutMs, channel.Writer, ct));

            await Task.WhenAll(subnetTasks);
            channel.Writer.Complete();
        }, ct);

        await foreach (var host in channel.Reader.ReadAllAsync(ct))
            yield return host;

        await scanTask;
    }

    // ── Per-subnet scanner ───────────────────────────────────────────────────

    private static async Task ScanSubnetAsync(
        string cidr,
        List<string> communities,
        int timeoutMs,
        ChannelWriter<DiscoveredHost> writer,
        CancellationToken ct)
    {
        var ips = ExpandCidr(cidr).ToList();

        await Parallel.ForEachAsync(ips,
            new ParallelOptions { MaxDegreeOfParallelism = 50, CancellationToken = ct },
            async (ip, token) =>
            {
                if (token.IsCancellationRequested) return;
                var host = await ProbeHostAsync(ip, communities, timeoutMs, token);
                host.SourceSubnet = cidr;   // ← tag for grouping
                await writer.WriteAsync(host, token);
            });
    }

    // ── Host probe (ping → SNMP v2c → v1) ───────────────────────────────────

    private static async Task<DiscoveredHost> ProbeHostAsync(
        string ip, List<string> communities, int timeoutMs, CancellationToken ct)
    {
        var host = new DiscoveredHost { IpAddress = ip };

        // Ping first — fast filter before SNMP
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(ip, timeoutMs / 2);
            if (reply.Status != IPStatus.Success)
                return host;   // unreachable — return as-is
            host.Reachable  = true;
            host.ResponseMs = (int)reply.RoundtripTime;
        }
        catch { return host; }

        // DNS reverse lookup (best-effort, don't block)
        try { host.Hostname = (await Dns.GetHostEntryAsync(ip, ct)).HostName; }
        catch { /* leave blank */ }

        // SNMP probe — v2c first (faster GETBULK), fall back to v1
        var endpoint = new IPEndPoint(IPAddress.Parse(ip), 161);
        foreach (var community in communities)
        {
            foreach (var version in new[] { VersionCode.V2, VersionCode.V1 })
            {
                if (ct.IsCancellationRequested) return host;
                try
                {
                    var vars   = ProbeOids.Select(o => new Variable(new ObjectIdentifier(o))).ToList();
                    var result = await Messenger.GetAsync(version, endpoint,
                        new OctetString(community), vars, CancellationToken.None);

                    if (result.Any())
                    {
                        host.BestVersion   = version == VersionCode.V2 ? SnmpVersion.V2c : SnmpVersion.V1;
                        host.BestCommunity = community;
                        host.SysDescr      = result[0].Data.ToString() ?? string.Empty;
                        host.SysName       = result.Count > 1
                            ? result[1].Data.ToString() ?? string.Empty
                            : string.Empty;
                        host.DeviceType    = InferDeviceType(host.SysDescr, host.SysName);
                        return host;
                    }
                }
                catch { /* try next community / version */ }
            }
        }

        return host;   // reachable but SNMP unresponsive
    }

    // ── Device type inference ─────────────────────────────────────────────────

    private static string InferDeviceType(string sysDescr, string sysName)
    {
        var d = (sysDescr + " " + sysName).ToLowerInvariant();

        if (d.Contains("printer")    || d.Contains("laserjet")   || d.Contains("officejet") ||
            d.Contains("mfp")        || d.Contains("workcentre") || d.Contains("bizhub")    ||
            d.Contains("imagerunner") || d.Contains("phaser")    || d.Contains("versalink") ||
            d.Contains("colorqube")  || d.Contains("ecosys")     || d.Contains("taskalfa"))
            return "🖨 Printer";

        if (d.Contains("switch")     || d.Contains("catalyst")   || d.Contains("procurve")  ||
            d.Contains("nexus")      || d.Contains("arista")     || d.Contains("ex")         ||
            d.Contains("comware"))
            return "🔀 Switch";

        if (d.Contains("router")     || d.Contains("cisco ios")  || d.Contains("junos")     ||
            d.Contains("asr")        || d.Contains("isr"))
            return "🌐 Router";

        if (d.Contains("windows")    || d.Contains("linux")      || d.Contains("ubuntu")    ||
            d.Contains("server")     || d.Contains("esxi")       || d.Contains("vmware"))
            return "🖥 Server";

        if (d.Contains("access point") || d.Contains("aironet") || d.Contains("unifi")      ||
            d.Contains("ap-")          || d.Contains("wap"))
            return "📶 AP";

        if (d.Contains("ups") || d.Contains("apc ") || d.Contains("eaton"))
            return "🔋 UPS";

        return "📦 Device";
    }

    // ── CIDR list parser ──────────────────────────────────────────────────────

    /// <summary>
    /// Parses "Label=CIDR, Label2=CIDR2, CIDR3" into (Label, Cidr) tuples.
    /// Label defaults to the CIDR string when not provided.
    /// </summary>
    public static List<(string Label, string Cidr)> ParseCidrList(string input)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(input)) return result;

        foreach (var token in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim();
            if (string.IsNullOrEmpty(t)) continue;

            var eqIdx = t.IndexOf('=');
            if (eqIdx > 0)
            {
                var label = t[..eqIdx].Trim();
                var cidr  = t[(eqIdx + 1)..].Trim();
                result.Add((label, cidr));
            }
            else
            {
                result.Add((t, t));
            }
        }

        return result;
    }

    // ── CIDR expansion ─────────────────────────────────────────────────────────

    private static IEnumerable<string> ExpandCidr(string cidr)
    {
        cidr = cidr.Trim();
        if (!cidr.Contains('/'))
        {
            yield return cidr;
            yield break;
        }

        var parts   = cidr.Split('/');
        var ip      = IPAddress.Parse(parts[0]);
        var prefix  = int.Parse(parts[1]);
        var mask    = prefix == 0 ? 0 : unchecked((int)0xFFFFFFFF << (32 - prefix));
        var ipInt   = IpToInt(ip);
        var network = ipInt & mask;
        var count   = (int)Math.Pow(2, 32 - prefix);

        // Skip network address (.0) and broadcast (.255)
        for (var i = 1; i < count - 1; i++)
            yield return IntToIp(network + i);
    }

    private static int IpToInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    private static string IntToIp(int n)
    {
        return new IPAddress(new byte[]
        {
            (byte)((n >> 24) & 0xFF),
            (byte)((n >> 16) & 0xFF),
            (byte)((n >> 8)  & 0xFF),
            (byte)(n         & 0xFF),
        }).ToString();
    }
}
