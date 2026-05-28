using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using SNMP.Core.Enums;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;
using SNMP.Engine.Services;

namespace SNMP.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    // ── Services ──────────────────────────────────────────────────────────────
    private readonly ISnmpClient         _client;
    private readonly INegotiationService _negotiation;
    private readonly IMibRepository      _mib;
    private readonly IOidTranslator      _translator;
    private readonly ILogService         _log;
    private readonly IExportService      _export;
    private readonly ITrapReceiver       _trapReceiver;
    private readonly IDiscoveryService   _discovery;
    private readonly ITargetStore        _targetStore;
    private readonly IV3ProfileStore     _v3Store;
    private readonly TableParser         _tableParser;

    private CancellationTokenSource? _cts;

    // ══════════════════════════════════════════════════════════════════════════
    // Collections
    // ══════════════════════════════════════════════════════════════════════════
    public ObservableCollection<SnmpResult>     Results          { get; } = new();
    public ObservableCollection<SnmpResult>     FilteredResults  { get; } = new();
    public ObservableCollection<LogEntry>       LogEntries       { get; } = new();
    public ObservableCollection<MibModuleInfo>  MibModules       { get; } = new();
    public ObservableCollection<TrapEntry>      Traps            { get; } = new();
    public ObservableCollection<DiscoveredHost> DiscoveredHosts  { get; } = new();
    public ObservableCollection<SavedTarget>    SavedTargets     { get; } = new();
    public ObservableCollection<PollSeries>     PollSeries       { get; } = new();
    public ObservableCollection<MibNode>        FilteredTree     { get; } = new();
    public ObservableCollection<SnmpResult>     FavouriteOids    { get; } = new();
    public ObservableCollection<V3Profile>      V3Profiles       { get; } = new();

    // Table view
    public ObservableCollection<SnmpTableResult> Tables          { get; } = new();
    private SnmpTableResult? _selectedTable;
    public SnmpTableResult? SelectedTable
    {
        get => _selectedTable;
        set { Set(ref _selectedTable, value); RebuildTableColumns(); }
    }
    public ObservableCollection<SnmpTableRow> TableRows          { get; } = new();

    // ══════════════════════════════════════════════════════════════════════════
    // Connection
    // ══════════════════════════════════════════════════════════════════════════
    private string _host = string.Empty;
    public string Host { get => _host; set => Set(ref _host, value); }

    private int _port = 161;
    public int Port { get => _port; set => Set(ref _port, value); }

    private SnmpVersion _version = SnmpVersion.V2c;
    public SnmpVersion Version
    {
        get => _version;
        set { Set(ref _version, value); OnPropertyChanged(nameof(IsV3)); }
    }
    public SnmpVersion[] Versions { get; } = Enum.GetValues<SnmpVersion>();
    public bool IsV3 => Version == SnmpVersion.V3;

    private string _community = "public";
    public string Community { get => _community; set => Set(ref _community, value); }

    private string _rootOid = "1.3.6.1.2.1";
    public string RootOid { get => _rootOid; set => Set(ref _rootOid, value); }

    // ── SNMPv3 fields ─────────────────────────────────────────────────────────
    private string _v3SecurityName = string.Empty;
    public string V3SecurityName { get => _v3SecurityName; set => Set(ref _v3SecurityName, value); }

    private AuthProtocol _v3AuthProto = AuthProtocol.SHA1;
    public AuthProtocol V3AuthProto { get => _v3AuthProto; set => Set(ref _v3AuthProto, value); }
    public AuthProtocol[] AuthProtocols { get; } = Enum.GetValues<AuthProtocol>();

    private string _v3AuthPassword = string.Empty;
    public string V3AuthPassword { get => _v3AuthPassword; set => Set(ref _v3AuthPassword, value); }

    private PrivProtocol _v3PrivProto = PrivProtocol.AES128;
    public PrivProtocol V3PrivProto { get => _v3PrivProto; set => Set(ref _v3PrivProto, value); }
    public PrivProtocol[] PrivProtocols { get; } = Enum.GetValues<PrivProtocol>();

    private string _v3PrivPassword = string.Empty;
    public string V3PrivPassword { get => _v3PrivPassword; set => Set(ref _v3PrivPassword, value); }

    private string _v3ContextName = string.Empty;
    public string V3ContextName { get => _v3ContextName; set => Set(ref _v3ContextName, value); }

    private V3Profile? _selectedV3Profile;
    public V3Profile? SelectedV3Profile
    {
        get => _selectedV3Profile;
        set { Set(ref _selectedV3Profile, value); (DeleteV3ProfileCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }
    private string _v3ProfileName = string.Empty;
    public string V3ProfileName { get => _v3ProfileName; set => Set(ref _v3ProfileName, value); }

    // ══════════════════════════════════════════════════════════════════════════
    // Status / busy
    // ══════════════════════════════════════════════════════════════════════════
    private string _statusText = "Ready.";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { Set(ref _isBusy, value); RaiseCommandsChanged(); } }

    private int _oidCount;
    public int OidCount { get => _oidCount; set => Set(ref _oidCount, value); }

    private string _elapsed = "00:00";
    public string Elapsed { get => _elapsed; set => Set(ref _elapsed, value); }

    private int _mibCount;
    public int MibCount { get => _mibCount; set => Set(ref _mibCount, value); }

    // ══════════════════════════════════════════════════════════════════════════
    // Result filter
    // ══════════════════════════════════════════════════════════════════════════
    private string _resultFilter = string.Empty;
    public string ResultFilter
    {
        get => _resultFilter;
        set { Set(ref _resultFilter, value); ApplyResultFilter(); }
    }
    private bool _filterActive;
    public bool FilterActive { get => _filterActive; set => Set(ref _filterActive, value); }

    private SnmpResult? _selectedResult;
    public SnmpResult? SelectedResult
    {
        get => _selectedResult;
        set { Set(ref _selectedResult, value); (AddFavouriteCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MIB / OID tree
    // ══════════════════════════════════════════════════════════════════════════
    private MibModuleInfo? _selectedModule;
    public MibModuleInfo? SelectedModule
    {
        get => _selectedModule;
        set { Set(ref _selectedModule, value); (RemoveModuleCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }
    private bool _isDragOver;
    public bool IsDragOver { get => _isDragOver; set => Set(ref _isDragOver, value); }

    private string _treeFilter = string.Empty;
    public string TreeFilter
    {
        get => _treeFilter;
        set { Set(ref _treeFilter, value); ApplyTreeFilter(); }
    }
    private List<MibNode> _allTreeNodes = new();

    // ══════════════════════════════════════════════════════════════════════════
    // Trap Receiver
    // ══════════════════════════════════════════════════════════════════════════
    private bool _trapListening;
    public bool TrapListening { get => _trapListening; set => Set(ref _trapListening, value); }

    private int _trapPort = 162;
    public int TrapPort { get => _trapPort; set => Set(ref _trapPort, value); }

    private int _trapCount;
    public int TrapCount { get => _trapCount; set => Set(ref _trapCount, value); }

    private TrapEntry? _selectedTrap;
    public TrapEntry? SelectedTrap
    {
        get => _selectedTrap;
        set { Set(ref _selectedTrap, value); OnPropertyChanged(nameof(SelectedTrapVarBinds)); }
    }
    public IEnumerable<TrapVarBind> SelectedTrapVarBinds =>
        _selectedTrap?.VarBinds ?? Enumerable.Empty<TrapVarBind>();

    private string _trapFilter = string.Empty;
    public string TrapFilter
    {
        get => _trapFilter;
        set { Set(ref _trapFilter, value); ApplyTrapFilter(); }
    }
    public ObservableCollection<TrapEntry> FilteredTraps { get; } = new();

    // ══════════════════════════════════════════════════════════════════════════
    // Discovery
    // ══════════════════════════════════════════════════════════════════════════
    private string _discoveryCidr = "192.168.1.0/24";
    public string DiscoveryCidr { get => _discoveryCidr; set => Set(ref _discoveryCidr, value); }

    private string _discoveryCommunities = "public,private";
    public string DiscoveryCommunities { get => _discoveryCommunities; set => Set(ref _discoveryCommunities, value); }

    private int _discoveryCount;
    public int DiscoveryCount { get => _discoveryCount; set => Set(ref _discoveryCount, value); }

    private bool _isDiscovering;
    public bool IsDiscovering { get => _isDiscovering; set { Set(ref _isDiscovering, value); RaiseCommandsChanged(); } }

    // ══════════════════════════════════════════════════════════════════════════
    // Live Polling + OxyPlot
    // ══════════════════════════════════════════════════════════════════════════
    private string _pollOid = string.Empty;
    public string PollOid { get => _pollOid; set => Set(ref _pollOid, value); }

    private int _pollIntervalSecs = 5;
    public int PollIntervalSecs { get => _pollIntervalSecs; set => Set(ref _pollIntervalSecs, value); }

    private PollSeries? _selectedPoll;
    public PollSeries? SelectedPoll
    {
        get => _selectedPoll;
        set { Set(ref _selectedPoll, value); RebuildChart(); (RemovePollCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    private PlotModel? _chartModel;
    public PlotModel? ChartModel { get => _chartModel; set => Set(ref _chartModel, value); }

    // ══════════════════════════════════════════════════════════════════════════
    // Saved Targets
    // ══════════════════════════════════════════════════════════════════════════
    private SavedTarget? _selectedSavedTarget;
    public SavedTarget? SelectedSavedTarget
    {
        get => _selectedSavedTarget;
        set { Set(ref _selectedSavedTarget, value); (DeleteTargetCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }
    private string _saveTargetLabel = string.Empty;
    public string SaveTargetLabel { get => _saveTargetLabel; set => Set(ref _saveTargetLabel, value); }

    // ══════════════════════════════════════════════════════════════════════════
    // Table quick-jump
    // ══════════════════════════════════════════════════════════════════════════
    public IReadOnlyList<(string Name, string Oid)> KnownTables => TableParser.KnownTables;

    // ══════════════════════════════════════════════════════════════════════════
    // Write mode / SET
    // ══════════════════════════════════════════════════════════════════════════
    private bool _writeModeEnabled;
    public bool WriteModeEnabled
    {
        get => _writeModeEnabled;
        set
        {
            if (value && MessageBox.Show(
                    "Enabling Write Mode allows SET operations that can modify device configuration.\n\nOnly proceed if you understand the risks.",
                    "Enable Write Mode?", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                == MessageBoxResult.Cancel) return;
            Set(ref _writeModeEnabled, value);
        }
    }
    private string _setOid   = string.Empty;
    public string SetOid   { get => _setOid;   set => Set(ref _setOid, value); }
    private string _setType  = "INTEGER";
    public string SetType  { get => _setType;  set => Set(ref _setType, value); }
    private string _setValue = string.Empty;
    public string SetValue { get => _setValue; set => Set(ref _setValue, value); }

    // ══════════════════════════════════════════════════════════════════════════
    // Commands
    // ══════════════════════════════════════════════════════════════════════════
    public ICommand WalkCommand              { get; }
    public ICommand GetCommand               { get; }
    public ICommand CancelCommand            { get; }
    public ICommand NegotiateCommand         { get; }
    public ICommand ImportMibCommand         { get; }
    public ICommand RemoveModuleCommand      { get; }
    public ICommand ClearMibCacheCommand     { get; }
    public ICommand ExportCsvCommand         { get; }
    public ICommand ExportJsonCommand        { get; }
    public ICommand ExportBundleCommand      { get; }
    public ICommand SetCommand               { get; }
    public ICommand ClearLogCommand          { get; }
    public ICommand PrinterQuickViewCommand  { get; }
    public ICommand StartTrapCommand         { get; }
    public ICommand StopTrapCommand          { get; }
    public ICommand ClearTrapsCommand        { get; }
    public ICommand StartDiscoveryCommand    { get; }
    public ICommand CancelDiscoveryCommand   { get; }
    public ICommand ConnectDiscoveredCommand { get; }
    public ICommand AddPollCommand           { get; }
    public ICommand RemovePollCommand        { get; }
    public ICommand SaveTargetCommand        { get; }
    public ICommand LoadTargetCommand        { get; }
    public ICommand DeleteTargetCommand      { get; }
    public ICommand AddFavouriteCommand      { get; }
    public ICommand RemoveFavouriteCommand   { get; }
    public ICommand CopyRowCommand           { get; }
    public ICommand CopyOidCommand           { get; }
    public ICommand SetRootOidFromTreeCommand { get; }
    public ICommand WalkTableCommand         { get; }
    public ICommand SaveV3ProfileCommand     { get; }
    public ICommand LoadV3ProfileCommand     { get; }
    public ICommand DeleteV3ProfileCommand   { get; }

    // ══════════════════════════════════════════════════════════════════════════
    // Constructor
    // ══════════════════════════════════════════════════════════════════════════
    public MainViewModel(
        ISnmpClient client, INegotiationService negotiation,
        IMibRepository mib, IOidTranslator translator,
        ILogService log, IExportService export,
        ITrapReceiver trapReceiver, IDiscoveryService discovery,
        ITargetStore targetStore, IV3ProfileStore v3Store,
        TableParser tableParser)
    {
        _client      = client;
        _negotiation = negotiation;
        _mib         = mib;
        _translator  = translator;
        _log         = log;
        _export      = export;
        _trapReceiver = trapReceiver;
        _discovery   = discovery;
        _targetStore = targetStore;
        _v3Store     = v3Store;
        _tableParser = tableParser;

        _log.EntryAdded += e => App.Current.Dispatcher.Invoke(() => LogEntries.Add(e));
        _trapReceiver.TrapReceived += OnTrapReceived;

        WalkCommand              = new RelayCommand(async () => await DoWalkAsync(),              () => !IsBusy);
        GetCommand               = new RelayCommand(async () => await DoGetAsync(),               () => !IsBusy);
        CancelCommand            = new RelayCommand(CancelAll,                                    () => IsBusy || IsDiscovering);
        NegotiateCommand         = new RelayCommand(async () => await DoNegotiateAsync(),         () => !IsBusy);
        ImportMibCommand         = new RelayCommand(async () => await DoImportMibAsync());
        RemoveModuleCommand      = new RelayCommand(async () => await DoRemoveModuleAsync(),      () => SelectedModule != null);
        ClearMibCacheCommand     = new RelayCommand(async () => await DoClearMibCacheAsync());
        ExportCsvCommand         = new RelayCommand(async () => await DoExportCsvAsync(),         () => Results.Count > 0);
        ExportJsonCommand        = new RelayCommand(async () => await DoExportJsonAsync(),        () => Results.Count > 0);
        ExportBundleCommand      = new RelayCommand(async () => await DoExportBundleAsync());
        SetCommand               = new RelayCommand(async () => await DoSetAsync(),               () => WriteModeEnabled && !IsBusy);
        ClearLogCommand          = new RelayCommand(() => { _log.Clear(); LogEntries.Clear(); });
        PrinterQuickViewCommand  = new RelayCommand(async () => await DoPrinterQuickViewAsync(),  () => !IsBusy);
        StartTrapCommand         = new RelayCommand(DoStartTraps,                                 () => !TrapListening);
        StopTrapCommand          = new RelayCommand(DoStopTraps,                                  () => TrapListening);
        ClearTrapsCommand        = new RelayCommand(() => { Traps.Clear(); FilteredTraps.Clear(); TrapCount = 0; });
        StartDiscoveryCommand    = new RelayCommand(async () => await DoDiscoveryAsync(),         () => !IsDiscovering);
        CancelDiscoveryCommand   = new RelayCommand(CancelAll,                                    () => IsDiscovering);
        ConnectDiscoveredCommand = new RelayCommand<DiscoveredHost>(ConnectDiscovered);
        AddPollCommand           = new RelayCommand(AddPollSeries,                                () => !string.IsNullOrWhiteSpace(PollOid));
        RemovePollCommand        = new RelayCommand(RemovePollSeries,                             () => SelectedPoll != null);
        SaveTargetCommand        = new RelayCommand(async () => await DoSaveTargetAsync(),        () => !string.IsNullOrWhiteSpace(Host));
        LoadTargetCommand        = new RelayCommand<SavedTarget>(LoadTarget);
        DeleteTargetCommand      = new RelayCommand(async () => await DoDeleteTargetAsync(),      () => SelectedSavedTarget != null);
        AddFavouriteCommand      = new RelayCommand(AddFavourite,                                 () => SelectedResult != null);
        RemoveFavouriteCommand   = new RelayCommand<SnmpResult>(r => FavouriteOids.Remove(r));
        CopyRowCommand           = new RelayCommand<SnmpResult>(CopyRow);
        CopyOidCommand           = new RelayCommand<SnmpResult>(r => Clipboard.SetText(r?.Oid ?? string.Empty));
        SetRootOidFromTreeCommand = new RelayCommand<MibNode>(n => { if (n != null) RootOid = n.NumericOid; });
        WalkTableCommand         = new RelayCommand<string>(async oid => await DoWalkTableAsync(oid ?? string.Empty));
        SaveV3ProfileCommand     = new RelayCommand(async () => await DoSaveV3ProfileAsync(),     () => !string.IsNullOrWhiteSpace(V3ProfileName));
        LoadV3ProfileCommand     = new RelayCommand<V3Profile>(LoadV3Profile);
        DeleteV3ProfileCommand   = new RelayCommand(async () => await DoDeleteV3ProfileAsync(),   () => SelectedV3Profile != null);

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        StatusText = "Loading MIB cache…";
        var mibCount = await _mib.LoadCacheAsync();
        RefreshMibModules(); RefreshMibTree();
        MibCount = _mib.Count;
        StatusText = mibCount > 0
            ? $"MIB cache restored — {mibCount} nodes. Ready."
            : "Ready. Import MIB files to enable OID translation.";

        var targets  = await _targetStore.LoadAsync();
        foreach (var t in targets) SavedTargets.Add(t);

        var profiles = await _v3Store.LoadAsync();
        foreach (var p in profiles) V3Profiles.Add(p);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Walk / GET / Negotiate
    // ══════════════════════════════════════════════════════════════════════════
    private async Task DoWalkAsync()
    {
        Results.Clear(); FilteredResults.Clear(); Tables.Clear(); OidCount = 0;
        IsBusy = true; _cts = new CancellationTokenSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (_, _) => Elapsed = $"{sw.Elapsed:mm\\:ss}";
        timer.Start();
        StatusText = $"Walking {RootOid}…";
        _log.Info($"WALK started: {Host}:{Port} v={Version} oid={RootOid}");
        try
        {
            await foreach (var r in _client.WalkAsync(BuildTarget(), RootOid, _cts.Token))
            {
                Enrich(r);
                App.Current.Dispatcher.Invoke(() =>
                {
                    Results.Add(r);
                    if (MatchesFilter(r)) FilteredResults.Add(r);
                    OidCount++;
                });
            }
            // Try table auto-detect
            var table = _tableParser.TryParse(Results, RootOid);
            if (table != null)
            {
                App.Current.Dispatcher.Invoke(() => { Tables.Add(table); SelectedTable = table; });
                _log.Info($"Table detected: {table.TableName} ({table.Rows.Count} rows, {table.ColumnNames.Count} cols)");
            }
            StatusText = $"Walk complete — {OidCount} OIDs in {sw.Elapsed:mm\\:ss}.";
            _log.Info($"WALK done: {OidCount} OIDs");
        }
        catch (OperationCanceledException) { StatusText = "Cancelled."; }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; _log.Error("Walk", ex.Message); }
        finally { timer.Stop(); IsBusy = false; Elapsed = $"{sw.Elapsed:mm\\:ss}"; }
    }

    private async Task DoGetAsync()
    {
        IsBusy = true; _cts = new CancellationTokenSource();
        var (r, err) = await _client.GetAsync(BuildTarget(), RootOid, _cts.Token);
        if (r != null)
        {
            Enrich(r);
            Results.Add(r);
            if (MatchesFilter(r)) FilteredResults.Add(r);
            OidCount++;
            StatusText = "GET succeeded.";
        }
        else StatusText = $"GET failed: {err}";
        IsBusy = false;
    }

    private async Task DoNegotiateAsync()
    {
        IsBusy = true; StatusText = "Auto-negotiating…"; _cts = new CancellationTokenSource();
        var report = await _negotiation.NegotiateAsync(Host, Port, ct: _cts.Token);
        foreach (var a in report.AttemptLog) _log.Debug(a);
        if (report.Success)
        {
            if (report.WorkingVersion.HasValue) { Version = report.WorkingVersion.Value; }
            if (report.WorkingCommunity != null) Community = report.WorkingCommunity;
            StatusText = report.FriendlyMessage;
        }
        else StatusText = $"Negotiation failed: {report.FriendlyMessage}";
        IsBusy = false;
    }

    // ── Walk a known SNMP table ────────────────────────────────────────────────
    private async Task DoWalkTableAsync(string tableOid)
    {
        if (string.IsNullOrWhiteSpace(tableOid)) return;
        RootOid = tableOid;
        await DoWalkAsync();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SNMP Table view
    // ══════════════════════════════════════════════════════════════════════════
    private void RebuildTableColumns()
    {
        TableRows.Clear();
        if (SelectedTable == null) return;
        foreach (var row in SelectedTable.Rows) TableRows.Add(row);
        OnPropertyChanged(nameof(SelectedTable));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Result filter
    // ══════════════════════════════════════════════════════════════════════════
    private void ApplyResultFilter()
    {
        FilteredResults.Clear();
        FilterActive = !string.IsNullOrWhiteSpace(ResultFilter);
        foreach (var r in Results.Where(MatchesFilter)) FilteredResults.Add(r);
    }

    private bool MatchesFilter(SnmpResult r)
    {
        if (!FilterActive) return true;
        var f = ResultFilter;
        return r.Oid.Contains(f, StringComparison.OrdinalIgnoreCase)
            || r.SymbolicName.Contains(f, StringComparison.OrdinalIgnoreCase)
            || r.RawValue.Contains(f, StringComparison.OrdinalIgnoreCase)
            || r.HumanValue.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SNMPv3 Profiles
    // ══════════════════════════════════════════════════════════════════════════
    private async Task DoSaveV3ProfileAsync()
    {
        var profile = new V3Profile
        {
            Name                 = V3ProfileName,
            SecurityName         = V3SecurityName,
            AuthProtocol         = V3AuthProto,
            AuthPasswordEncrypted = V3ProfileStore.Encrypt(V3AuthPassword),
            PrivProtocol         = V3PrivProto,
            PrivPasswordEncrypted = V3ProfileStore.Encrypt(V3PrivPassword),
            ContextName          = V3ContextName
        };
        await _v3Store.AddOrUpdateAsync(profile);
        var existing = V3Profiles.FirstOrDefault(p => p.Id == profile.Id);
        if (existing != null) V3Profiles.Remove(existing);
        V3Profiles.Insert(0, profile);
        StatusText = $"V3 profile '{profile.Name}' saved.";
        _log.Info($"V3 profile saved: {profile.Name}");
    }

    private void LoadV3Profile(V3Profile? p)
    {
        if (p == null) return;
        Version        = SnmpVersion.V3;
        V3SecurityName = p.SecurityName;
        V3AuthProto    = p.AuthProtocol;
        V3AuthPassword = V3ProfileStore.Decrypt(p.AuthPasswordEncrypted);
        V3PrivProto    = p.PrivProtocol;
        V3PrivPassword = V3ProfileStore.Decrypt(p.PrivPasswordEncrypted);
        V3ContextName  = p.ContextName;
        StatusText     = $"Loaded V3 profile: {p.Name}";
    }

    private async Task DoDeleteV3ProfileAsync()
    {
        if (SelectedV3Profile == null) return;
        await _v3Store.DeleteAsync(SelectedV3Profile.Id);
        V3Profiles.Remove(SelectedV3Profile);
        SelectedV3Profile = null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Trap Receiver
    // ══════════════════════════════════════════════════════════════════════════
    private void DoStartTraps()
    {
        try
        {
            _trapReceiver.Start(TrapPort);
            TrapListening = true;
            StatusText = $"Trap listener active on UDP {TrapPort}.";
            _log.Info($"Trap receiver started on port {TrapPort}.");
            (StartTrapCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopTrapCommand  as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex) { StatusText = $"Trap listener failed: {ex.Message}"; _log.Error("TrapReceiver", ex.Message); }
    }

    private void DoStopTraps()
    {
        _trapReceiver.Stop();
        TrapListening = false;
        StatusText = "Trap listener stopped.";
        (StartTrapCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (StopTrapCommand  as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnTrapReceived(object? _, TrapEntry trap)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Traps.Insert(0, trap);
            TrapCount = Traps.Count;
            if (MatchesTrapFilter(trap)) FilteredTraps.Insert(0, trap);
        });
    }

    private void ApplyTrapFilter()
    {
        FilteredTraps.Clear();
        foreach (var t in Traps.Where(MatchesTrapFilter)) FilteredTraps.Add(t);
    }

    private bool MatchesTrapFilter(TrapEntry t)
    {
        if (string.IsNullOrWhiteSpace(TrapFilter)) return true;
        var f = TrapFilter;
        return t.SourceIp.Contains(f, StringComparison.OrdinalIgnoreCase)
            || t.TrapName.Contains(f, StringComparison.OrdinalIgnoreCase)
            || t.TrapOid.Contains(f, StringComparison.OrdinalIgnoreCase)
            || t.Community.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Discovery
    // ══════════════════════════════════════════════════════════════════════════
    private async Task DoDiscoveryAsync()
    {
        DiscoveredHosts.Clear(); DiscoveryCount = 0;
        IsDiscovering = true; _cts = new CancellationTokenSource();
        var communities = DiscoveryCommunities.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0);
        StatusText = $"Scanning {DiscoveryCidr}…";
        try
        {
            await foreach (var h in _discovery.DiscoverAsync(DiscoveryCidr, communities, ct: _cts.Token))
                App.Current.Dispatcher.Invoke(() => { DiscoveredHosts.Add(h); DiscoveryCount++; });
            StatusText = $"Discovery done — {DiscoveryCount} hosts found.";
            _log.Info($"Discovery complete: {DiscoveryCount} hosts");
        }
        catch (OperationCanceledException) { StatusText = "Discovery cancelled."; }
        catch (Exception ex) { StatusText = $"Discovery error: {ex.Message}"; }
        finally { IsDiscovering = false; }
    }

    private void ConnectDiscovered(DiscoveredHost? h)
    {
        if (h == null || !h.Reachable) return;
        Host = h.IpAddress; Version = h.BestVersion;
        Community = h.BestCommunity.Length > 0 ? h.BestCommunity : "public";
        StatusText = $"Loaded {h.IpAddress} — click Walk to browse.";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Live Polling + OxyPlot chart
    // ══════════════════════════════════════════════════════════════════════════
    private void AddPollSeries()
    {
        if (string.IsNullOrWhiteSpace(PollOid)) return;
        var (name, _) = _translator.Translate(PollOid);
        var series = new PollSeries
        {
            Oid = PollOid, SymbolicName = name,
            Label = name != PollOid ? name : PollOid,
            IntervalSecs = PollIntervalSecs, IsRunning = true
        };
        PollSeries.Add(series);
        _ = RunPollLoopAsync(series);
        _log.Info($"Poll added: {PollOid} every {PollIntervalSecs}s");
    }

    private void RemovePollSeries()
    {
        if (SelectedPoll == null) return;
        SelectedPoll.IsRunning = false;
        PollSeries.Remove(SelectedPoll);
        if (SelectedPoll == _selectedPoll) { SelectedPoll = null; ChartModel = null; }
    }

    private async Task RunPollLoopAsync(PollSeries series)
    {
        var target = BuildTarget();
        while (series.IsRunning)
        {
            try
            {
                var (r, _) = await _client.GetAsync(target, series.Oid);
                if (r != null)
                {
                    var humanVal = _translator.InterpretValue(r.Oid, r.RawValue, r.Type);
                    double.TryParse(Regex.Replace(humanVal, @"[^\d\.\-]", ""), out var numVal);
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        series.LastValue = humanVal;
                        series.Points.Add(new PollPoint { Timestamp = DateTime.Now, Value = numVal });
                        if (series.Points.Count > 120) series.Points.RemoveAt(0);
                        if (SelectedPoll == series) RebuildChart();
                    });
                }
            }
            catch { /* device offline */ }
            await Task.Delay(series.IntervalSecs * 1000);
        }
    }

    private void RebuildChart()
    {
        if (SelectedPoll == null) { ChartModel = null; return; }

        var model = new PlotModel
        {
            Background       = OxyColor.FromRgb(0x1E, 0x22, 0x35),
            PlotAreaBorderColor = OxyColor.FromRgb(0x2A, 0x2F, 0x45),
            TextColor        = OxyColor.FromRgb(0x8B, 0x92, 0xA8),
        };

        // Axes
        model.Axes.Add(new DateTimeAxis
        {
            Position         = AxisPosition.Bottom,
            StringFormat     = "HH:mm:ss",
            TextColor        = OxyColor.FromRgb(0x8B, 0x92, 0xA8),
            TicklineColor    = OxyColor.FromRgb(0x2A, 0x2F, 0x45),
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromArgb(40, 0x4F, 0x8E, 0xF7),
        });
        model.Axes.Add(new LinearAxis
        {
            Position         = AxisPosition.Left,
            TextColor        = OxyColor.FromRgb(0x8B, 0x92, 0xA8),
            TicklineColor    = OxyColor.FromRgb(0x2A, 0x2F, 0x45),
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromArgb(40, 0x4F, 0x8E, 0xF7),
        });

        // Line series
        var line = new LineSeries
        {
            Title            = SelectedPoll.Label,
            Color            = OxyColor.FromRgb(0x4F, 0x8E, 0xF7),
            StrokeThickness  = 2,
            MarkerType       = MarkerType.Circle,
            MarkerSize       = 3,
            MarkerFill       = OxyColor.FromRgb(0x4F, 0x8E, 0xF7),
        };

        foreach (var pt in SelectedPoll.Points)
            line.Points.Add(new DataPoint(DateTimeAxis.ToDouble(pt.Timestamp), pt.Value));

        model.Series.Add(line);
        model.Title = SelectedPoll.Label;
        model.TitleColor = OxyColor.FromRgb(0xF1, 0xF3, 0xF9);

        ChartModel = model;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Saved Targets
    // ══════════════════════════════════════════════════════════════════════════
    private async Task DoSaveTargetAsync()
    {
        var label = string.IsNullOrWhiteSpace(SaveTargetLabel) ? Host : SaveTargetLabel;
        var t = new SavedTarget { Label = label, Host = Host, Port = Port,
            Version = Version, Community = Community, LastUsed = DateTime.Now };
        await _targetStore.AddOrUpdateAsync(t);
        var existing = SavedTargets.FirstOrDefault(s => s.Id == t.Id);
        if (existing != null) SavedTargets.Remove(existing);
        SavedTargets.Insert(0, t);
        StatusText = $"Target '{label}' saved.";
    }

    private void LoadTarget(SavedTarget? t)
    {
        if (t == null) return;
        Host = t.Host; Port = t.Port; Version = t.Version; Community = t.Community;
        StatusText = $"Loaded target: {t.Label}";
    }

    private async Task DoDeleteTargetAsync()
    {
        if (SelectedSavedTarget == null) return;
        await _targetStore.DeleteAsync(SelectedSavedTarget.Id);
        SavedTargets.Remove(SelectedSavedTarget);
        SelectedSavedTarget = null;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Favourites / Copy
    // ══════════════════════════════════════════════════════════════════════════
    private void AddFavourite()
    {
        if (SelectedResult == null || FavouriteOids.Any(f => f.Oid == SelectedResult.Oid)) return;
        FavouriteOids.Add(SelectedResult);
    }

    private static void CopyRow(SnmpResult? r)
    {
        if (r == null) return;
        Clipboard.SetText($"{r.Oid}\t{r.SymbolicName}\t{r.RawValue}\t{r.HumanValue}\t{r.Type}");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MIB Manager
    // ══════════════════════════════════════════════════════════════════════════
    public async Task HandleDroppedFilesAsync(IEnumerable<string> paths)
    { IsDragOver = false; await ImportFilesAsync(paths); }
    public void SetDragOver(bool over) => IsDragOver = over;

    private async Task DoImportMibAsync()
    {
        var dlg = new OpenFileDialog { Title = "Import MIB Files", Multiselect = true,
            Filter = "MIB files|*.mib;*.txt;*.my|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        await ImportFilesAsync(dlg.FileNames);
    }

    private async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        StatusText = $"Importing {list.Count} MIB file(s)…";
        var (loaded, failed, errors) = await _mib.ImportAsync(list);
        RefreshMibModules(); RefreshMibTree();
        MibCount = _mib.Count;
        StatusText = loaded > 0 ? $"MIB import done — {loaded} nodes, {_mib.Count} total." : "No nodes parsed.";
        foreach (var e in errors) _log.Warn($"MIB parse: {e}");
    }

    private async Task DoRemoveModuleAsync()
    {
        if (SelectedModule == null) return;
        var name = SelectedModule.ModuleName;
        if (MessageBox.Show($"Remove module '{name}'?", "Remove Module",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await _mib.RemoveModuleAsync(name);
        RefreshMibModules(); RefreshMibTree(); MibCount = _mib.Count; SelectedModule = null;
    }

    private async Task DoClearMibCacheAsync()
    {
        if (MessageBox.Show("Clear ALL MIB modules and cache?", "Clear Cache",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await _mib.ClearAllAsync();
        RefreshMibModules(); RefreshMibTree(); MibCount = 0;
    }

    private void RefreshMibModules()
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            MibModules.Clear();
            foreach (var m in _mib.LoadedModules) MibModules.Add(m);
        });
    }

    private void RefreshMibTree()
    {
        _allTreeNodes = _mib.AllNodes.OrderBy(n => n.NumericOid).ToList();
        App.Current.Dispatcher.Invoke(ApplyTreeFilter);
    }

    private void ApplyTreeFilter()
    {
        FilteredTree.Clear();
        var src = string.IsNullOrWhiteSpace(TreeFilter)
            ? _allTreeNodes
            : _allTreeNodes.Where(n =>
                n.Name.Contains(TreeFilter, StringComparison.OrdinalIgnoreCase) ||
                n.NumericOid.Contains(TreeFilter));
        foreach (var n in src.Take(500)) FilteredTree.Add(n);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Exports / SET / Printer
    // ══════════════════════════════════════════════════════════════════════════
    private async Task DoExportCsvAsync()
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "snmp_results.csv" };
        if (dlg.ShowDialog() != true) return;
        await _export.ExportCsvAsync(Results, dlg.FileName);
        StatusText = $"Exported: {dlg.FileName}";
    }

    private async Task DoExportJsonAsync()
    {
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "snmp_results.json" };
        if (dlg.ShowDialog() != true) return;
        await _export.ExportJsonAsync(Results, dlg.FileName);
        StatusText = $"Exported: {dlg.FileName}";
    }

    private async Task DoExportBundleAsync()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Select output folder" };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var path = await _export.ExportDiagnosticsBundleAsync(Results, _log.Entries, BuildTarget(), dlg.SelectedPath);
        StatusText = $"Bundle: {path}";
    }

    private async Task DoSetAsync()
    {
        if (!WriteModeEnabled || string.IsNullOrWhiteSpace(SetOid)) return;
        IsBusy = true; _cts = new CancellationTokenSource();
        var (ok, err) = await _client.SetAsync(BuildTarget(), SetOid, SetType, SetValue, _cts.Token);
        StatusText = ok ? $"SET ok: {SetOid}" : $"SET failed: {err}";
        IsBusy = false;
    }

    private async Task DoPrinterQuickViewAsync()
    {
        Results.Clear(); FilteredResults.Clear(); OidCount = 0;
        IsBusy = true; _cts = new CancellationTokenSource();
        var oids = new[] {
            "1.3.6.1.2.1.1.5.0","1.3.6.1.2.1.1.1.0","1.3.6.1.2.1.43.5.1.1.16.1",
            "1.3.6.1.2.1.43.5.1.1.15.1","1.3.6.1.2.1.25.3.2.1.5.1",
            "1.3.6.1.2.1.43.11.1.1.8.1.1","1.3.6.1.2.1.43.11.1.1.9.1.1",
            "1.3.6.1.2.1.43.11.1.1.8.1.2","1.3.6.1.2.1.43.11.1.1.9.1.2",
            "1.3.6.1.2.1.43.11.1.1.8.1.3","1.3.6.1.2.1.43.11.1.1.9.1.3",
            "1.3.6.1.2.1.43.11.1.1.8.1.4","1.3.6.1.2.1.43.11.1.1.9.1.4" };
        var target = BuildTarget();
        foreach (var oid in oids)
        {
            if (_cts.Token.IsCancellationRequested) break;
            var (r, _) = await _client.GetAsync(target, oid, _cts.Token);
            if (r != null) { Enrich(r); App.Current.Dispatcher.Invoke(() => { Results.Add(r); if (MatchesFilter(r)) FilteredResults.Add(r); OidCount++; }); }
        }
        StatusText = $"Printer view — {OidCount} OIDs.";
        IsBusy = false;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════
    private void CancelAll() => _cts?.Cancel();

    private SnmpTarget BuildTarget()
    {
        var t = new SnmpTarget
        {
            Host = Host, Port = Port, Version = Version,
            Community = Community, WriteCommunity = Community,
            SecurityName = V3SecurityName,
            AuthProtocol = V3AuthProto,
            PrivProtocol = V3PrivProto,
            ContextName  = V3ContextName
        };
        if (!string.IsNullOrEmpty(V3AuthPassword))
        {
            var sec = new System.Security.SecureString();
            foreach (var c in V3AuthPassword) sec.AppendChar(c);
            t.AuthPassword = sec;
        }
        if (!string.IsNullOrEmpty(V3PrivPassword))
        {
            var sec = new System.Security.SecureString();
            foreach (var c in V3PrivPassword) sec.AppendChar(c);
            t.PrivPassword = sec;
        }
        return t;
    }

    private void Enrich(SnmpResult r)
    {
        var (name, desc) = _translator.Translate(r.Oid);
        r.SymbolicName = name;
        r.Description  = desc;
        r.HumanValue   = _translator.InterpretValue(r.Oid, r.RawValue, r.Type);
    }

    private void RaiseCommandsChanged()
    {
        foreach (var c in new ICommand[] { WalkCommand, GetCommand, CancelCommand,
            NegotiateCommand, SetCommand, PrinterQuickViewCommand,
            StartDiscoveryCommand, CancelDiscoveryCommand })
            (c as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
    { if (Equals(f, v)) return; f = v; OnPropertyChanged(n); }
    private void OnPropertyChanged(string? n) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
