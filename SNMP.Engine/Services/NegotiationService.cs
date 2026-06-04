using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using SNMP.Core.Enums;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Services;

/// <summary>
/// Auto-negotiation probes the target host across SNMP versions and community strings
/// to determine the first working combination, building a NegotiationReport for the user.
///
/// Strategy:
///   1. Try v2c with each candidate community (most modern devices speak v2c).
///   2. Fall back to v1 with each community (legacy / embedded devices).
///   3. Attempt v3 noAuthNoPriv with empty credentials as a last resort.
///   4. Classify failures along the way so the report is informative even on total failure.
/// </summary>
public sealed class NegotiationService : INegotiationService
{
    private static readonly string[] DefaultCommunities = ["public", "private", "snmp", "community", "monitor"];
    private readonly ILogService _log;

    public NegotiationService(ILogService log) => _log = log;

    public async Task<NegotiationReport> NegotiateAsync(
        string host, int port = 161,
        IEnumerable<string>? communitiesToTry = null,
        CancellationToken ct = default)
    {
        var report = new NegotiationReport();
        var communities = (communitiesToTry ?? DefaultCommunities).Distinct().ToList();
        NegotiationReport BuildCanceledReport()
        {
            var canceledReport = new NegotiationReport
            {
                Success = false,
                Result = NegotiationResult.Unknown,
                FriendlyMessage = "Auto-negotiation was canceled.",
                Details = "The operation was canceled before negotiation completed.",
                AttemptLog = [.. report.AttemptLog]
            };
            _log.Info("Negotiation canceled.");
            return canceledReport;
        }

        if (ct.IsCancellationRequested) return BuildCanceledReport();

        IPEndPoint ep;
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            if (addrs.Length == 0) throw new Exception("No addresses returned.");
            ep = new IPEndPoint(addrs[0], port);
        }
        catch (OperationCanceledException) { return BuildCanceledReport(); }
        catch (Exception ex)
        {
            report.Result = NegotiationResult.NoResponse;
            report.FriendlyMessage = $"DNS resolution failed for '{host}'.";
            report.Details = ex.Message;
            return report;
        }

        // Probe sysObjectID (.1.3.6.1.2.1.1.2.0) — lightweight, universally supported
        const string probeOid = "1.3.6.1.2.1.1.2.0";

        // ── Phase 1: v2c ─────────────────────────────────────────────────────
        foreach (var comm in communities)
        {
            if (ct.IsCancellationRequested) return BuildCanceledReport();
            var attempt = $"v2c community='{comm}'";
            report.AttemptLog.Add(attempt);

            try
            {
                var vars = new List<Variable> { new(new ObjectIdentifier(probeOid)) };
                await Task.Run(() =>
                    Messenger.Get(VersionCode.V2, ep, new OctetString(comm), vars, 2000), ct);

                // Success
                report.Success         = true;
                report.WorkingVersion  = SnmpVersion.V2c;
                report.WorkingCommunity = comm;
                report.Result          = NegotiationResult.Success;
                report.FriendlyMessage = $"Connected via SNMPv2c (community: '{comm}').";
                _log.Info($"Negotiation succeeded: {attempt}");
                return report;
            }
            catch (Exception ex)
            {
                var c = ErrorClassifier.Classify(ex);
                report.AttemptLog[^1] += $" → {c.FriendlyMessage}";
                _log.Debug($"Negotiation attempt failed: {attempt} — {c.FriendlyMessage}");

                // If it's an auth failure (wrong community) keep trying.
                // If it's a hard network error (timeout/no response), skip remaining communities.
                if (c.Category == NegotiationResult.NoResponse || c.Category == NegotiationResult.Timeout)
                {
                    report.Result          = c.Category;
                    report.FriendlyMessage = c.FriendlyMessage;
                    report.Details         = c.Details;
                    return report; // No point probing further versions on a dead host
                }
            }
        }

        // ── Phase 2: v1 ──────────────────────────────────────────────────────
        foreach (var comm in communities)
        {
            if (ct.IsCancellationRequested) return BuildCanceledReport();
            var attempt = $"v1 community='{comm}'";
            report.AttemptLog.Add(attempt);

            try
            {
                var vars = new List<Variable> { new(new ObjectIdentifier(probeOid)) };
                await Task.Run(() =>
                    Messenger.Get(VersionCode.V1, ep, new OctetString(comm), vars, 2000), ct);

                report.Success          = true;
                report.WorkingVersion   = SnmpVersion.V1;
                report.WorkingCommunity = comm;
                report.Result           = NegotiationResult.Success;
                report.FriendlyMessage  = $"Connected via SNMPv1 (community: '{comm}').";
                _log.Info($"Negotiation succeeded: {attempt}");
                return report;
            }
            catch (Exception ex)
            {
                var c = ErrorClassifier.Classify(ex);
                report.AttemptLog[^1] += $" → {c.FriendlyMessage}";
            }
        }

        // ── Phase 3: v3 noAuthNoPriv ──────────────────────────────────────────
        {
            if (ct.IsCancellationRequested) return BuildCanceledReport();
            var attempt = "v3 noAuthNoPriv";
            report.AttemptLog.Add(attempt);
            try
            {
                var discovery = Messenger.GetNextDiscovery(SnmpType.GetBulkRequestPdu);
                var probe     = discovery.GetResponse(2000, ep);
                // If we get a report PDU back, the device speaks v3
                report.Success          = true;
                report.WorkingVersion   = SnmpVersion.V3;
                report.WorkingCommunity = null;
                report.Result           = NegotiationResult.Success;
                report.FriendlyMessage  = "Device responds to SNMPv3 — configure credentials to proceed.";
                _log.Info($"Negotiation succeeded: {attempt}");
                return report;
            }
            catch (Exception ex)
            {
                var c = ErrorClassifier.Classify(ex);
                report.AttemptLog[^1] += $" → {c.FriendlyMessage}";
            }
        }

        // All phases exhausted
        report.Success         = false;
        report.Result          = NegotiationResult.Unknown;
        report.FriendlyMessage = "Auto-negotiation failed across all SNMP versions and communities. Check device SNMP configuration.";
        _log.Warn("Negotiation exhausted all options without success.");
        return report;
    }
}
