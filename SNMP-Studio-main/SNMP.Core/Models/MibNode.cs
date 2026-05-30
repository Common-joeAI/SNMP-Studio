namespace SNMP.Core.Models;

/// <summary>
/// A parsed node from a MIB file, keyed by its numeric OID.
/// </summary>
public sealed class MibNode
{
    public string NumericOid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Syntax { get; set; } = string.Empty;           // e.g. INTEGER, OCTET STRING
    public string Access { get; set; } = string.Empty;           // read-only, read-write, etc.
    public Dictionary<int, string> Enumerations { get; set; } = new(); // INTEGER enum values
}
