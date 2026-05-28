using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface IOidTranslator
{
    /// <summary>
    /// Translate a numeric OID to a symbolic name and description.
    /// Falls back to well-known standard OIDs if no MIB is loaded.
    /// </summary>
    (string SymbolicName, string Description) Translate(string numericOid);

    /// <summary>
    /// Produce a human-readable interpretation of a value given its OID context.
    /// Handles uptime ticks, status enums, supply percentages, etc.
    /// </summary>
    string InterpretValue(string numericOid, string rawValue, string snmpType);
}
