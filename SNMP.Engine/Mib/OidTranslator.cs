using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Mib;

/// <summary>
/// Translates numeric OIDs to symbolic names and human-readable values.
///
/// Resolution priority:
///   1. Exact match in loaded MIB repository (imported .mib files)
///   2. Longest-prefix match (handles instance qualifiers like .0)
///   3. Built-in OID database (this file) covering:
///        • MIB-II / RFC-1213, SNMPv2-MIB, IF-MIB, HOST-RESOURCES-MIB
///        • Printer-MIB RFC-3805 (standard)
///        • HP / FutureSmart Enterprise MIB  (1.3.6.1.4.1.11.2.3.9.*)
///        • HP JetDirect / Networking MIB    (1.3.6.1.4.1.11.2.4.3.*)
///        • Cisco Enterprise MIB roots       (1.3.6.1.4.1.9.*)
///        • Cisco IOS / NX-OS system OIDs
///        • Cisco Interface / VLAN / Spanning-Tree / CDP / VTP
///        • Cisco QoS / OSPF / BGP / HSRP / EIGRP / AAA
///   4. Raw numeric OID as-is
/// </summary>
public sealed class OidTranslator : IOidTranslator
{
    private readonly IMibRepository _mib;

    public OidTranslator(IMibRepository mib) => _mib = mib;

