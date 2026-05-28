using SNMP.Core.Enums;

namespace SNMP.Core.Models;

/// <summary>
/// A named SNMPv3 credential set — persisted to %APPDATA%\SNMPStudio\v3profiles.json.
/// Passwords are stored as Base64-encoded encrypted blobs (DPAPI on Windows).
/// </summary>
public sealed class V3Profile
{
    public Guid         Id           { get; set; } = Guid.NewGuid();
    public string       Name         { get; set; } = string.Empty;   // friendly label
    public string       SecurityName { get; set; } = string.Empty;   // SNMP user name
    public AuthProtocol AuthProtocol { get; set; } = AuthProtocol.SHA1;
    public string       AuthPasswordEncrypted { get; set; } = string.Empty;
    public PrivProtocol PrivProtocol { get; set; } = PrivProtocol.AES128;
    public string       PrivPasswordEncrypted { get; set; } = string.Empty;
    public string       ContextName  { get; set; } = string.Empty;
    public DateTime     CreatedAt    { get; set; } = DateTime.Now;
}
