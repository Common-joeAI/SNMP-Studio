using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Services;

/// <summary>
/// Converts a flat list of SnmpResults (from a walk of a table OID) into a
/// structured SnmpTableResult with named columns and indexed rows.
///
/// SNMP table OID structure:
///   &lt;tableOid&gt;.&lt;entryOid&gt;.&lt;columnId&gt;.&lt;rowIndex&gt;
/// e.g.  1.3.6.1.2.1.2.2.1.2.1  →  ifTable.ifEntry.ifDescr.1
///
/// The parser groups by the last numeric arc(s) (row index) and the
/// penultimate arc (column), then maps column numbers to symbolic names
/// via the MIB repository / OID translator.
/// </summary>
public sealed class TableParser
{
    private readonly IOidTranslator _translator;

    public TableParser(IOidTranslator translator)
    {
        _translator = translator;
    }

    /// <summary>
    /// Auto-detects whether results look like a table and parses them.
    /// Returns null if results are not table-structured.
    /// </summary>
    public SnmpTableResult? TryParse(IEnumerable<SnmpResult> results, string baseOid)
    {
        var list = results.ToList();
        if (list.Count < 2) return null;

        // Strip leading dot from base OID for prefix matching
        var prefix = baseOid.TrimStart('.');

        // Collect OID arcs beyond the base
        var parsed = new List<(string ColArc, string RowIndex, SnmpResult Result)>();
        foreach (var r in list)
        {
            var oid = r.Oid.TrimStart('.');
            if (!oid.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var tail = oid[prefix.Length..].TrimStart('.');
            if (string.IsNullOrEmpty(tail)) continue;

            // tail = colId.rowIndex  (e.g. "2.1" or "1.1.1.0")
            // We take the first arc as column, rest as row index
            var dotIdx = tail.IndexOf('.');
            if (dotIdx < 0) continue;
            var colArc  = tail[..dotIdx];
            var rowIdx  = tail[(dotIdx + 1)..];
            parsed.Add((colArc, rowIdx, r));
        }

        if (parsed.Count == 0) return null;

        // Distinct columns and rows
        var columns  = parsed.Select(p => p.ColArc).Distinct().OrderBy(int.Parse).ToList();
        var rowIdxs  = parsed.Select(p => p.RowIndex).Distinct().ToList();

        // Needs at least 2 columns or 2 rows to be worth tabulating
        if (columns.Count < 2 && rowIdxs.Count < 2) return null;

        var (tableName, _) = _translator.Translate(prefix);

        var table = new SnmpTableResult
        {
            TableName = tableName != prefix ? tableName : baseOid,
            BaseOid   = baseOid
        };

        // Build column metadata
        foreach (var col in columns)
        {
            var colOid  = $"{prefix}.{col}";
            var (name, _) = _translator.Translate(colOid);
            table.ColumnOids.Add(colOid);
            table.ColumnNames.Add(name != colOid ? name : $"col{col}");
        }

        // Build rows
        foreach (var rowIdx in rowIdxs)
        {
            var row = new SnmpTableRow { Index = rowIdx };
            foreach (var col in columns)
            {
                var cell = parsed.FirstOrDefault(p => p.ColArc == col && p.RowIndex == rowIdx);
                var colOid = $"{prefix}.{col}";
                row.Cells[colOid] = cell.Result?.HumanValue
                    ?? cell.Result?.RawValue
                    ?? string.Empty;
            }
            table.Rows.Add(row);
        }

        return table;
    }

    /// <summary>
    /// Well-known SNMP tables with their base OIDs.
    /// Used to offer quick-jump shortcuts in the UI.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Oid)> KnownTables = new[]
    {
        ("ifTable — Interfaces",            "1.3.6.1.2.1.2.2"),
        ("ifXTable — Extended Interfaces",  "1.3.6.1.2.1.31.1.1"),
        ("ipAddrTable — IP Addresses",      "1.3.6.1.2.1.4.20"),
        ("hrSWInstalledTable — SW List",    "1.3.6.1.2.1.25.6.3"),
        ("hrDeviceTable — Devices",         "1.3.6.1.2.1.25.3.2"),
        ("prtMarkerSuppliesTable — Toner",  "1.3.6.1.2.1.43.11.1"),
        ("prtInputTable — Paper Trays",     "1.3.6.1.2.1.43.8.2"),
        ("tcpConnTable — TCP Connections",  "1.3.6.1.2.1.6.13"),
        ("udpTable — UDP Listeners",        "1.3.6.1.2.1.7.5"),
        ("atTable — ARP Cache",             "1.3.6.1.2.1.3.1"),
    };
}
