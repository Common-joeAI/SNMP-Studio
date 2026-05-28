using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Mib;

/// <summary>
/// Translates numeric OIDs to symbolic names and human-readable values.
///
/// Resolution priority:
///   1. Exact match in loaded MIB repository
///   2. Longest-prefix match (handles instance qualifiers like .0)
///   3. Built-in table of ~100 standard RFC-1213 / RFC-1907 / printer MIB OIDs
///   4. Raw numeric OID displayed as-is
/// </summary>
public sealed class OidTranslator : IOidTranslator
{
    private readonly IMibRepository _mib;

    public OidTranslator(IMibRepository mib) => _mib = mib;

    // ── Standard OID fallback table ──────────────────────────────────────────
    // Covers MIB-II (RFC-1213), SNMPv2-MIB, IF-MIB, HOST-RESOURCES-MIB,
    // and Printer-MIB (RFC-3805). Loaded even with no MIB files present.
    private static readonly Dictionary<string, (string Name, string Desc)> StandardOids = new()
    {
        // System
        ["1.3.6.1.2.1.1.1"]    = ("sysDescr",       "A textual description of the entity."),
        ["1.3.6.1.2.1.1.1.0"]  = ("sysDescr.0",     "A textual description of the entity."),
        ["1.3.6.1.2.1.1.2"]    = ("sysObjectID",     "The vendor's authoritative identification."),
        ["1.3.6.1.2.1.1.2.0"]  = ("sysObjectID.0",  "The vendor's authoritative identification."),
        ["1.3.6.1.2.1.1.3"]    = ("sysUpTime",      "Time since network management last re-initialized."),
        ["1.3.6.1.2.1.1.3.0"]  = ("sysUpTime.0",    "Time since network management last re-initialized."),
        ["1.3.6.1.2.1.1.4"]    = ("sysContact",     "Contact person for this node."),
        ["1.3.6.1.2.1.1.4.0"]  = ("sysContact.0",   "Contact person for this node."),
        ["1.3.6.1.2.1.1.5"]    = ("sysName",        "Administratively-assigned name."),
        ["1.3.6.1.2.1.1.5.0"]  = ("sysName.0",      "Administratively-assigned name."),
        ["1.3.6.1.2.1.1.6"]    = ("sysLocation",    "Physical location of this node."),
        ["1.3.6.1.2.1.1.6.0"]  = ("sysLocation.0",  "Physical location of this node."),
        ["1.3.6.1.2.1.1.7"]    = ("sysServices",    "Value indicating the services this entity offers."),
        ["1.3.6.1.2.1.1.7.0"]  = ("sysServices.0",  "Value indicating the services this entity offers."),

        // Interfaces
        ["1.3.6.1.2.1.2.1.0"]  = ("ifNumber.0",     "Number of network interfaces."),
        ["1.3.6.1.2.1.2.2.1.1"] = ("ifIndex",       "Interface index."),
        ["1.3.6.1.2.1.2.2.1.2"] = ("ifDescr",       "Textual string describing the interface."),
        ["1.3.6.1.2.1.2.2.1.5"] = ("ifSpeed",       "Interface bandwidth in bits per second."),
        ["1.3.6.1.2.1.2.2.1.7"] = ("ifAdminStatus", "Desired state: up(1) down(2) testing(3)."),
        ["1.3.6.1.2.1.2.2.1.8"] = ("ifOperStatus",  "Current operational state."),
        ["1.3.6.1.2.1.2.2.1.10"] = ("ifInOctets",   "Octets received on interface."),
        ["1.3.6.1.2.1.2.2.1.16"] = ("ifOutOctets",  "Octets transmitted on interface."),

        // Printer MIB (RFC-3805) — supplies
        ["1.3.6.1.2.1.43.11.1.1.8"]  = ("prtMarkerSuppliesLevel",    "Current level of this supply."),
        ["1.3.6.1.2.1.43.11.1.1.9"]  = ("prtMarkerSuppliesMaxCapacity", "Max capacity of this supply."),
        ["1.3.6.1.2.1.43.11.1.1.6"]  = ("prtMarkerSuppliesType",     "Type of supply (toner, ink, etc.)."),
        ["1.3.6.1.2.1.43.11.1.1.7"]  = ("prtMarkerSuppliesColorant", "Colorant name (black, cyan, etc.)."),

        // Printer — status
        ["1.3.6.1.2.1.25.3.2.1.5"]   = ("hrDeviceStatus",  "Device status: unknown(1) running(2) warning(3) testing(4) down(5)."),
        ["1.3.6.1.2.1.43.18.1.1.8"]  = ("prtAlertDescription", "Textual description of printer alert."),
        ["1.3.6.1.2.1.43.5.1.1.15"]  = ("prtGeneralPrinterName", "Printer name."),
        ["1.3.6.1.2.1.43.5.1.1.16"]  = ("prtGeneralSerialNumber",  "Printer serial number."),
        ["1.3.6.1.2.1.43.8.2.1.13"]  = ("prtInputMediaDimFeedDirDeclared", "Paper tray media size."),
        ["1.3.6.1.2.1.43.7.3.1.3"]   = ("prtOutputName", "Output bin name."),

        // Host resources — storage
        ["1.3.6.1.2.1.25.2.3.1.3"]   = ("hrStorageDescr",       "Description of storage area."),
        ["1.3.6.1.2.1.25.2.3.1.5"]   = ("hrStorageSize",        "Size of storage in allocation units."),
        ["1.3.6.1.2.1.25.2.3.1.6"]   = ("hrStorageUsed",        "Amount of storage in use."),
        ["1.3.6.1.2.1.25.2.3.1.4"]   = ("hrStorageAllocationUnits", "Size of each allocation unit in bytes."),

        // SNMPv2-MIB
        ["1.3.6.1.6.3.1.1.4.1.0"]    = ("snmpTrapOID.0",      "OID of the trap being sent."),
        ["1.3.6.1.6.3.10.2.1.3.0"]   = ("snmpEngineBoots.0",  "Number of times the SNMP engine has re-booted."),
        ["1.3.6.1.6.3.10.2.1.4.0"]   = ("snmpEngineTime.0",   "Time in seconds since last engine boot."),
    };

