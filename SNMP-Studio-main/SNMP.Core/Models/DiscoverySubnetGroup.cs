using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SNMP.Core.Models;

/// <summary>
/// Groups discovered hosts under a single subnet label.
/// Shown as a collapsible section in the Discovery page when
/// Group-by-Subnet mode is active.
/// </summary>
public sealed class DiscoverySubnetGroup : INotifyPropertyChanged
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>CIDR block — e.g. "192.168.10.0/24"</summary>
    public string Subnet { get; init; } = string.Empty;

    /// <summary>
    /// Optional friendly label shown in the header alongside the CIDR.
    /// The user can type this in the multi-subnet input (format: "label=CIDR").
    /// Falls back to the subnet string when not provided.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Header text: "Label  ·  192.168.10.0/24"</summary>
    public string DisplayHeader =>
        string.IsNullOrWhiteSpace(Label) || Label == Subnet
            ? Subnet
            : $"{Label}  ·  {Subnet}";

    // ── Live counts ───────────────────────────────────────────────────────────

    private int _totalScanned;
    public int TotalScanned
    {
        get => _totalScanned;
        set { _totalScanned = value; OnPropertyChanged(); OnPropertyChanged(nameof(SummaryLine)); }
    }

    // ── Hosts ─────────────────────────────────────────────────────────────────

    public ObservableCollection<DiscoveredHost> Hosts { get; } = new();

    // ── Derived stats (re-evaluated on demand) ────────────────────────────────

    public int ReachableCount  => Hosts.Count(h => h.Reachable);
    public int SnmpCount       => Hosts.Count(h => h.Reachable && !string.IsNullOrEmpty(h.BestCommunity));
    public int PrinterCount    => Hosts.Count(h => h.DeviceType.Contains("Printer"));
    public int SwitchCount     => Hosts.Count(h => h.DeviceType.Contains("Switch"));

    public string SummaryLine  =>
        $"{ReachableCount} reachable  ·  {SnmpCount} SNMP  ·  {PrinterCount} printers  ·  {SwitchCount} switches";

    public void RefreshStats()
    {
        OnPropertyChanged(nameof(ReachableCount));
        OnPropertyChanged(nameof(SnmpCount));
        OnPropertyChanged(nameof(PrinterCount));
        OnPropertyChanged(nameof(SwitchCount));
        OnPropertyChanged(nameof(SummaryLine));
    }

    // ── Expand / collapse ─────────────────────────────────────────────────────

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    // ── INPC ──────────────────────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
