using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SNMP.Core.Enums;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;
using SNMP.Engine.Services;

namespace SNMP.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    // ── Services ─────────────────────────────────────────────────────────────
    private readonly ISnmpClient         _client;
    private readonly INegotiationService _negotiation;
    private readonly IMibRepository      _mib;
    private readonly IOidTranslator      _translator;
    private readonly ILogService         _log;
    private readonly IExportService      _export;

    private CancellationTokenSource? _cts;

    // ── Bindable collections ──────────────────────────────────────────────────
    public ObservableCollection<SnmpResult>    Results      { get; } = new();
    public ObservableCollection<LogEntry>      LogEntries   { get; } = new();
    public ObservableCollection<MibModuleInfo> MibModules   { get; } = new();

    // ── Connection ────────────────────────────────────────────────────────────
    private string _host = string.Empty;
    public string Host { get => _host; set => Set(ref _host, value); }

    private int _port = 161;
    public int Port { get => _port; set => Set(ref _port, value); }

    private SnmpVersion _version = SnmpVersion.V2c;
    public SnmpVersion Version { get => _version; set => Set(ref _version, value); }
    public SnmpVersion[] Versions { get; } = Enum.GetValues<SnmpVersion>();

    private string _community = "public";
    public string Community { get => _community; set => Set(ref _community, value); }

    private string _rootOid = "1.3.6.1.2.1";
    public string RootOid { get => _rootOid; set => Set(ref _rootOid, value); }

    // ── Status ────────────────────────────────────────────────────────────────
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

    // ── MIB Manager ───────────────────────────────────────────────────────────
    private MibModuleInfo? _selectedModule;
    public MibModuleInfo? SelectedModule
    {
        get => _selectedModule;
        set { Set(ref _selectedModule, value); (RemoveModuleCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    private string _mibDropHint = "Drag & drop MIB files here, or click Import MIB";
    public string MibDropHint { get => _mibDropHint; set => Set(ref _mibDropHint, value); }

    private bool _isDragOver;
    public bool IsDragOver { get => _isDragOver; set => Set(ref _isDragOver, value); }

    // ── Write mode ────────────────────────────────────────────────────────────
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

    // SET fields
    private string _setOid   = string.Empty;
    public string SetOid   { get => _setOid;   set => Set(ref _setOid, value); }
    private string _setType  = "INTEGER";
    public string SetType  { get => _setType;  set => Set(ref _setType, value); }
    private string _setValue = string.Empty;
    public string SetValue { get => _setValue; set => Set(ref _setValue, value); }

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand WalkCommand             { get; }
    public ICommand GetCommand              { get; }
    public ICommand CancelCommand           { get; }
    public ICommand NegotiateCommand        { get; }
    public ICommand ImportMibCommand        { get; }
    public ICommand RemoveModuleCommand     { get; }
    public ICommand ClearMibCacheCommand    { get; }
    public ICommand ExportCsvCommand        { get; }
    public ICommand ExportJsonCommand       { get; }
    public ICommand ExportBundleCommand     { get; }
    public ICommand SetCommand              { get; }
    public ICommand ClearLogCommand         { get; }
    public ICommand PrinterQuickViewCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainViewModel(ISnmpClient client, INegotiationService negotiation,
        IMibRepository mib, IOidTranslator translator, ILogService log, IExportService export)
    {
        _client      = client;
        _negotiation = negotiation;
        _mib         = mib;
        _translator  = translator;
        _log         = log;
        _export      = export;

        _log.EntryAdded += entry => App.Current.Dispatcher.Invoke(() => LogEntries.Add(entry));

        WalkCommand             = new RelayCommand(async () => await DoWalkAsync(),              () => !IsBusy);
        GetCommand              = new RelayCommand(async () => await DoGetAsync(),               () => !IsBusy);
        CancelCommand           = new RelayCommand(() => _cts?.Cancel(),                         () => IsBusy);
        NegotiateCommand        = new RelayCommand(async () => await DoNegotiateAsync(),         () => !IsBusy);
        ImportMibCommand        = new RelayCommand(async () => await DoImportMibAsync());
        RemoveModuleCommand     = new RelayCommand(async () => await DoRemoveModuleAsync(),      () => SelectedModule != null);
        ClearMibCacheCommand    = new RelayCommand(async () => await DoClearMibCacheAsync());
        ExportCsvCommand        = new RelayCommand(async () => await DoExportCsvAsync(),         () => Results.Count > 0);
        ExportJsonCommand       = new RelayCommand(async () => await DoExportJsonAsync(),        () => Results.Count > 0);
        ExportBundleCommand     = new RelayCommand(async () => await DoExportBundleAsync());
        SetCommand              = new RelayCommand(async () => await DoSetAsync(),               () => WriteModeEnabled && !IsBusy);
        ClearLogCommand         = new RelayCommand(() => { _log.Clear(); LogEntries.Clear(); });
        PrinterQuickViewCommand = new RelayCommand(async () => await DoPrinterQuickViewAsync(),  () => !IsBusy);

        // Load cache on startup (fire-and-forget — UI will update via MibCount)
        _ = LoadCacheOnStartupAsync();
    }

    // ── Startup cache load ────────────────────────────────────────────────────
    private async Task LoadCacheOnStartupAsync()
    {
        StatusText = "Loading MIB cache…";
        var count = await _mib.LoadCacheAsync();
        RefreshMibModules();
        MibCount = _mib.Count;
        if (count > 0)
        {
            StatusText = $"MIB cache restored — {count} nodes across {MibModules.Count} modules. Ready.";
            _log.Info($"MIB cache loaded: {count} nodes, {MibModules.Count} modules.");
        }
        else
        {
            StatusText = "Ready. No MIB cache found — import MIB files to enable OID translation.";
        }
    }

    // ── Walk ─────────────────────────────────────────────────────────────────
    private async Task DoWalkAsync()
    {
        Results.Clear(); OidCount = 0;
        IsBusy = true;
        _cts   = new CancellationTokenSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (_, _) => Elapsed = $"{sw.Elapsed:mm\\:ss}";
        timer.Start();

        StatusText = $"Walking {RootOid}…";
        _log.Info($"WALK started: {Host}:{Port} v={Version} oid={RootOid}");

        try
        {
            var target = BuildTarget();
            await foreach (var r in _client.WalkAsync(target, RootOid, _cts.Token))
            {
                Enrich(r);
                App.Current.Dispatcher.Invoke(() => { Results.Add(r); OidCount++; });
            }
            StatusText = $"Walk complete — {OidCount} OIDs in {sw.Elapsed:mm\\:ss}.";
            _log.Info($"WALK done: {OidCount} OIDs");
        }
        catch (OperationCanceledException) { StatusText = "Walk cancelled."; }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; _log.Error("Walk error", ex.Message); }
        finally { timer.Stop(); IsBusy = false; Elapsed = $"{sw.Elapsed:mm\\:ss}"; }
    }

    // ── GET ──────────────────────────────────────────────────────────────────
    private async Task DoGetAsync()
    {
        IsBusy = true;
        _cts   = new CancellationTokenSource();
        var (result, error) = await _client.GetAsync(BuildTarget(), RootOid, _cts.Token);
        if (result != null) { Enrich(result); Results.Add(result); OidCount++; StatusText = "GET succeeded."; }
        else StatusText = $"GET failed: {error}";
        IsBusy = false;
    }

    // ── Negotiate ────────────────────────────────────────────────────────────
    private async Task DoNegotiateAsync()
    {
        IsBusy = true; StatusText = "Auto-negotiating…";
        _cts   = new CancellationTokenSource();
        var report = await _negotiation.NegotiateAsync(Host, Port, ct: _cts.Token);
        foreach (var a in report.AttemptLog) _log.Debug(a);

        if (report.Success)
        {
            StatusText = report.FriendlyMessage;
            if (report.WorkingVersion.HasValue) Version = report.WorkingVersion.Value;
            if (report.WorkingCommunity != null) Community = report.WorkingCommunity;
            _log.Info($"Negotiation: {report.FriendlyMessage}");
        }
        else
        {
            StatusText = $"Negotiation failed: {report.FriendlyMessage}";
            _log.Warn($"Negotiation failed: {report.FriendlyMessage}");
        }
        IsBusy = false;
    }

    // ── Import MIB (file picker) ──────────────────────────────────────────────
    private async Task DoImportMibAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title       = "Import MIB Files",
            Multiselect = true,
            Filter      = "MIB files|*.mib;*.txt;*.my|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        await ImportFilesAsync(dlg.FileNames);
    }

    // ── Import MIB (drag-drop) — called from code-behind ─────────────────────
    public async Task HandleDroppedFilesAsync(IEnumerable<string> paths)
    {
        IsDragOver = false;
        await ImportFilesAsync(paths);
    }

    public void SetDragOver(bool over) => IsDragOver = over;

    private async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        var pathList = paths.ToList();
        StatusText = $"Importing {pathList.Count} MIB file(s)…";
        _log.Info($"MIB import started: {pathList.Count} file(s)");

        var (loaded, failed, errors) = await _mib.ImportAsync(pathList);

        RefreshMibModules();
        MibCount = _mib.Count;

        StatusText = loaded > 0
            ? $"MIB import done — {loaded} nodes added, {_mib.Count} total. {(failed > 0 ? $"{failed} file(s) failed." : "")}"
            : $"MIB import: no nodes parsed. {(failed > 0 ? $"{failed} file(s) failed." : "Check file format.")}";

        foreach (var e in errors) _log.Warn($"MIB parse: {e}");
        _log.Info($"MIB import complete: {loaded} nodes from {pathList.Count} file(s), cache updated.");
    }

    // ── Remove module ─────────────────────────────────────────────────────────
    private async Task DoRemoveModuleAsync()
    {
        if (SelectedModule == null) return;
        var name = SelectedModule.ModuleName;

        if (MessageBox.Show($"Remove module '{name}' and all its OID mappings?",
                "Remove Module", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK) return;

        await _mib.RemoveModuleAsync(name);
        RefreshMibModules();
        MibCount = _mib.Count;
        SelectedModule = null;
        StatusText = $"Module '{name}' removed. {_mib.Count} nodes remaining.";
        _log.Info($"MIB module removed: {name}");
    }

    // ── Clear all MIB cache ───────────────────────────────────────────────────
    private async Task DoClearMibCacheAsync()
    {
        if (MessageBox.Show("This will clear ALL loaded MIB modules and delete the cache.\nYou will need to re-import MIB files.",
                "Clear MIB Cache?", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            != MessageBoxResult.OK) return;

        await _mib.ClearAllAsync();
        RefreshMibModules();
        MibCount = 0;
        StatusText = "MIB cache cleared.";
        _log.Info("MIB cache cleared by user.");
    }

    // ── Exports ───────────────────────────────────────────────────────────────
    private async Task DoExportCsvAsync()
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "snmp_results.csv" };
        if (dlg.ShowDialog() != true) return;
        await _export.ExportCsvAsync(Results, dlg.FileName);
        _log.Info($"Exported CSV: {dlg.FileName}");
        StatusText = $"Exported to {dlg.FileName}";
    }

    private async Task DoExportJsonAsync()
    {
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "snmp_results.json" };
        if (dlg.ShowDialog() != true) return;
        await _export.ExportJsonAsync(Results, dlg.FileName);
        _log.Info($"Exported JSON: {dlg.FileName}");
        StatusText = $"Exported to {dlg.FileName}";
    }

    private async Task DoExportBundleAsync()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
            { Description = "Select output folder for diagnostics bundle" };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var path = await _export.ExportDiagnosticsBundleAsync(
            Results, _log.Entries, BuildTarget(), dlg.SelectedPath);
        _log.Info($"Diagnostics bundle: {path}");
        StatusText = $"Bundle saved to: {path}";
    }

    // ── SET ───────────────────────────────────────────────────────────────────
    private async Task DoSetAsync()
    {
        if (!WriteModeEnabled) return;
        if (string.IsNullOrWhiteSpace(SetOid) || string.IsNullOrWhiteSpace(SetValue))
        { StatusText = "SET requires OID and value."; return; }

        IsBusy = true;
        _cts   = new CancellationTokenSource();
        var (ok, err) = await _client.SetAsync(BuildTarget(), SetOid, SetType, SetValue, _cts.Token);
        StatusText = ok ? $"SET succeeded: {SetOid}" : $"SET failed: {err}";
        IsBusy = false;
    }

    // ── Printer Quick View ────────────────────────────────────────────────────
    private async Task DoPrinterQuickViewAsync()
    {
        Results.Clear(); OidCount = 0;
        IsBusy = true;
        _cts   = new CancellationTokenSource();
        StatusText = "Printer Quick View…";

        var printerOids = new[]
        {
            "1.3.6.1.2.1.1.5.0",            // sysName
            "1.3.6.1.2.1.1.1.0",            // sysDescr
            "1.3.6.1.2.1.43.5.1.1.16.1",    // Serial number
            "1.3.6.1.2.1.43.5.1.1.15.1",    // Printer name
            "1.3.6.1.2.1.25.3.2.1.5.1",     // hrDeviceStatus
            "1.3.6.1.2.1.43.11.1.1.8.1.1",  // Black toner level
            "1.3.6.1.2.1.43.11.1.1.9.1.1",  // Black toner max
            "1.3.6.1.2.1.43.11.1.1.8.1.2",  // Cyan toner level
            "1.3.6.1.2.1.43.11.1.1.9.1.2",  // Cyan toner max
            "1.3.6.1.2.1.43.11.1.1.8.1.3",  // Magenta toner level
            "1.3.6.1.2.1.43.11.1.1.9.1.3",  // Magenta toner max
            "1.3.6.1.2.1.43.11.1.1.8.1.4",  // Yellow toner level
            "1.3.6.1.2.1.43.11.1.1.9.1.4",  // Yellow toner max
        };

        var target = BuildTarget();
        foreach (var oid in printerOids)
        {
            if (_cts.Token.IsCancellationRequested) break;
            var (r, _) = await _client.GetAsync(target, oid, _cts.Token);
            if (r != null)
            {
                Enrich(r);
                App.Current.Dispatcher.Invoke(() => { Results.Add(r); OidCount++; });
            }
        }

        StatusText = $"Printer Quick View done — {OidCount} OIDs.";
        IsBusy = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshMibModules()
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            MibModules.Clear();
            foreach (var m in _mib.LoadedModules)
                MibModules.Add(m);
        });
    }

    private SnmpTarget BuildTarget() => new()
    {
        Host           = Host,
        Port           = Port,
        Version        = Version,
        Community      = Community,
        WriteCommunity = Community
    };

    private void Enrich(SnmpResult r)
    {
        var (name, desc) = _translator.Translate(r.Oid);
        r.SymbolicName = name;
        r.Description  = desc;
        r.HumanValue   = _translator.InterpretValue(r.Oid, r.RawValue, r.Type);
    }

    private void RaiseCommandsChanged()
    {
        (WalkCommand      as RelayCommand)?.RaiseCanExecuteChanged();
        (GetCommand       as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand    as RelayCommand)?.RaiseCanExecuteChanged();
        (NegotiateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SetCommand       as RelayCommand)?.RaiseCanExecuteChanged();
        (PrinterQuickViewCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