    // ── Uptime / status interpretation maps ──────────────────────────────────
    private static readonly Dictionary<string, Dictionary<int, string>> StatusEnums = new()
    {
        ["ifAdminStatus"] = new() { [1] = "up", [2] = "down", [3] = "testing" },
        ["ifOperStatus"]  = new() { [1] = "up", [2] = "down", [3] = "testing",
                                    [4] = "unknown", [5] = "dormant", [6] = "notPresent", [7] = "lowerLayerDown" },
        ["hrDeviceStatus"] = new() { [1] = "unknown", [2] = "running", [3] = "warning",
                                     [4] = "testing", [5] = "down" },
    };

    // ── Translation ──────────────────────────────────────────────────────────

    public (string SymbolicName, string Description) Translate(string numericOid)
    {
        var normalized = Normalize(numericOid);

        // 1. Exact MIB match
        var node = _mib.Lookup(normalized);
        if (node != null) return (FormatSymbolic(node, normalized), node.Description);

        // 2. Longest prefix MIB match
        node = _mib.LongestPrefixMatch(normalized);
        if (node != null)
        {
            var instance = normalized[node.NumericOid.Length..].TrimStart('.');
            return ($"{node.Name}.{instance}", node.Description);
        }

        // 3. Standard OID table
        if (StandardOids.TryGetValue(normalized, out var std))
            return (std.Name, std.Desc);

        // 4. Longest prefix in standard table
        var bestStd = StandardOids.Keys
            .Where(k => normalized.StartsWith(k))
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();
        if (bestStd != null)
        {
            var (name, desc) = StandardOids[bestStd];
            var instance = normalized[bestStd.Length..].TrimStart('.');
            return ($"{name}.{instance}", desc);
        }

        // 4. Raw OID
        return (numericOid, string.Empty);
    }

    public string InterpretValue(string numericOid, string rawValue, string snmpType)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return rawValue;

        var normalized = Normalize(numericOid);
        var (symName, _) = Translate(numericOid);
        var baseName = symName.Split('.')[0];

        // ── Uptime: TimeTicks are 1/100th of a second ────────────────────────
        if (snmpType.Contains("TimeTick", StringComparison.OrdinalIgnoreCase)
            || baseName.Contains("UpTime", StringComparison.OrdinalIgnoreCase)
            || baseName.Contains("upTime", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(rawValue, out var ticks))
                return FormatUptime(ticks);
        }

        // ── Interface speed: bps → Mbps ──────────────────────────────────────
        if (baseName == "ifSpeed" && long.TryParse(rawValue, out var bps))
            return bps >= 1_000_000 ? $"{bps / 1_000_000} Mbps" : $"{bps} bps";

        // ── Status enums from MIB node ────────────────────────────────────────
        var node = _mib.LongestPrefixMatch(normalized) ?? _mib.Lookup(normalized);
        if (node?.Enumerations.Count > 0 && int.TryParse(rawValue, out var intVal))
            if (node.Enumerations.TryGetValue(intVal, out var enumLabel))
                return $"{enumLabel} ({intVal})";

        // ── Status enums from built-in table ─────────────────────────────────
        if (StatusEnums.TryGetValue(baseName, out var enumMap)
            && int.TryParse(rawValue, out var ev)
            && enumMap.TryGetValue(ev, out var label))
            return $"{label} ({ev})";

        // ── Printer supply percentage ─────────────────────────────────────────
        // prtMarkerSuppliesLevel + prtMarkerSuppliesMaxCapacity — caller computes %
        // We interpret here if the value looks like a percentage marker (-1 = unknown)
        if (baseName == "prtMarkerSuppliesLevel")
        {
            if (rawValue == "-1") return "Unknown / unrestricted";
            if (rawValue == "-2") return "Exhausted";
            if (int.TryParse(rawValue, out var lvl) && lvl >= 0) return $"{lvl} units remaining";
        }

        // ── Octet strings that look like hex — try ASCII ──────────────────────
        if (snmpType.Contains("OctetString", StringComparison.OrdinalIgnoreCase)
            && rawValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var hex   = rawValue[2..].Replace(" ", "");
                var bytes = Convert.FromHexString(hex);
                var ascii = System.Text.Encoding.ASCII.GetString(bytes).Trim();
                if (ascii.All(c => c >= 32 && c < 127)) return ascii;
            }
            catch { /* leave raw */ }
        }

        return rawValue;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string FormatSymbolic(MibNode node, string normalized)
    {
        // If the queried OID has an instance suffix beyond the base OID, append it
        if (normalized.Length > node.NumericOid.Length)
        {
            var instance = normalized[node.NumericOid.Length..].TrimStart('.');
            return $"{node.Name}.{instance}";
        }
        return node.Name;
    }

    private static string FormatUptime(long ticks)
    {
        var ts = TimeSpan.FromSeconds(ticks / 100.0);
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours:D2}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
        return $"{ts.Hours:D2}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
    }

    private static string Normalize(string oid) => oid.StartsWith('.') ? oid[1..] : oid;
}
