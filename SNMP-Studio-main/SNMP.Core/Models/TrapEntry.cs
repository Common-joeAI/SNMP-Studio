namespace SNMP.Core.Models;

/// <summary>A single received SNMP trap.</summary>
public sealed class TrapEntry
{
    public DateTime ReceivedAt  { get; set; } = DateTime.Now;
    public string   SourceIp    { get; set; } = string.Empty;
    public string   Version     { get; set; } = string.Empty;
    public string   Community   { get; set; } = string.Empty;
    public string   TrapOid     { get; set; } = string.Empty;   // snmpTrapOID
    public string   TrapName    { get; set; } = string.Empty;   // MIB-resolved
    public string   Enterprise  { get; set; } = string.Empty;   // v1 enterprise OID
    public int      GenericType { get; set; }                   // v1 generic trap type
    public List<TrapVarBind> VarBinds { get; set; } = new();
}

public sealed class TrapVarBind
{
    public string Oid         { get; set; } = string.Empty;
    public string SymbolicName { get; set; } = string.Empty;
    public string Value       { get; set; } = string.Empty;
    public string Type        { get; set; } = string.Empty;
}
