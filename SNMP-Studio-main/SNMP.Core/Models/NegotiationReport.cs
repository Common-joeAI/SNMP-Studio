using SNMP.Core.Enums;

namespace SNMP.Core.Models;

public sealed class NegotiationReport
{
    public bool Success { get; set; }
    public SnmpVersion? WorkingVersion { get; set; }
    public string? WorkingCommunity { get; set; }
    public NegotiationResult Result { get; set; }
    public string FriendlyMessage { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public List<string> AttemptLog { get; set; } = new();
}