    // ══════════════════════════════════════════════════════════════════════════
    // STANDARD OID TABLE  — RFC MIBs (always available, no import required)
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, (string Name, string Desc)> StandardOids = new()
    {
        // ── MIB-II System (RFC-1213 / SNMPv2-MIB) ────────────────────────────
        ["1.3.6.1.2.1.1.1"]     = ("sysDescr",          "A textual description of the entity."),
        ["1.3.6.1.2.1.1.1.0"]   = ("sysDescr.0",        "A textual description of the entity."),
        ["1.3.6.1.2.1.1.2"]     = ("sysObjectID",        "Vendor authoritative identification OID."),
        ["1.3.6.1.2.1.1.2.0"]   = ("sysObjectID.0",     "Vendor authoritative identification OID."),
        ["1.3.6.1.2.1.1.3"]     = ("sysUpTime",          "Centiseconds since last re-initialization."),
        ["1.3.6.1.2.1.1.3.0"]   = ("sysUpTime.0",       "Centiseconds since last re-initialization."),
        ["1.3.6.1.2.1.1.4"]     = ("sysContact",         "Contact person for this node."),
        ["1.3.6.1.2.1.1.4.0"]   = ("sysContact.0",      "Contact person for this node."),
        ["1.3.6.1.2.1.1.5"]     = ("sysName",            "Administratively assigned name."),
        ["1.3.6.1.2.1.1.5.0"]   = ("sysName.0",         "Administratively assigned name."),
        ["1.3.6.1.2.1.1.6"]     = ("sysLocation",        "Physical location of this node."),
        ["1.3.6.1.2.1.1.6.0"]   = ("sysLocation.0",     "Physical location of this node."),
        ["1.3.6.1.2.1.1.7"]     = ("sysServices",        "Services offered bitmask."),
        ["1.3.6.1.2.1.1.7.0"]   = ("sysServices.0",     "Services offered bitmask."),
        ["1.3.6.1.2.1.1.8"]     = ("sysLastChange",     "Value of sysUpTime at last config change."),
        ["1.3.6.1.2.1.1.8.0"]   = ("sysLastChange.0",  "Value of sysUpTime at last config change."),

        // ── IF-MIB (RFC-2863) — Interfaces ───────────────────────────────────
        ["1.3.6.1.2.1.2.1.0"]    = ("ifNumber.0",         "Number of network interfaces."),
        ["1.3.6.1.2.1.2.2.1.1"]  = ("ifIndex",            "Interface index."),
        ["1.3.6.1.2.1.2.2.1.2"]  = ("ifDescr",            "Textual description of the interface."),
        ["1.3.6.1.2.1.2.2.1.3"]  = ("ifType",             "Interface type (ethernetCsmacd=6, etc.)."),
        ["1.3.6.1.2.1.2.2.1.4"]  = ("ifMtu",              "Interface MTU in bytes."),
        ["1.3.6.1.2.1.2.2.1.5"]  = ("ifSpeed",            "Interface bandwidth in bits per second."),
        ["1.3.6.1.2.1.2.2.1.6"]  = ("ifPhysAddress",      "Interface physical (MAC) address."),
        ["1.3.6.1.2.1.2.2.1.7"]  = ("ifAdminStatus",      "Desired state: up(1) down(2) testing(3)."),
        ["1.3.6.1.2.1.2.2.1.8"]  = ("ifOperStatus",       "Current operational state."),
        ["1.3.6.1.2.1.2.2.1.9"]  = ("ifLastChange",       "sysUpTime when interface last changed."),
        ["1.3.6.1.2.1.2.2.1.10"] = ("ifInOctets",         "Total octets received on interface."),
        ["1.3.6.1.2.1.2.2.1.11"] = ("ifInUcastPkts",      "Unicast packets received."),
        ["1.3.6.1.2.1.2.2.1.13"] = ("ifInDiscards",       "Inbound packets discarded (no errors)."),
        ["1.3.6.1.2.1.2.2.1.14"] = ("ifInErrors",         "Inbound packets with errors."),
        ["1.3.6.1.2.1.2.2.1.16"] = ("ifOutOctets",        "Total octets transmitted on interface."),
        ["1.3.6.1.2.1.2.2.1.17"] = ("ifOutUcastPkts",     "Unicast packets transmitted."),
        ["1.3.6.1.2.1.2.2.1.19"] = ("ifOutDiscards",      "Outbound packets discarded."),
        ["1.3.6.1.2.1.2.2.1.20"] = ("ifOutErrors",        "Outbound packets with errors."),

        // IF-MIB High-Capacity counters (ifXTable)
        ["1.3.6.1.2.1.31.1.1.1.1"]  = ("ifName",          "Interface textual name."),
        ["1.3.6.1.2.1.31.1.1.1.6"]  = ("ifHCInOctets",    "64-bit octets received."),
        ["1.3.6.1.2.1.31.1.1.1.10"] = ("ifHCOutOctets",   "64-bit octets transmitted."),
        ["1.3.6.1.2.1.31.1.1.1.15"] = ("ifHighSpeed",     "Interface speed in Mbps."),
        ["1.3.6.1.2.1.31.1.1.1.18"] = ("ifAlias",         "Interface alias / description."),

        // ── IP-MIB (RFC-4293) ─────────────────────────────────────────────────
        ["1.3.6.1.2.1.4.1.0"]   = ("ipForwarding.0",     "Whether IP forwarding is enabled: forwarding(1) notForwarding(2)."),
        ["1.3.6.1.2.1.4.3.0"]   = ("ipInReceives.0",     "Total IP datagrams received."),
        ["1.3.6.1.2.1.4.5.0"]   = ("ipInHdrErrors.0",    "IP datagrams discarded due to header errors."),
        ["1.3.6.1.2.1.4.10.0"]  = ("ipInDelivers.0",     "IP datagrams successfully delivered."),
        ["1.3.6.1.2.1.4.11.0"]  = ("ipOutRequests.0",    "IP datagrams originating locally."),
        ["1.3.6.1.2.1.4.14.0"]  = ("ipOutDiscards.0",    "Outbound IP datagrams discarded."),
        ["1.3.6.1.2.1.4.17.0"]  = ("ipReasmReqds.0",     "IP fragments requiring reassembly."),

        // ── UDP-MIB / TCP-MIB ─────────────────────────────────────────────────
        ["1.3.6.1.2.1.7.1.0"]   = ("udpInDatagrams.0",   "UDP datagrams delivered."),
        ["1.3.6.1.2.1.7.2.0"]   = ("udpNoPorts.0",       "UDP datagrams to unknown port."),
        ["1.3.6.1.2.1.7.4.0"]   = ("udpOutDatagrams.0",  "UDP datagrams sent."),
        ["1.3.6.1.2.1.6.10.0"]  = ("tcpInSegs.0",        "TCP segments received."),
        ["1.3.6.1.2.1.6.11.0"]  = ("tcpOutSegs.0",       "TCP segments sent."),
        ["1.3.6.1.2.1.6.12.0"]  = ("tcpRetransSegs.0",   "TCP segments retransmitted."),
        ["1.3.6.1.2.1.6.13.1.1"] = ("tcpConnState",      "TCP connection state."),

        // ── HOST-RESOURCES-MIB (RFC-2790) ────────────────────────────────────
        ["1.3.6.1.2.1.25.1.1.0"]  = ("hrSystemUptime.0",  "Host system uptime in centiseconds."),
        ["1.3.6.1.2.1.25.1.6.0"]  = ("hrSystemNumUsers.0","Number of users currently logged on."),
        ["1.3.6.1.2.1.25.1.7.0"]  = ("hrSystemProcesses.0","Number of process contexts."),
        ["1.3.6.1.2.1.25.2.1"]    = ("hrStorageTypes",    "Storage type definitions."),
        ["1.3.6.1.2.1.25.2.3.1.1"] = ("hrStorageIndex",   "Storage area index."),
        ["1.3.6.1.2.1.25.2.3.1.2"] = ("hrStorageType",    "Storage type (RAM, disk, etc.)."),
        ["1.3.6.1.2.1.25.2.3.1.3"] = ("hrStorageDescr",   "Description of storage area."),
        ["1.3.6.1.2.1.25.2.3.1.4"] = ("hrStorageAllocationUnits", "Allocation unit size in bytes."),
        ["1.3.6.1.2.1.25.2.3.1.5"] = ("hrStorageSize",    "Size in allocation units."),
        ["1.3.6.1.2.1.25.2.3.1.6"] = ("hrStorageUsed",    "Storage in use, allocation units."),
        ["1.3.6.1.2.1.25.3.2.1.1"] = ("hrDeviceIndex",    "Device index."),
        ["1.3.6.1.2.1.25.3.2.1.2"] = ("hrDeviceType",     "Device type OID."),
        ["1.3.6.1.2.1.25.3.2.1.3"] = ("hrDeviceDescr",    "Device description."),
        ["1.3.6.1.2.1.25.3.2.1.5"] = ("hrDeviceStatus",   "Device status: unknown(1) running(2) warning(3) testing(4) down(5)."),
        ["1.3.6.1.2.1.25.3.2.1.6"] = ("hrDeviceErrors",   "Number of device errors."),
        ["1.3.6.1.2.1.25.3.5.1.1"] = ("hrPrinterStatus",  "Printer status: idle(1) running(2) warmup(3) searching(4) stopped(5)."),
        ["1.3.6.1.2.1.25.3.5.1.2"] = ("hrPrinterDetectedErrorState", "Bitmask of printer error states."),

        // ── Printer-MIB (RFC-3805) — General ─────────────────────────────────
        ["1.3.6.1.2.1.43.5.1.1.1"]  = ("prtGeneralConfigChanges",    "Number of configuration changes."),
        ["1.3.6.1.2.1.43.5.1.1.2"]  = ("prtGeneralCurrentLocalization", "Localization index."),
        ["1.3.6.1.2.1.43.5.1.1.3"]  = ("prtGeneralReset",            "Printer reset control."),
        ["1.3.6.1.2.1.43.5.1.1.5"]  = ("prtGeneralPrinterName",      "Printer's administrative name."),
        ["1.3.6.1.2.1.43.5.1.1.6"]  = ("prtGeneralSerialNumber",     "Printer serial number."),
        ["1.3.6.1.2.1.43.5.1.1.7"]  = ("prtAlertCriticalEvents",     "Critical alert count."),
        ["1.3.6.1.2.1.43.5.1.1.8"]  = ("prtAlertAllEvents",          "Total alert count."),
        ["1.3.6.1.2.1.43.5.1.1.15"] = ("prtGeneralPrinterName",      "Printer name."),
        ["1.3.6.1.2.1.43.5.1.1.16"] = ("prtGeneralSerialNumber",     "Printer serial number."),

        // Printer-MIB — Input trays (prtInputTable)
        ["1.3.6.1.2.1.43.8.2.1.2"]  = ("prtInputType",               "Input type: other(1) unknown(2) sheetFeedAutoRemovableTray(3) sheetFeedAutoNonRemovableTray(4) sheetFeedManual(5) continuousRoll(6) continuousFanFold(7)."),
        ["1.3.6.1.2.1.43.8.2.1.3"]  = ("prtInputDimUnit",            "Unit of measure for input dimensions."),
        ["1.3.6.1.2.1.43.8.2.1.4"]  = ("prtInputMediaDimFeedDirDecl", "Media feed-direction declared size."),
        ["1.3.6.1.2.1.43.8.2.1.5"]  = ("prtInputMediaDimXFeedDirDecl","Media cross-feed declared size."),
        ["1.3.6.1.2.1.43.8.2.1.9"]  = ("prtInputMediaName",          "Media name (Letter, A4, etc.)."),
        ["1.3.6.1.2.1.43.8.2.1.11"] = ("prtInputCapacityUnit",       "Capacity unit (sheets)."),
        ["1.3.6.1.2.1.43.8.2.1.12"] = ("prtInputMaxCapacity",        "Max sheets this tray holds."),
        ["1.3.6.1.2.1.43.8.2.1.13"] = ("prtInputCurrentLevel",       "Current sheet count in tray."),
        ["1.3.6.1.2.1.43.8.2.1.18"] = ("prtInputDescription",        "Tray description."),

        // Printer-MIB — Output bins (prtOutputTable)
        ["1.3.6.1.2.1.43.9.2.1.2"]  = ("prtOutputType",              "Output bin type."),
        ["1.3.6.1.2.1.43.9.2.1.11"] = ("prtOutputCapacityUnit",      "Capacity unit (sheets)."),
        ["1.3.6.1.2.1.43.9.2.1.12"] = ("prtOutputMaxCapacity",       "Max sheets this bin holds."),
        ["1.3.6.1.2.1.43.9.2.1.13"] = ("prtOutputRemainingCapacity", "Remaining capacity in sheets."),
        ["1.3.6.1.2.1.43.9.2.1.14"] = ("prtOutputStatus",            "Output status."),
        ["1.3.6.1.2.1.43.9.2.1.18"] = ("prtOutputDescription",       "Output bin description."),

        // Printer-MIB — Markers
        ["1.3.6.1.2.1.43.10.2.1.4"] = ("prtMarkerLifeCount",         "Total pages printed lifetime."),
        ["1.3.6.1.2.1.43.10.2.1.5"] = ("prtMarkerPowerOnCount",      "Pages printed since last power on."),
        ["1.3.6.1.2.1.43.10.2.1.7"] = ("prtMarkerProcessColorants",  "Number of process colorants."),
        ["1.3.6.1.2.1.43.10.2.1.8"] = ("prtMarkerSpotColorants",     "Number of spot colorants."),

        // Printer-MIB — Supplies (prtMarkerSuppliesTable)
        ["1.3.6.1.2.1.43.11.1.1.1"] = ("prtMarkerSuppliesIndex",     "Supplies table index."),
        ["1.3.6.1.2.1.43.11.1.1.3"] = ("prtMarkerSuppliesClass",     "Supply class: other(1) supplyThatIsConsumed(3) receptacleThatIsFilled(4)."),
        ["1.3.6.1.2.1.43.11.1.1.4"] = ("prtMarkerSuppliesType",      "Supply type (e.g. toner cartridge)."),
        ["1.3.6.1.2.1.43.11.1.1.5"] = ("prtMarkerSuppliesDescription","Textual description of supply."),
        ["1.3.6.1.2.1.43.11.1.1.6"] = ("prtMarkerSuppliesSupplyUnit","Unit used for supply level."),
        ["1.3.6.1.2.1.43.11.1.1.7"] = ("prtMarkerSuppliesMaxCapacity","Max supply capacity."),
        ["1.3.6.1.2.1.43.11.1.1.8"] = ("prtMarkerSuppliesLevel",     "Current supply level."),

        // Printer-MIB — Colorant table
        ["1.3.6.1.2.1.43.12.1.1.3"] = ("prtMarkerColorantRole",      "Colorant role."),
        ["1.3.6.1.2.1.43.12.1.1.4"] = ("prtMarkerColorantValue",     "Colorant name: black, cyan, magenta, yellow, etc."),
        ["1.3.6.1.2.1.43.12.1.1.5"] = ("prtMarkerColorantTonality",  "Levels of tonality supported."),

        // Printer-MIB — Alerts (prtAlertTable)
        ["1.3.6.1.2.1.43.18.1.1.1"] = ("prtAlertIndex",              "Alert table index."),
        ["1.3.6.1.2.1.43.18.1.1.2"] = ("prtAlertSeverityLevel",      "Severity: other(1) critical(3) warning(4) warningBinaryChangeEvent(5)."),
        ["1.3.6.1.2.1.43.18.1.1.5"] = ("prtAlertGroup",              "Alert group (input/output/marker/etc.)."),
        ["1.3.6.1.2.1.43.18.1.1.8"] = ("prtAlertCode",               "Alert code (e.g. doorOpen, inputMediaTrayMissing)."),
        ["1.3.6.1.2.1.43.18.1.1.9"] = ("prtAlertDescription",        "Textual description of printer alert."),

        // ── SNMPv2-MIB / SNMP Framework ──────────────────────────────────────
        ["1.3.6.1.6.3.1.1.4.1.0"]   = ("snmpTrapOID.0",       "OID of the trap being sent."),
        ["1.3.6.1.6.3.10.2.1.1.0"]  = ("snmpEngineID.0",      "Unique SNMP engine ID."),
        ["1.3.6.1.6.3.10.2.1.2.0"]  = ("snmpEngineBoots.0",   "Times the engine has re-booted."),
        ["1.3.6.1.6.3.10.2.1.3.0"]  = ("snmpEngineTime.0",    "Seconds since last engine boot."),
        ["1.3.6.1.6.3.10.2.1.4.0"]  = ("snmpEngineMaxMsgSize.0", "Max message size this engine supports."),
    };

