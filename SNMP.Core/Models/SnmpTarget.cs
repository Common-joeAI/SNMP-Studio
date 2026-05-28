using SNMP.Core.Enums;
using System.Security;

namespace SNMP.Core.Models;

/// <summary>
/// Describes a remote SNMP agent — address, version, and credentials.
/// SecureString is used for sensitive fields so they are never stored as plain managed strings.
/// </summary>
public sealed class SnmpTarget
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 161;
    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;
    public int TimeoutMs { get; set; } = 3000;
    public int Retries { get; set; } = 1;

    // v1 / v2c
    public string Community { get; set; } = "public";
    public string WriteCommunity { get; set; } = "private";

    // v3
    public string SecurityName { get; set; } = string.Empty;
    public AuthProtocol AuthProtocol { get; set; } = AuthProtocol.None;
    public SecureString? AuthPassword { get; set; }
    public PrivProtocol PrivProtocol { get; set; } = PrivProtocol.None;
    public SecureString? PrivPassword { get; set; }
    public string ContextName { get; set; } = string.Empty;
    public string EngineId { get; set; } = string.Empty;
}
