using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SNMP.App.ViewModels;
using SNMP.App.Views;
using SNMP.Core.Interfaces;
using SNMP.Engine.Mib;
using SNMP.Engine.Services;

namespace SNMP.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();

        // Core services
        sc.AddSingleton<ILogService,         LogService>();
        sc.AddSingleton<IMibRepository,      MibRepository>();
        sc.AddSingleton<IOidTranslator,      OidTranslator>();
        sc.AddSingleton<ISnmpClient,         SnmpClient>();
        sc.AddSingleton<INegotiationService, NegotiationService>();
        sc.AddSingleton<IExportService,      ExportService>();

        // ViewModel + View
        sc.AddTransient<MainViewModel>();
        sc.AddTransient<MainWindow>();

        _services = sc.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