    // ══════════════════════════════════════════════════════════════════════════
    // HP / FUTURESMART ENTERPRISE OID TABLE
    // Enterprise OID root: 1.3.6.1.4.1.11  (Hewlett-Packard)
    //
    // HP PRINTER MIB subtree layout:
    //   1.3.6.1.4.1.11.2.3.9.1   → hp-npSNMP-system
    //   1.3.6.1.4.1.11.2.3.9.4   → hp-npSNMP-device
    //   1.3.6.1.4.1.11.2.3.9.4.2 → hp-device-info (FutureSmart core)
    //
    // FutureSmart = HP's embedded firmware platform used across
    // LaserJet Pro, Enterprise, MFP series since ~2012.
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, (string Name, string Desc)> HpOids = new()
    {
        // ── HP Enterprise root ────────────────────────────────────────────────
        ["1.3.6.1.4.1.11"]           = ("hp",               "Hewlett-Packard enterprise OID root."),
        ["1.3.6.1.4.1.11.2"]         = ("hp-nm",            "HP Network Management subtree."),
        ["1.3.6.1.4.1.11.2.3.9"]     = ("hp-npSNMP",       "HP Network Peripheral SNMP."),
        ["1.3.6.1.4.1.11.2.3.9.1"]   = ("hp-npSNMP-system","HP NP SNMP system info."),
        ["1.3.6.1.4.1.11.2.3.9.4"]   = ("hp-npSNMP-device","HP NP SNMP device info."),
        ["1.3.6.1.4.1.11.2.3.9.4.2"] = ("hp-device-info",  "HP device information subtree (FutureSmart core)."),

        // ── FutureSmart — Identification ──────────────────────────────────────
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.1.0"] = ("hp-fsDeviceName",         "HP FutureSmart device name."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.2.0"] = ("hp-fsDeviceDescription",  "HP device description string."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.3.0"] = ("hp-fsDeviceAssetNumber",  "Asset/inventory number."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.4.0"] = ("hp-fsDeviceLocation",     "Device physical location."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.5.0"] = ("hp-fsDeviceContact",      "Device contact person."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.7.0"] = ("hp-fsDeviceModelNumber",  "Device model number."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.8.0"] = ("hp-fsDeviceSerialNumber", "Device serial number (FutureSmart)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.10.0"]= ("hp-fsDeviceFWVersion",    "Firmware version string."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.11.0"]= ("hp-fsDeviceSKU",          "Product SKU / part number."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.14.0"]= ("hp-fsFirmwareDateCode",   "Firmware build date code."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.1.16.0"]= ("hp-fsDeviceUUID",         "Device unique identifier (UUID)."),

