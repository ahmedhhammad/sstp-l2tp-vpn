using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SarmadiVpn;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private ImageSource? _brandIcon;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        _brandIcon = Icon;
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateTaskbarStatusIcon();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
            PasswordInput.Password = _viewModel.Password;
            PskInput.Password = _viewModel.L2tpPsk;
            UpdateL2tpVisibility();
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
        if (_brandIcon is null) return;

        var color = _viewModel.State switch
        {
            SarmadiVpn.Core.VpnState.Connected => Color.FromRgb(34, 197, 94),
            SarmadiVpn.Core.VpnState.Connecting or SarmadiVpn.Core.VpnState.Disconnecting => Color.FromRgb(240, 180, 20),
            _ => Color.FromRgb(220, 53, 69)
        };

        const int size = 32;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(_brandIcon, new Rect(0, 0, size, size));
            var center = new Point(6.5, 6.5);
            drawing.DrawEllipse(Brushes.White, null, center, 6.5, 6.5);
            drawing.DrawEllipse(new SolidColorBrush(color), null, center, 5, 5);
        }

        var icon = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        icon.Render(visual);
        icon.Freeze();
        Icon = icon;
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
            _closing = true;
            Close();
        }
    }
}
