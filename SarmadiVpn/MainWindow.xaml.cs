using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SarmadiVpn;

public partial class MainWindow : Window
{
    private static readonly uint TaskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");
    private readonly MainViewModel _viewModel = new();
    private TaskbarStatusOverlay? _taskbarOverlay;
    private bool _taskbarButtonReady;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        SourceInitialized += Window_SourceInitialized;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
            PasswordInput.Password = _viewModel.Password;
            PskInput.Password = _viewModel.L2tpPsk;
            UpdateL2tpVisibility();
            UpdateTaskbarStatusIcon();
        }
        catch (Exception ex)
        {
            MessageBox.Show("SARMADI VPN could not finish startup. " + ex.Message, "Startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Protocol)) Dispatcher.Invoke(UpdateL2tpVisibility);
        if (e.PropertyName == nameof(MainViewModel.State))
        {
            if (Dispatcher.CheckAccess()) UpdateTaskbarStatusIcon();
            else Dispatcher.BeginInvoke(UpdateTaskbarStatusIcon);
        }
    }

    private void UpdateTaskbarStatusIcon()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !_taskbarButtonReady) return;

        try
        {
            _taskbarOverlay ??= new TaskbarStatusOverlay();
            _taskbarOverlay.Update(handle, _viewModel.State);
        }
        catch (Exception ex) { _viewModel.Write("Could not update the taskbar VPN status icon: " + ex.Message); }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == TaskbarButtonCreatedMessage)
        {
            _taskbarButtonReady = true;
            UpdateTaskbarStatusIcon();
        }

        return IntPtr.Zero;
    }

    private void UpdateL2tpVisibility() => L2tpPanel.Visibility = _viewModel.Protocol == SarmadiVpn.Core.VpnProtocol.L2tp ? Visibility.Visible : Visibility.Collapsed;
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
        try
        {
            await _viewModel.ShutdownAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("VPN shutdown cleanup did not complete. " + ex.Message, "Shutdown cleanup failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            try { if (_taskbarButtonReady) _taskbarOverlay?.Clear(new WindowInteropHelper(this).Handle); }
            catch (Exception ex) { _viewModel.Write("Could not clear the taskbar status overlay: " + ex.Message); }
            _taskbarOverlay?.Dispose();
            _closing = true;
            Close();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);
}