        // ── FutureSmart — Status ───────────────────────────────────────────────
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.2.1.0"] = ("hp-fsDeviceStatus",          "Overall device status: idle(1) processing(2) stopped(3) other(4)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.2.2.0"] = ("hp-fsSubunitStatus",         "Subunit status code."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.2.3.0"] = ("hp-fsDeviceCondition",       "Device condition code."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.2.4.0"] = ("hp-fsDeviceStatusDescription","Status description string."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.2.5.0"] = ("hp-fsDevicePercentDone",     "Job completion percentage."),

        // ── FutureSmart — Printer counters ────────────────────────────────────
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.4.1.1.4.0"] = ("hp-fsPrintEngineLifetimeCount","Total pages printed (lifetime counter)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.4.1.1.5.0"] = ("hp-fsPrintEnginePowerCycles", "Power cycle count."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.4.1.1.6.0"] = ("hp-fsPrintEngineJamCount",    "Paper jam count."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.4.1.1.7.0"] = ("hp-fsScannerLifetimeCount",   "Scanner page count (MFP)."),

        // ── FutureSmart — Toner / Supply levels ───────────────────────────────
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.1"]  = ("hp-fsMarkerIndex",           "Marker/supply index."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.2"]  = ("hp-fsMarkerColor",           "Supply color name."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.3"]  = ("hp-fsMarkerSupplyDescription","Supply description (e.g. 'Black Toner')."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.4"]  = ("hp-fsMarkerSupplyLevel",     "Current toner/ink level (0-100 or raw)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.5"]  = ("hp-fsMarkerSupplyMaxCapacity","Maximum supply capacity."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.6"]  = ("hp-fsMarkerSupplyType",      "Supply type code."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.7"]  = ("hp-fsMarkerSupplyStatus",    "Supply status: ok(1) lowLevel(2) criticallyLow(3) notPresent(4) exhausted(5)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.8"]  = ("hp-fsMarkerLifetimeCount",   "Pages using this marker, lifetime."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.9"]  = ("hp-fsMarkerPartNumber",      "Cartridge/supply part number."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.6.1.1.10"] = ("hp-fsMarkerCartridgeSerial", "Cartridge serial number."),

        // ── FutureSmart — Input/Output trays ─────────────────────────────────
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.5.1.1.1"]  = ("hp-fsInputIndex",            "Paper tray index."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.5.1.1.2"]  = ("hp-fsInputName",             "Paper tray name (e.g. Tray 1)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.5.1.1.3"]  = ("hp-fsInputType",             "Tray type."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.5.1.1.4"]  = ("hp-fsInputMediaName",        "Media type/size in tray (Letter, A4, etc.)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.5.1.1.5"]  = ("hp-fsInputCapacityUnit",     "Capacity unit (sheets)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.5.1.1.6"]  = ("hp-fsInputMaxCapacity",      "Maximum sheet capacity."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.5.1.1.7"]  = ("hp-fsInputCurrentLevel",     "Current sheet count."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.5.1.1.8"]  = ("hp-fsInputStatus",           "Tray status: ok(1) full(2) empty(3) other(4) paperLow(5)."),

        // ── FutureSmart — Network/JetDirect ──────────────────────────────────
        ["1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.1.0"]  = ("hp-fsNetworkIPv4Address",    "JetDirect IPv4 address."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.2.0"]  = ("hp-fsNetworkSubnetMask",     "JetDirect subnet mask."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.3.0"]  = ("hp-fsNetworkDefaultGW",      "JetDirect default gateway."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.4.0"]  = ("hp-fsNetworkConfigBy",       "Network config method: bootp(1) dhcp(2) manual(3)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.5.0"]  = ("hp-fsNetworkMACAddress",     "JetDirect MAC address."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.6.0"]  = ("hp-fsNetworkHostname",       "JetDirect hostname."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.7.0"]  = ("hp-fsNetworkIPv6Address",    "JetDirect IPv6 address."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.9.0"]  = ("hp-fsNetworkLinkSpeed",      "Network link speed in Mbps."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.2.1.1.10.0"] = ("hp-fsNetworkDuplexMode",     "Duplex: fullDuplex(1) halfDuplex(2) auto(3)."),

        // ── FutureSmart — Security / EWS ─────────────────────────────────────
        ["1.3.6.1.4.1.11.2.3.9.4.2.3.1.1.0"]    = ("hp-fsEWSEnabled",            "Embedded web server enabled (true/false)."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.3.2.1.0"]    = ("hp-fsSNMPv1v2Enabled",       "SNMPv1/v2c read access enabled."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.3.2.2.0"]    = ("hp-fsSNMPv3Enabled",         "SNMPv3 enabled."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.3.2.3.0"]    = ("hp-fsSNMPWriteEnabled",      "SNMP write (SET) enabled."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.3.3.1.0"]    = ("hp-fsAdminPasswordSet",      "Admin password configured (true/false)."),

        // ── HP JetDirect legacy (pre-FutureSmart, still present on most devices) ──
        ["1.3.6.1.4.1.11.2.4.3.1.1.0"]  = ("hp-jdStatus",              "JetDirect overall status."),
        ["1.3.6.1.4.1.11.2.4.3.1.2.0"]  = ("hp-jdModelNumber",         "JetDirect model number."),
        ["1.3.6.1.4.1.11.2.4.3.1.3.0"]  = ("hp-jdSerialNumber",        "JetDirect serial number."),
        ["1.3.6.1.4.1.11.2.4.3.1.4.0"]  = ("hp-jdFirmwareVersion",     "JetDirect firmware version."),
        ["1.3.6.1.4.1.11.2.4.3.1.5.0"]  = ("hp-jdMACAddress",          "JetDirect MAC address."),
        ["1.3.6.1.4.1.11.2.4.3.1.6.0"]  = ("hp-jdIPAddress",           "JetDirect IP address."),
        ["1.3.6.1.4.1.11.2.4.3.1.7.0"]  = ("hp-jdSubnetMask",          "JetDirect subnet mask."),
        ["1.3.6.1.4.1.11.2.4.3.1.8.0"]  = ("hp-jdDefaultGateway",      "JetDirect default gateway."),
        ["1.3.6.1.4.1.11.2.4.3.1.9.0"]  = ("hp-jdDHCPEnabled",         "DHCP enabled (1=yes 2=no)."),
        ["1.3.6.1.4.1.11.2.4.3.5.1.0"]  = ("hp-jdPrintPageCount",      "Total pages printed (JetDirect)."),
        ["1.3.6.1.4.1.11.2.4.3.5.2.0"]  = ("hp-jdPrintErrors",         "Print error count."),
        ["1.3.6.1.4.1.11.2.4.3.7.1.1.0"]= ("hp-jdTonerBlackLevel",     "Black toner level percent."),
        ["1.3.6.1.4.1.11.2.4.3.7.1.2.0"]= ("hp-jdTonerCyanLevel",      "Cyan toner level percent."),
        ["1.3.6.1.4.1.11.2.4.3.7.1.3.0"]= ("hp-jdTonerMagentaLevel",   "Magenta toner level percent."),
        ["1.3.6.1.4.1.11.2.4.3.7.1.4.0"]= ("hp-jdTonerYellowLevel",    "Yellow toner level percent."),

        // ── HP sysObjectID OIDs (FutureSmart model identification) ───────────
        // Walk sysObjectID.0 and match prefix to identify model family
        ["1.3.6.1.4.1.11.2.3.9.1.1"]    = ("hp-LaserJetFamilyRoot",     "HP LaserJet family sysObjectID root."),
        ["1.3.6.1.4.1.11.2.3.9.4.2.1.3.3.1.4.1"] = ("hp-fsPrinterPersonality", "Printer language: PCL(1) PS(2) PCL6(3)."),
    };

    // ══════════════════════════════════════════════════════════════════════════
    // CISCO ENTERPRISE OID TABLE
    // Enterprise OID root: 1.3.6.1.4.1.9  (Cisco Systems)
    //
    // Major subtrees:
    //   1.3.6.1.4.1.9.2    → local (IOS MIB - general)
    //   1.3.6.1.4.1.9.3    → temporary (cisco temporary OIDs)
    //   1.3.6.1.4.1.9.5    → workgroup (CiscoWorks / old)
    //   1.3.6.1.4.1.9.9    → ciscoMgmt (primary managed MIBs)
    //   1.3.6.1.4.1.9.10   → ciscoExperiment
    //   1.3.6.1.4.1.9.12   → ciscoConfig
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, (string Name, string Desc)> CiscoOids = new()
    {
        // ── Cisco root & sysObjectIDs ─────────────────────────────────────────
        ["1.3.6.1.4.1.9"]           = ("cisco",                "Cisco Systems enterprise OID root."),
        ["1.3.6.1.4.1.9.1"]         = ("ciscoProducts",        "Cisco product sysObjectID root."),
        ["1.3.6.1.4.1.9.1.1"]       = ("cisco7000",            "Cisco 7000 series router."),
        ["1.3.6.1.4.1.9.1.208"]     = ("cisco2960",            "Cisco Catalyst 2960 series."),
        ["1.3.6.1.4.1.9.1.696"]     = ("cisco3750",            "Cisco Catalyst 3750 series."),
        ["1.3.6.1.4.1.9.1.1045"]    = ("cisco3560",            "Cisco Catalyst 3560 series."),
        ["1.3.6.1.4.1.9.1.1227"]    = ("cisco4500",            "Cisco Catalyst 4500 series."),
        ["1.3.6.1.4.1.9.1.1290"]    = ("ciscoASR1002",         "Cisco ASR 1002 router."),
        ["1.3.6.1.4.1.9.1.1745"]    = ("cisco2960X",           "Cisco Catalyst 2960-X series."),
        ["1.3.6.1.4.1.9.2"]         = ("ciscoLocal",           "Cisco local MIB subtree (IOS)."),
        ["1.3.6.1.4.1.9.9"]         = ("ciscoMgmt",            "Cisco Management MIB root."),

        // ── CISCO-PROCESS-MIB (CPU/Memory) — ciscoMgmt.109 ──────────────────
        ["1.3.6.1.4.1.9.9.109"]         = ("ciscoProcessMIB",       "Cisco process/CPU MIB."),
        ["1.3.6.1.4.1.9.9.109.1.1.1.1.3.1"] = ("cpmCPUTotal5sec",  "CPU utilization over last 5 seconds (%)."),
        ["1.3.6.1.4.1.9.9.109.1.1.1.1.4.1"] = ("cpmCPUTotal1min",  "CPU utilization over last 1 minute (%)."),
        ["1.3.6.1.4.1.9.9.109.1.1.1.1.5.1"] = ("cpmCPUTotal5min",  "CPU utilization over last 5 minutes (%)."),
        ["1.3.6.1.4.1.9.9.109.1.1.1.1.6.1"] = ("cpmCPUMonInterval","Monitoring interval seconds."),
        ["1.3.6.1.4.1.9.9.109.1.1.1.1.7.1"] = ("cpmCPUTotalMonIntervalValue","CPU % during monitoring interval."),
        ["1.3.6.1.4.1.9.9.109.1.1.1.1.8.1"] = ("cpmCPUInterruptMonIntervalValue","CPU interrupt % during interval."),

        // ── CISCO-MEMORY-POOL-MIB — ciscoMgmt.48 ─────────────────────────────
        ["1.3.6.1.4.1.9.9.48"]              = ("ciscoMemoryPoolMIB",   "Cisco memory pool MIB."),
        ["1.3.6.1.4.1.9.9.48.1.1.1.2.1"]   = ("ciscoMemoryPoolName",  "Memory pool name (Processor, I/O, etc.)."),
        ["1.3.6.1.4.1.9.9.48.1.1.1.5.1"]   = ("ciscoMemoryPoolUsed",  "Bytes currently in use."),
        ["1.3.6.1.4.1.9.9.48.1.1.1.6.1"]   = ("ciscoMemoryPoolFree",  "Bytes currently free."),
        ["1.3.6.1.4.1.9.9.48.1.1.1.7.1"]   = ("ciscoMemoryPoolLargestFree","Largest contiguous free block."),

        // ── CISCO-ENVMON-MIB (Temperature/Fans/Voltage) — ciscoMgmt.13 ───────
        ["1.3.6.1.4.1.9.9.13"]              = ("ciscoEnvMonMIB",        "Cisco environmental monitoring MIB."),
        ["1.3.6.1.4.1.9.9.13.1.2.1.2.1"]   = ("ciscoEnvMonTemperatureStatusDescr","Temperature sensor description."),
        ["1.3.6.1.4.1.9.9.13.1.2.1.3.1"]   = ("ciscoEnvMonTemperatureStatusValue","Temperature in Celsius."),
        ["1.3.6.1.4.1.9.9.13.1.2.1.6.1"]   = ("ciscoEnvMonTemperatureState","Temp state: normal(1) warning(2) critical(3) shutdown(4)."),
        ["1.3.6.1.4.1.9.9.13.1.3.1.2.1"]   = ("ciscoEnvMonVoltageStatusDescr","Voltage sensor description."),
        ["1.3.6.1.4.1.9.9.13.1.3.1.3.1"]   = ("ciscoEnvMonVoltageStatusValue","Voltage in millivolts."),
        ["1.3.6.1.4.1.9.9.13.1.3.1.6.1"]   = ("ciscoEnvMonVoltageState", "Voltage state: normal(1) warning(2) critical(3) shutdown(4)."),
        ["1.3.6.1.4.1.9.9.13.1.4.1.2.1"]   = ("ciscoEnvMonFanStatusDescr","Fan description."),
        ["1.3.6.1.4.1.9.9.13.1.4.1.3.1"]   = ("ciscoEnvMonFanState",     "Fan state: normal(1) warning(2) critical(3) shutdown(4) notPresent(5)."),
        ["1.3.6.1.4.1.9.9.13.1.5.1.2.1"]   = ("ciscoEnvMonSupplyStatusDescr","Power supply description."),
        ["1.3.6.1.4.1.9.9.13.1.5.1.3.1"]   = ("ciscoEnvMonSupplyState",  "Supply state: normal(1) warning(2) critical(3) shutdown(4) notPresent(5)."),

        // ── CISCO-FLASH-MIB — ciscoMgmt.10 ───────────────────────────────────
        ["1.3.6.1.4.1.9.9.10.1.1.1.1.4.1"] = ("ciscoFlashDeviceSize",    "Flash device total size in bytes."),
        ["1.3.6.1.4.1.9.9.10.1.1.1.1.6.1"] = ("ciscoFlashDeviceMinPartitionSize","Minimum partition size."),

        // ── CISCO-STACK-MIB / VSS — ciscoMgmt.500 ────────────────────────────
        ["1.3.6.1.4.1.9.9.500"]             = ("ciscoStackWiseMIB",       "Cisco StackWise MIB."),
        ["1.3.6.1.4.1.9.9.500.1.2.1.1.1"]  = ("cswSwitchNumCurrent",     "Number of switches in stack."),
        ["1.3.6.1.4.1.9.9.500.1.2.1.1.2"]  = ("cswSwitchRole",           "Switch role in stack: master(1) member(2) notMember(3)."),
        ["1.3.6.1.4.1.9.9.500.1.2.1.1.6"]  = ("cswSwitchState",          "Switch state in stack."),
        ["1.3.6.1.4.1.9.9.500.1.2.1.1.7"]  = ("cswSwitchMacAddress",     "Switch MAC address."),

        // ── ENTITY-MIB (RFC-2737) — standard but widely used with Cisco ───────
        ["1.3.6.1.2.1.47.1.1.1.1.2"]       = ("entPhysicalDescr",        "Physical entity description (chassis, module, port)."),
        ["1.3.6.1.2.1.47.1.1.1.1.5"]       = ("entPhysicalClass",        "Class: chassis(3) backplane(4) container(5) powerSupply(6) fan(7) sensor(8) module(9) port(10)."),
        ["1.3.6.1.2.1.47.1.1.1.1.7"]       = ("entPhysicalName",         "Physical name (e.g. 'GigabitEthernet1/0/1')."),
        ["1.3.6.1.2.1.47.1.1.1.1.8"]       = ("entPhysicalHardwareRev",  "Hardware revision."),
        ["1.3.6.1.2.1.47.1.1.1.1.9"]       = ("entPhysicalFirmwareRev",  "Firmware revision."),
        ["1.3.6.1.2.1.47.1.1.1.1.10"]      = ("entPhysicalSoftwareRev",  "Software revision."),
        ["1.3.6.1.2.1.47.1.1.1.1.11"]      = ("entPhysicalSerialNum",    "Physical entity serial number."),
        ["1.3.6.1.2.1.47.1.1.1.1.13"]      = ("entPhysicalModelName",    "Physical model name (e.g. 'WS-C3750X-24T-S')."),

        // ── CISCO-VLAN-MEMBERSHIP-MIB — ciscoMgmt.68 ─────────────────────────
        ["1.3.6.1.4.1.9.9.68"]              = ("ciscoVlanMembershipMIB",  "Cisco VLAN membership MIB."),
        ["1.3.6.1.4.1.9.9.68.1.2.1.1.2"]   = ("vmVlan",                  "VLAN assigned to this port."),
        ["1.3.6.1.4.1.9.9.68.1.2.2.1.2"]   = ("vmVlanType",              "VLAN assignment type: static(1) dynamic(2)."),

        // ── CISCO-VTP-MIB — ciscoMgmt.46 ─────────────────────────────────────
        ["1.3.6.1.4.1.9.9.46"]              = ("ciscoVtpMIB",             "Cisco VTP MIB."),
        ["1.3.6.1.4.1.9.9.46.1.1.1.1.1"]   = ("vtpVersion",              "VTP version: none(1) v1(2) v2(3) v3(4)."),
        ["1.3.6.1.4.1.9.9.46.1.1.1.1.2"]   = ("vtpMaxVlanStorage",       "Max VLANs for local config storage."),
        ["1.3.6.1.4.1.9.9.46.1.2.1.1.1"]   = ("vtpVlanIndex",            "VLAN index."),
        ["1.3.6.1.4.1.9.9.46.1.2.1.1.2"]   = ("vtpVlanName",             "VLAN name."),
        ["1.3.6.1.4.1.9.9.46.1.2.1.1.3"]   = ("vtpVlanState",            "VLAN state: operational(1) suspended(2) mtuTooBigForDevice(3) mtuTooBigForTrunk(4)."),

        // ── CISCO-STP-EXTENSIONS-MIB (Spanning Tree) — ciscoMgmt.82 ──────────
        ["1.3.6.1.4.1.9.9.82"]              = ("ciscoStpExtMIB",          "Cisco Spanning Tree Extensions MIB."),
        ["1.3.6.1.4.1.9.9.82.1.1.1.1.1"]   = ("stpxSpanningTreeType",    "STP type: pvstPlus(1) mistp(2) mistpPvstPlus(3) rstp(4) rapidPvstPlus(5) mst(6)."),
        ["1.3.6.1.4.1.9.9.82.1.2.1.1.1"]   = ("stpxPortIndex",           "STP port index."),
        ["1.3.6.1.4.1.9.9.82.1.2.1.1.3"]   = ("stpxPortEnable",          "Port STP enable status."),
        ["1.3.6.1.4.1.9.9.82.1.7.1.1.1"]   = ("stpxRSTPPortIndex",       "RSTP port index."),
        ["1.3.6.1.4.1.9.9.82.1.7.1.1.4"]   = ("stpxRSTPPortOperEdgePort","RSTP port operating as edge port."),

        // ── CISCO-CDP-MIB (Cisco Discovery Protocol) — ciscoMgmt.23 ──────────
        ["1.3.6.1.4.1.9.9.23"]              = ("ciscoCdpMIB",             "Cisco Discovery Protocol MIB."),
        ["1.3.6.1.4.1.9.9.23.1.1.1.1.1"]   = ("cdpInterfaceIfIndex",     "CDP interface index."),
        ["1.3.6.1.4.1.9.9.23.1.2.1.1.1"]   = ("cdpCacheIfIndex",         "CDP cache interface index."),
        ["1.3.6.1.4.1.9.9.23.1.2.1.1.3"]   = ("cdpCacheAddress",         "CDP neighbor IP address."),
        ["1.3.6.1.4.1.9.9.23.1.2.1.1.4"]   = ("cdpCacheVersion",         "CDP neighbor IOS version."),
        ["1.3.6.1.4.1.9.9.23.1.2.1.1.5"]   = ("cdpCacheDeviceId",        "CDP neighbor device ID / hostname."),
        ["1.3.6.1.4.1.9.9.23.1.2.1.1.6"]   = ("cdpCacheDevicePort",      "CDP neighbor port (e.g. GigabitEthernet0/1)."),
        ["1.3.6.1.4.1.9.9.23.1.2.1.1.7"]   = ("cdpCachePlatform",        "CDP neighbor platform / model."),
        ["1.3.6.1.4.1.9.9.23.1.2.1.1.8"]   = ("cdpCacheCapabilities",    "CDP neighbor capabilities bitmask."),
        ["1.3.6.1.4.1.9.9.23.1.2.1.1.11"]  = ("cdpCacheNativeVLAN",      "CDP neighbor native VLAN."),
        ["1.3.6.1.4.1.9.9.23.1.2.1.1.12"]  = ("cdpCacheDuplex",          "CDP neighbor duplex."),

        // ── CISCO-HSRP-MIB (HSRP) — ciscoMgmt.106 ───────────────────────────
        ["1.3.6.1.4.1.9.9.106"]             = ("ciscoHsrpMIB",            "Cisco HSRP MIB."),
        ["1.3.6.1.4.1.9.9.106.1.2.1.1.3"]  = ("cHsrpGrpVirtualIpAddr",  "HSRP virtual IP address."),
        ["1.3.6.1.4.1.9.9.106.1.2.1.1.5"]  = ("cHsrpGrpPriority",       "HSRP group priority (0-255)."),
        ["1.3.6.1.4.1.9.9.106.1.2.1.1.10"] = ("cHsrpGrpStandbyState",   "HSRP state: initial(1) learn(2) listen(3) speak(4) standby(5) active(6)."),
        ["1.3.6.1.4.1.9.9.106.1.2.1.1.11"] = ("cHsrpGrpActiveRouter",   "HSRP active router IP."),
        ["1.3.6.1.4.1.9.9.106.1.2.1.1.12"] = ("cHsrpGrpStandbyRouter",  "HSRP standby router IP."),

        // ── CISCO-QOS-PIB-MIB / CISCO-CLASS-BASED-QOS-MIB — ciscoMgmt.166 ───
        ["1.3.6.1.4.1.9.9.166"]             = ("cbQosMIB",               "Cisco Class-Based QoS MIB."),
        ["1.3.6.1.4.1.9.9.166.1.1.1.1.3"]  = ("cbQosIfIndex",           "QoS policy interface index."),
        ["1.3.6.1.4.1.9.9.166.1.5.1.1.8"]  = ("cbQosPoliceConformedByte","Bytes conforming to policed rate."),
        ["1.3.6.1.4.1.9.9.166.1.5.1.1.10"] = ("cbQosPoliceExceededByte", "Bytes exceeding policed rate."),
        ["1.3.6.1.4.1.9.9.166.1.6.1.1.3"]  = ("cbQosCMName",            "Class-map name."),
        ["1.3.6.1.4.1.9.9.166.1.7.1.1.7"]  = ("cbQosQueueDepth",        "Current queue depth."),
        ["1.3.6.1.4.1.9.9.166.1.7.1.1.12"] = ("cbQosTailDropPkt",       "Tail-dropped packet count."),

        // ── CISCO-BGP4-MIB / BGP-MIB (RFC-4273) ─────────────────────────────
        ["1.3.6.1.2.1.15.3.1.1"]            = ("bgpPeerIdentifier",       "BGP peer IP address."),
        ["1.3.6.1.2.1.15.3.1.2"]            = ("bgpPeerState",            "BGP peer state: idle(1) connect(2) active(3) opensent(4) openconfirm(5) established(6)."),
        ["1.3.6.1.2.1.15.3.1.3"]            = ("bgpPeerAdminStatus",      "BGP peer admin status: stop(1) start(2)."),
        ["1.3.6.1.2.1.15.3.1.7"]            = ("bgpPeerHoldTime",         "BGP negotiated hold time in seconds."),
        ["1.3.6.1.2.1.15.3.1.8"]            = ("bgpPeerKeepAlive",        "BGP keep-alive interval seconds."),
        ["1.3.6.1.2.1.15.3.1.16"]           = ("bgpPeerInUpdates",        "BGP updates received from peer."),
        ["1.3.6.1.2.1.15.3.1.17"]           = ("bgpPeerOutUpdates",       "BGP updates sent to peer."),
        ["1.3.6.1.2.1.15.3.1.24"]           = ("bgpPeerFsmEstablishedTime","Seconds peer has been in established state."),

        // ── OSPF-MIB (RFC-1850) ───────────────────────────────────────────────
        ["1.3.6.1.2.1.14.1.1.0"]            = ("ospfRouterId.0",          "OSPF router ID (IP format)."),
        ["1.3.6.1.2.1.14.1.2.0"]            = ("ospfAdminStat.0",         "OSPF admin status: enabled(1) disabled(2)."),
        ["1.3.6.1.2.1.14.7.1.6"]            = ("ospfNbrState",            "OSPF neighbor state: down(1) attempt(2) init(3) twoWay(4) exchangeStart(5) exchange(6) loading(7) full(8)."),
        ["1.3.6.1.2.1.14.7.1.7"]            = ("ospfNbrEvents",           "OSPF state-machine events for neighbor."),
        ["1.3.6.1.2.1.14.10.1.3"]           = ("ospfVirtNbrState",        "OSPF virtual neighbor state."),

        // ── EIGRP (Cisco-proprietary, CISCO-EIGRP-MIB) — ciscoMgmt.362 ───────
        ["1.3.6.1.4.1.9.9.362"]             = ("ciscoEigrpMIB",           "Cisco EIGRP MIB."),
        ["1.3.6.1.4.1.9.9.362.1.2.1.1.7"]  = ("cEigrpPeerAddrType",     "EIGRP neighbor address type."),
        ["1.3.6.1.4.1.9.9.362.1.2.1.1.8"]  = ("cEigrpPeerAddr",         "EIGRP neighbor IP address."),
        ["1.3.6.1.4.1.9.9.362.1.2.1.1.10"] = ("cEigrpHoldTime",         "EIGRP hold time seconds."),
        ["1.3.6.1.4.1.9.9.362.1.2.1.1.11"] = ("cEigrpUpTime",           "Time since adjacency formed."),
        ["1.3.6.1.4.1.9.9.362.1.2.1.1.14"] = ("cEigrpPktsEnqueued",     "EIGRP packets in queue."),

        // ── CISCO-CONFIG-MAN-MIB (config management) — ciscoMgmt.43 ──────────
        ["1.3.6.1.4.1.9.9.43"]              = ("ciscoConfigManMIB",       "Cisco configuration management MIB."),
        ["1.3.6.1.4.1.9.9.43.1.1.1.0"]     = ("ccmHistoryRunningLastChanged","sysUpTime of last running-config change."),
        ["1.3.6.1.4.1.9.9.43.1.1.2.0"]     = ("ccmHistoryRunningLastSaved",  "sysUpTime of last running-config save."),
        ["1.3.6.1.4.1.9.9.43.1.1.3.0"]     = ("ccmHistoryStartupLastChanged","sysUpTime of last startup-config change."),

        // ── CISCO-IETF-PW-MIB / Interface port states ────────────────────────
        ["1.3.6.1.4.1.9.9.315"]             = ("cportMIB",                "Cisco port MIB."),
        ["1.3.6.1.4.1.9.9.315.1.2.1.1.1"]  = ("cportIfIndex",           "Port interface index."),
        ["1.3.6.1.4.1.9.9.315.1.2.1.1.2"]  = ("cportName",              "Port name/description."),

        // ── CISCO-SYSLOG-MIB — ciscoMgmt.41 ──────────────────────────────────
        ["1.3.6.1.4.1.9.9.41"]              = ("ciscoSyslogMIB",          "Cisco syslog MIB."),
        ["1.3.6.1.4.1.9.9.41.1.2.3.1.5"]   = ("clogHistMsgText",         "Syslog message text."),
        ["1.3.6.1.4.1.9.9.41.1.2.3.1.4"]   = ("clogHistSeverity",        "Syslog severity: emergency(1) alert(2) critical(3) error(4) warning(5) notice(6) info(7) debug(8)."),

        // ── CISCO-NTP-MIB — ciscoMgmt.168 ────────────────────────────────────
        ["1.3.6.1.4.1.9.9.168"]             = ("ciscoNtpMIB",             "Cisco NTP MIB."),
        ["1.3.6.1.4.1.9.9.168.1.1.1.0"]    = ("cntpSysSyncStatus",       "NTP sync status: synced(1) notSynced(2)."),
        ["1.3.6.1.4.1.9.9.168.1.1.2.0"]    = ("cntpSysPeerAddress",      "NTP stratum 1 peer address."),
        ["1.3.6.1.4.1.9.9.168.1.1.8.0"]    = ("cntpSysStratum",          "NTP stratum level (1=best, 16=unsync)."),
        ["1.3.6.1.4.1.9.9.168.1.2.1.1.4"]  = ("cntpPeersPeerAddress",    "NTP peer address."),
        ["1.3.6.1.4.1.9.9.168.1.2.1.1.6"]  = ("cntpPeersLeapIndicator",  "Leap indicator: noWarning(0) addSecond(1) subtractSecond(2) alarmCondition(3)."),

        // ── CISCO-IOS-XR-INFRA-SYSDB / NX-OS OIDs ────────────────────────────
        ["1.3.6.1.4.1.9.12"]                = ("ciscoConfig",             "Cisco configuration MIB root."),
        ["1.3.6.1.4.1.9.12.3.1.3"]          = ("ciscoImageVersion",       "IOS image version string."),
        ["1.3.6.1.4.1.9.12.3.1.4"]          = ("ciscoImageDescription",   "IOS image description."),

        // ── Cisco trap OIDs ───────────────────────────────────────────────────
        ["1.3.6.1.4.1.9.9.13.3.0.1"]        = ("ciscoEnvMonShutdownNotif","Cisco environmental shutdown notification."),
        ["1.3.6.1.4.1.9.9.46.2.0.1"]        = ("vtpConfigRevNumberError", "VTP config revision number error trap."),
        ["1.3.6.1.4.1.9.9.46.2.0.2"]        = ("vtpConfigDigestError",    "VTP configuration digest error trap."),
        ["1.3.6.1.4.1.9.9.23.3.0.1"]        = ("cdpGlobalRun",            "CDP globally enabled/disabled notification."),
    };

    // ══════════════════════════════════════════════════════════════════════════
    // MERGED VENDOR OID TABLE  (HP + Cisco combined for fast lookup)
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, (string Name, string Desc)> VendorOids;

    static OidTranslator()
    {
        // Merge HP + Cisco into one dictionary at class-load time
        VendorOids = new Dictionary<string, (string, string)>(
            HpOids.Count + CiscoOids.Count);
        foreach (var kv in HpOids)    VendorOids[kv.Key] = kv.Value;
        foreach (var kv in CiscoOids) VendorOids[kv.Key] = kv.Value;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // STATUS ENUM INTERPRETATION MAPS
    // ══════════════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, Dictionary<int, string>> StatusEnums = new()
    {
        ["ifAdminStatus"]  = new() { [1] = "up", [2] = "down", [3] = "testing" },
        ["ifOperStatus"]   = new() { [1] = "up", [2] = "down", [3] = "testing",
                                     [4] = "unknown", [5] = "dormant", [6] = "notPresent", [7] = "lowerLayerDown" },
        ["hrDeviceStatus"] = new() { [1] = "unknown", [2] = "running", [3] = "warning",
                                     [4] = "testing", [5] = "down" },
        ["hrPrinterStatus"]= new() { [1] = "idle", [2] = "running", [3] = "warmup",
                                     [4] = "searching", [5] = "stopped" },
        ["ciscoEnvMonTemperatureState"] = new() { [1]="normal",[2]="warning",[3]="critical",[4]="shutdown" },
        ["ciscoEnvMonVoltageState"]     = new() { [1]="normal",[2]="warning",[3]="critical",[4]="shutdown" },
        ["ciscoEnvMonFanState"]         = new() { [1]="normal",[2]="warning",[3]="critical",[4]="shutdown",[5]="notPresent" },
        ["ciscoEnvMonSupplyState"]      = new() { [1]="normal",[2]="warning",[3]="critical",[4]="shutdown",[5]="notPresent" },
        ["cHsrpGrpStandbyState"]        = new() { [1]="initial",[2]="learn",[3]="listen",[4]="speak",[5]="standby",[6]="active" },
        ["bgpPeerState"]                = new() { [1]="idle",[2]="connect",[3]="active",[4]="opensent",[5]="openconfirm",[6]="established" },
        ["ospfNbrState"]                = new() { [1]="down",[2]="attempt",[3]="init",[4]="twoWay",[5]="exchangeStart",[6]="exchange",[7]="loading",[8]="full" },
        ["vtpVlanState"]                = new() { [1]="operational",[2]="suspended",[3]="mtuTooBigForDevice",[4]="mtuTooBigForTrunk" },
        ["vtpVersion"]                  = new() { [1]="none",[2]="v1",[3]="v2",[4]="v3" },
        ["stpxSpanningTreeType"]        = new() { [1]="pvstPlus",[2]="mistp",[3]="mistpPvstPlus",[4]="rstp",[5]="rapidPvstPlus",[6]="mst" },
        ["cntpSysSyncStatus"]           = new() { [1]="synced",[2]="notSynced" },
        ["hp-fsMarkerSupplyStatus"]     = new() { [1]="ok",[2]="lowLevel",[3]="criticallyLow",[4]="notPresent",[5]="exhausted" },
        ["hp-fsNetworkConfigBy"]        = new() { [1]="bootp",[2]="dhcp",[3]="manual" },
        ["hp-fsNetworkDuplexMode"]      = new() { [1]="fullDuplex",[2]="halfDuplex",[3]="auto" },
        ["hp-fsInputStatus"]            = new() { [1]="ok",[2]="full",[3]="empty",[4]="other",[5]="paperLow" },
        ["ipForwarding"]                = new() { [1]="forwarding",[2]="notForwarding" },
        ["clogHistSeverity"]            = new() { [1]="emergency",[2]="alert",[3]="critical",[4]="error",[5]="warning",[6]="notice",[7]="info",[8]="debug" },
    };

    // ══════════════════════════════════════════════════════════════════════════
    // TRANSLATION ENGINE
    // ══════════════════════════════════════════════════════════════════════════

    public (string SymbolicName, string Description) Translate(string numericOid)
    {
        var normalized = Normalize(numericOid);

        // 1. Exact MIB match (user-imported .mib file)
        var node = _mib.Lookup(normalized);
        if (node != null) return (FormatSymbolic(node, normalized), node.Description);

        // 2. Longest-prefix MIB match
        node = _mib.LongestPrefixMatch(normalized);
        if (node != null)
        {
            var inst = normalized[node.NumericOid.Length..].TrimStart('.');
            return ($"{node.Name}.{inst}", node.Description);
        }

        // 3. Exact standard OID match
        if (StandardOids.TryGetValue(normalized, out var std))
            return (std.Name, std.Desc);

        // 4. Exact vendor OID match (HP FutureSmart / Cisco)
        if (VendorOids.TryGetValue(normalized, out var vnd))
            return (vnd.Name, vnd.Desc);

        // 5. Longest-prefix match in standard OIDs
        var bestStd = LongestPrefixKey(StandardOids, normalized);
        if (bestStd != null)
        {
            var (name, desc) = StandardOids[bestStd];
            var inst = normalized[bestStd.Length..].TrimStart('.');
            return ($"{name}.{inst}", desc);
        }

        // 6. Longest-prefix match in vendor OIDs
        var bestVnd = LongestPrefixKey(VendorOids, normalized);
        if (bestVnd != null)
        {
            var (name, desc) = VendorOids[bestVnd];
            var inst = normalized[bestVnd.Length..].TrimStart('.');
            return ($"{name}.{inst}", desc);
        }

        // 7. Raw OID (unknown)
        return (numericOid, string.Empty);
    }

    public string InterpretValue(string numericOid, string rawValue, string snmpType)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return rawValue;

        var (symName, _) = Translate(numericOid);
        var baseName     = symName.Split('.')[0];

        // ── TimeTicks → human-readable uptime ────────────────────────────────
        if (snmpType.Contains("TimeTick", StringComparison.OrdinalIgnoreCase)
            || baseName.Contains("UpTime", StringComparison.OrdinalIgnoreCase)
            || baseName.Contains("upTime", StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(rawValue, out var ticks))
                return FormatUptime(ticks);
        }

        // ── Interface speed bps → Mbps ────────────────────────────────────────
        if (baseName == "ifSpeed" && long.TryParse(rawValue, out var bps))
            return bps >= 1_000_000 ? $"{bps / 1_000_000} Mbps" : $"{bps} bps";

        if (baseName == "ifHighSpeed" && long.TryParse(rawValue, out var mbps))
            return $"{mbps} Mbps";

        // ── HP toner levels (0-100 percent or special values) ─────────────────
        if (baseName is "hp-fsMarkerSupplyLevel" or "hp-jdTonerBlackLevel"
            or "hp-jdTonerCyanLevel" or "hp-jdTonerMagentaLevel" or "hp-jdTonerYellowLevel")
        {
            if (rawValue == "-1") return "Unknown / unrestricted";
            if (rawValue == "-2") return "Exhausted / not installed";
            if (int.TryParse(rawValue, out var pct) && pct >= 0) return $"{pct}%";
        }

        // ── Standard prtMarkerSuppliesLevel ───────────────────────────────────
        if (baseName == "prtMarkerSuppliesLevel")
        {
            if (rawValue == "-1") return "Unknown / unrestricted";
            if (rawValue == "-2") return "Exhausted";
            if (int.TryParse(rawValue, out var lvl) && lvl >= 0) return $"{lvl} units remaining";
        }

        // ── Cisco CPU % — plain integer, just append unit ─────────────────────
        if (baseName is "cpmCPUTotal5sec" or "cpmCPUTotal1min" or "cpmCPUTotal5min")
        {
            if (int.TryParse(rawValue, out var cpu)) return $"{cpu}%";
        }

        // ── Cisco memory bytes → human-readable ──────────────────────────────
        if (baseName is "ciscoMemoryPoolUsed" or "ciscoMemoryPoolFree" or "ciscoMemoryPoolLargestFree")
        {
            if (long.TryParse(rawValue, out var memBytes)) return FormatBytes(memBytes);
        }

        // ── Cisco temperature (millidegrees? no — Celsius directly) ──────────
        if (baseName == "ciscoEnvMonTemperatureStatusValue")
        {
            if (int.TryParse(rawValue, out var degC)) return $"{degC} °C";
        }

        // ── NTP stratum ───────────────────────────────────────────────────────
        if (baseName == "cntpSysStratum")
        {
            if (rawValue == "16") return "16 (unsynced)";
            if (int.TryParse(rawValue, out var stratum)) return $"Stratum {stratum}";
        }

        // ── Status enums from loaded MIB node ─────────────────────────────────
        var normalized = Normalize(numericOid);
        var node = _mib.LongestPrefixMatch(normalized) ?? _mib.Lookup(normalized);
        if (node?.Enumerations.Count > 0 && int.TryParse(rawValue, out var intVal))
            if (node.Enumerations.TryGetValue(intVal, out var enumLabel))
                return $"{enumLabel} ({intVal})";

        // ── Status enums from built-in table (standard + vendor) ─────────────
        if (StatusEnums.TryGetValue(baseName, out var enumMap)
            && int.TryParse(rawValue, out var ev)
            && enumMap.TryGetValue(ev, out var label))
            return $"{label} ({ev})";

        // ── OctetString hex → ASCII attempt ───────────────────────────────────
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

    // ══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private static string FormatSymbolic(MibNode node, string normalized)
    {
        if (normalized.Length > node.NumericOid.Length)
        {
            var instance = normalized[node.NumericOid.Length..].TrimStart('.');
            return $"{node.Name}.{instance}";
        }
        return node.Name;
    }

    private static string Normalize(string oid) =>
        oid.TrimStart('.').Trim();

    private static string? LongestPrefixKey(
        Dictionary<string, (string Name, string Desc)> dict, string normalized)
    {
        string? best    = null;
        var     bestLen = 0;
        foreach (var key in dict.Keys)
        {
            if (normalized.StartsWith(key, StringComparison.Ordinal) && key.Length > bestLen)
            {
                best    = key;
                bestLen = key.Length;
            }
        }
        return best;
    }

    private static string FormatUptime(long centiseconds)
    {
        var ts = TimeSpan.FromSeconds(centiseconds / 100.0);
        if (ts.TotalDays >= 1)
            return $"{(int)ts.TotalDays}d {ts.Hours:D2}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
        if (ts.TotalHours >= 1)
            return $"{ts.Hours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
        return $"{ts.Minutes}m {ts.Seconds:D2}s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }
}
