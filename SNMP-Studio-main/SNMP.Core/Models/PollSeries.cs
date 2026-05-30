namespace SNMP.Core.Models;

/// <summary>A live-poll series — one OID polled on an interval, values stored for charting.</summary>
public sealed class PollSeries
{
    public Guid   Id           { get; set; } = Guid.NewGuid();
    public string Label        { get; set; } = string.Empty;
    public string Oid          { get; set; } = string.Empty;
    public string SymbolicName { get; set; } = string.Empty;
    public int    IntervalSecs { get; set; } = 5;
    public bool   IsRunning    { get; set; }
    public List<PollPoint> Points { get; set; } = new();
    public string LastValue    { get; set; } = string.Empty;
    public string Unit         { get; set; } = string.Empty;   // e.g. "%", "Mbps"
}

public sealed class PollPoint
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double   Value     { get; set; }
}
