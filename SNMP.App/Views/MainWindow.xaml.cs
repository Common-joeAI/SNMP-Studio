using System.Windows;
using SNMP.App.ViewModels;

namespace SNMP.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
