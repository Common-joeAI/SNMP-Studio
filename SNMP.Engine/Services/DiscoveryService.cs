using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using SNMP.Core.Enums;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Services;

/// <summary>
/// Scans a CIDR subnet (e.g. 192.168.1.0/24), pings each host, then probes SNMP.
/// Yields results as they arrive — no need to wait for the full scan.
/// </summary>
public sealed class DiscoveryService : IDiscoveryService
{
    private static readonly string[] ProbeOids =
    {
        "1.3.6.1.2.1.1.1.0",  // sysDescr
        "1.3.6.1.2.1.1.5.0",  // sysName
    };

    public async IAsyncEnumerable<DiscoveredHost> DiscoverAsync(
        string cidr,
        IEnumerable<string> communities,
        int timeoutMs = 1500,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var ips = ExpandCidr(cidr).ToList();
        var communityList = communities.ToList();
        if (communityList.Count == 0) communityList.Add("public");

        // Channel for thread-safe streaming of results
        var channel = System.Threading.Channels.Channel.CreateUnbounded<DiscoveredHost>();

        var scanTask = Task.Run(async () =>
        {
            await Parallel.ForEachAsync(ips,
                new ParallelOptions { MaxDegreeOfParallelism = 50, CancellationToken = ct },
                async (ip, token) =>
                {
                    if (token.IsCancellationRequested) return;
                    var host = await ProbeHostAsync(ip, communityList, timeoutMs, token);
                    await channel.Writer.WriteAsync(host, token);
                });
            channel.Writer.Complete();
        }, ct);

        await foreach (var host in channel.Reader.ReadAllAsync(ct))
            yield return host;

        await scanTask;
    }

    private static async Task<DiscoveredHost> ProbeHostAsync(
        string ip, List<string> communities, int timeoutMs, CancellationToken ct)
    {
        var host = new DiscoveredHost { IpAddress = ip };

        // Ping first
        using var ping = new Ping();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var reply = await ping.SendPingAsync(ip, timeoutMs / 2);
            if (reply.Status != IPStatus.Success)
                return host; // not reachable
            host.Reachable   = true;
            host.ResponseMs  = (int)reply.RoundtripTime;
        }
        catch { return host; }

        // Try hostname resolution
        try { host.Hostname = (await Dns.GetHostEntryAsync(ip, ct)).HostName; }
        catch { /* leave blank */ }

        // Probe SNMP v2c → v1
        var endpoint = new IPEndPoint(IPAddress.Parse(ip), 161);
        foreach (var community in communities)
        {
            foreach (var version in new[] { VersionCode.V2, VersionCode.V1 })
            {
                try
                {
                    var oids = ProbeOids.Select(o => new Variable(new ObjectIdentifier(o))).ToList();
                    var result = await Messenger.GetAsync(version, endpoint,
                        new OctetString(community), oids,
                        CancellationToken.None); // short inner timeout handled by UDP

                    if (result.Any())
                    {
                        host.BestVersion   = version == VersionCode.V2 ? SnmpVersion.V2c : SnmpVersion.V1;
                        host.BestCommunity = community;
                        host.SysDescr      = result[0].Data.ToString() ?? string.Empty;
                        host.SysName       = result.Count > 1 ? result[1].Data.ToString() ?? string.Empty : string.Empty;
                        host.DeviceType    = InferDeviceType(host.SysDescr, host.SysName);
                        return host;
                    }
                }
                catch { /* try next */ }
            }
        }

        return host; // reachable but no SNMP
    }

    private static string InferDeviceType(string sysDescr, string sysName)
    {
        var d = (sysDescr + " " + sysName).ToLowerInvariant();
        if (d.Contains("printer") || d.Contains("laserjet") || d.Contains("officejet") ||
            d.Contains("mfp") || d.Contains("workcentre") || d.Contains("bizhub"))
            return "🖨 Printer";
        if (d.Contains("switch") || d.Contains("catalyst") || d.Contains("procurve"))
            return "🔀 Switch";
        if (d.Contains("router") || d.Contains("cisco ios") || d.Contains("junos"))
            return "🌐 Router";
        if (d.Contains("windows") || d.Contains("linux") || d.Contains("ubuntu") || d.Contains("server"))
            return "🖥 Server";
        if (d.Contains("access point") || d.Contains("aironet") || d.Contains("unifi"))
            return "📶 AP";
        return "📦 Device";
    }

    // ── CIDR expansion ────────────────────────────────────────────────────────

    private static IEnumerable<string> ExpandCidr(string cidr)
    {
        if (!cidr.Contains('/'))
        {
            yield return cidr;
            yield break;
        }

        var parts  = cidr.Split('/');
        var ip     = IPAddress.Parse(parts[0]);
        var prefix = int.Parse(parts[1]);
        var mask   = prefix == 0 ? 0 : unchecked((int)0xFFFFFFFF << (32 - prefix));
        var ipInt  = IpToInt(ip);
        var network = ipInt & mask;
        var count  = (int)Math.Pow(2, 32 - prefix);

        for (var i = 1; i < count - 1; i++)  // skip network + broadcast
            yield return IntToIp(network + i);
    }

    private static int IpToInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    private static string IntToIp(int n)
    {
        var b = new byte[4];
        b[0] = (byte)((n >> 24) & 0xFF);
        b[1] = (byte)((n >> 16) & 0xFF);
        b[2] = (byte)((n >> 8)  & 0xFF);
        b[3] = (byte)(n         & 0xFF);
        return new IPAddress(b).ToString();
    }
}
