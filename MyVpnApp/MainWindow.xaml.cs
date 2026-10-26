using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace MyVpnApp;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        PasswordInput.Password = _viewModel.Password;
        PskInput.Password = _viewModel.L2tpPsk;
        UpdateL2tpVisibility();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Protocol)) Dispatcher.Invoke(UpdateL2tpVisibility);
    }

    private void UpdateL2tpVisibility() => L2tpPanel.Visibility = _viewModel.Protocol == MyVpnApp.Core.VpnProtocol.L2tp ? Visibility.Visible : Visibility.Collapsed;
    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) => _viewModel.Password = PasswordInput.Password;
    private void PskInput_PasswordChanged(object sender, RoutedEventArgs e) => _viewModel.L2tpPsk = PskInput.Password;

    private void RouteGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Column is not DataGridCheckBoxColumn || e.Row.Item is not RouteRow row) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(async () => await _viewModel.SaveRouteChangesAsync(row)));
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        IsEnabled = false;
        await _viewModel.ShutdownAsync();
        _closing = true;
        Close();
    }
}
