namespace SNMP.Core.Models;

/// <summary>
/// Represents a single OID→value pair returned from a GET or WALK operation.
/// </summary>
public sealed class SnmpResult
{
    public string Oid { get; set; } = string.Empty;           // numeric OID
    public string SymbolicName { get; set; } = string.Empty;  // MIB-resolved name, e.g. sysDescr.0
    public string Description { get; set; } = string.Empty;   // MIB short description
    public string RawValue { get; set; } = string.Empty;      // raw value as string
    public string HumanValue { get; set; } = string.Empty;    // interpreted value (uptime, enum, %)
    public string Type { get; set; } = string.Empty;          // SNMP type (OctetString, Integer32, etc.)
    public bool IsWritable { get; set; }
}
