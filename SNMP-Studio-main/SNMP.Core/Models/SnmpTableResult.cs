namespace SNMP.Core.Models;

/// <summary>
/// Represents a parsed SNMP table — e.g. ifTable, hrSWInstalledTable, prtMarkerSuppliesTable.
/// Rows are keyed by the trailing index suffix (.1, .2 …).
/// Columns are keyed by the column OID base (last arc before index).
/// </summary>
public sealed class SnmpTableResult
{
    public string       TableName  { get; set; } = string.Empty;
    public string       BaseOid    { get; set; } = string.Empty;
    public List<string> ColumnNames { get; set; } = new();   // symbolic names
    public List<string> ColumnOids  { get; set; } = new();   // numeric bases
    public List<SnmpTableRow> Rows  { get; set; } = new();
}

public sealed class SnmpTableRow
{
    public string Index { get; set; } = string.Empty;
    /// <summary>Column OID base → human value for this row.</summary>
    public Dictionary<string, string> Cells { get; set; } = new();
}
