using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using MyVpnApp.Core;

namespace MyVpnApp;

public sealed class RouteRow(RouteEntry entry) : INotifyPropertyChanged
{
    private bool _enabled = entry.Enabled;
    private string _status = "Not applied";
    public Guid Id { get; } = entry.Id;
    public string Destination { get; } = entry.Destination;
    public string Label { get; } = entry.Label;
    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; Changed(); } }
    public string Status { get => _status; set { if (_status == value) return; _status = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public RouteEntry ToEntry() => new(Id, Destination, Label, Enabled);
}

public sealed class MainViewModel : INotifyPropertyChanged, IAppLogger
{
    private readonly string _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyVpnApp");
    private readonly CredentialProtector _protector = new();
    private readonly VpnService _vpn;
    private readonly RouteManager _routeManager;
    private readonly RouteStore _routeStore;
    private readonly SettingsStore _settingsStore;
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private readonly CancellationTokenSource _pollCancellation = new();
    private bool _wasConnected;
    private string _stateText = "Disconnected";
    private VpnState _state = VpnState.Disconnected;
    private string _server = "";
    private string _username = "";
    private string _password = "";
    private string _l2tpPsk = "";
    private string _newDestination = "";
    private string _newLabel = "";
    private bool _rememberCredentials;
    private VpnProtocol _protocol;
    private RouteRow? _selectedRoute;

    public ObservableCollection<RouteRow> Routes { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public AsyncCommand ConnectCommand { get; }
    public AsyncCommand DisconnectCommand { get; }
    public AsyncCommand AddRouteCommand { get; }
    public AsyncCommand DeleteRouteCommand { get; }
    public AsyncCommand NatCommand { get; }
    public VpnProtocol[] Protocols { get; } = [VpnProtocol.Sstp, VpnProtocol.L2tp];
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        Directory.CreateDirectory(_dataDirectory);
        _routeStore = new RouteStore(Path.Combine(_dataDirectory, "routes.json"), this);
        _settingsStore = new SettingsStore(Path.Combine(_dataDirectory, "settings.json"), this);
        var runner = new ProcessRunner(this);
        var gatewayResolver = new GatewayResolver(runner, this);
        _vpn = new VpnService(runner, this);
        _routeManager = new RouteManager(runner, gatewayResolver, Path.Combine(_dataDirectory, "applied-routes.json"), this);
        ConnectCommand = new AsyncCommand(ConnectAsync, () => State is VpnState.Disconnected or VpnState.Error);
        DisconnectCommand = new AsyncCommand(DisconnectAsync, () => State is VpnState.Connected or VpnState.Connecting);
        AddRouteCommand = new AsyncCommand(AddRouteAsync);
        DeleteRouteCommand = new AsyncCommand(DeleteRouteAsync, () => SelectedRoute is not null);
        NatCommand = new AsyncCommand(EnableNatTraversalAsync);
    }

    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }
    public VpnState State { get => _state; private set { if (Set(ref _state, value)) { OnPropertyChanged(nameof(CanEditConnection)); ConnectCommand.Refresh(); DisconnectCommand.Refresh(); } } }
    public bool CanEditConnection => State is VpnState.Disconnected or VpnState.Error;
    public string Server { get => _server; set => Set(ref _server, value); }
    public string Username { get => _username; set => Set(ref _username, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public string L2tpPsk { get => _l2tpPsk; set => Set(ref _l2tpPsk, value); }
    public string NewDestination { get => _newDestination; set => Set(ref _newDestination, value); }
    public string NewLabel { get => _newLabel; set => Set(ref _newLabel, value); }
    public bool RememberCredentials { get => _rememberCredentials; set => Set(ref _rememberCredentials, value); }
    public VpnProtocol Protocol { get => _protocol; set => Set(ref _protocol, value); }
    public RouteRow? SelectedRoute { get => _selectedRoute; set { if (Set(ref _selectedRoute, value)) DeleteRouteCommand.Refresh(); } }

    public async Task InitializeAsync()
    {
        await _routeManager.CleanupStaleAsync();
        foreach (var entry in await _routeStore.LoadAsync()) Routes.Add(new RouteRow(entry));
        var settings = await _settingsStore.LoadAsync();
        Server = settings.Server; Username = settings.Username; Protocol = settings.Protocol; RememberCredentials = settings.RememberCredentials;
        if (RememberCredentials && !string.IsNullOrWhiteSpace(settings.ProtectedPassword))
        {
            try { Password = _protector.Unprotect(settings.ProtectedPassword); }
            catch (Exception ex) { Write("Saved password could not be decrypted for this Windows user. " + ex.Message); }
        }
        if (!string.IsNullOrWhiteSpace(settings.ProtectedL2tpPsk))
        {
            try { L2tpPsk = _protector.Unprotect(settings.ProtectedL2tpPsk); }
            catch (Exception ex) { Write("Saved L2TP key could not be decrypted for this Windows user. " + ex.Message); }
        }
        _wasConnected = await _vpn.IsConnectedAsync();
        State = _wasConnected ? VpnState.Connected : VpnState.Disconnected;
        StateText = _wasConnected ? "Connected" : "Disconnected";
        if (_wasConnected) await ApplyRoutesAsync();
        _ = PollConnectionAsync(_pollCancellation.Token);
    }

    public async Task ShutdownAsync()
    {
        _pollCancellation.Cancel();
        await SaveSettingsAsync();
        if (_wasConnected || State == VpnState.Connected)
        {
            await _vpn.DisconnectAsync();
            await _routeManager.CleanupAllAsync();
        }
    }

    private async Task ConnectAsync()
    {
        State = VpnState.Connecting; StateText = "Connecting";
        try
        {
            await SaveSettingsAsync();
            var result = await _vpn.ConnectAsync(CurrentSettings(), Password, L2tpPsk);
            if (!result.Succeeded)
            {
                State = VpnState.Error; StateText = "Error"; Write(result.Message);
                MessageBox.Show(result.Message, "VPN connection failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _wasConnected = true; State = VpnState.Connected; StateText = "Connected";
            await ApplyRoutesAsync();
        }
        catch (Exception ex)
        {
            State = VpnState.Error; StateText = "Error"; Write(ex.ToString());
            MessageBox.Show(ex.Message, "VPN connection failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            var result = await _vpn.DisconnectAsync();
            Write(result.Succeeded ? "VPN disconnected." : $"rasdial disconnect returned {result.ExitCode}: {result.CombinedOutput}");
            if (!result.Succeeded && await _vpn.IsConnectedAsync())
            {
                Write("Windows still reports the VPN connected; keeping its routes until the connection is down.");
                return;
            }
            await _routeManager.CleanupAllAsync();
            _wasConnected = false; State = VpnState.Disconnected; StateText = "Disconnected";
            foreach (var route in Routes) route.Status = "Not applied";
        }
        catch (Exception ex) { Write("Disconnect failed: " + ex.Message); }
    }

    private async Task ApplyRoutesAsync()
    {
        var results = await _routeManager.ApplyEnabledAsync(Routes.Select(r => r.ToEntry()));
        foreach (var result in results)
        {
            var row = Routes.FirstOrDefault(r => r.Destination == result.Destination);
            if (row is not null) row.Status = result.Applied ? "Applied" : "Failed: " + result.Message;
            Write($"Route {result.Destination}: {(result.Applied ? "Applied" : "Failed")}. {result.Message}");
        }
    }

    private async Task AddRouteAsync()
    {
        try
        {
            var network = CidrHelper.Parse(NewDestination);
            if (network.PrefixLength == 0) throw new InvalidOperationException("0.0.0.0/0 would send all traffic through the VPN. Choose a narrower destination.");
            var entries = await _routeStore.AddAsync(new RouteEntry(Guid.NewGuid(), network.Cidr, NewLabel.Trim(), true));
            Routes.Clear(); foreach (var route in entries) Routes.Add(new RouteRow(route));
            NewDestination = ""; NewLabel = "";
            if (_wasConnected) await ApplyRoutesAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not add route", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    public async Task SaveRouteChangesAsync(RouteRow row)
    {
        try
        {
            await _routeStore.SaveAsync(Routes.Select(r => r.ToEntry()));
            if (!_wasConnected) { if (!row.Enabled) row.Status = "Not applied"; return; }
            if (row.Enabled)
            {
                var result = (await _routeManager.ApplyEnabledAsync([row.ToEntry()])).FirstOrDefault();
                if (result is not null) row.Status = result.Applied ? "Applied" : "Failed: " + result.Message;
            }
            else
            {
                row.Status = await _routeManager.DeleteAsync(row.Id) ? "Not applied" : "Failed: Windows did not confirm route removal.";
            }
        }
        catch (Exception ex) { Write("Could not save route changes: " + ex.Message); }
    }

    private async Task DeleteRouteAsync()
    {
        if (SelectedRoute is not { } selected) return;
        var route = selected.ToEntry();
        var deleteSucceeded = await _routeManager.DeleteAsync(route.Id);
        var entries = await _routeStore.DeleteAsync(route.Id);
        Routes.Clear(); foreach (var item in entries) Routes.Add(new RouteRow(item));
        if (!deleteSucceeded && selected.Status == "Applied")
            MessageBox.Show("The route was removed from the saved list, but Windows did not confirm its removal. The cleanup record is retained for the next launch.", "Route cleanup incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task EnableNatTraversalAsync()
    {
        try
        {
            NatTraversalSettings.EnableL2tpNatTraversal();
            Write("Enabled L2TP NAT traversal in PolicyAgent. A Windows reboot is required before it takes effect.");
            MessageBox.Show("L2TP NAT traversal was enabled. Restart Windows for the change to take effect.", "Restart required", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show("Could not update the PolicyAgent setting. Run the app as administrator. " + ex.Message, "Registry update failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        await Task.CompletedTask;
    }

    private async Task PollConnectionAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var connected = await _vpn.IsConnectedAsync(cancellationToken);
                if (connected && !_wasConnected)
                {
                    _wasConnected = true; State = VpnState.Connected; StateText = "Connected"; await ApplyRoutesAsync();
                }
                else if (!connected && _wasConnected)
                {
                    _wasConnected = false; State = VpnState.Disconnected; StateText = "Disconnected";
                    await _routeManager.CleanupAllAsync(cancellationToken);
                    foreach (var route in Routes) route.Status = "Not applied";
                    Write("VPN connection dropped; applied routes were removed.");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Write("Connection state polling failed: " + ex.Message); }
    }

    private VpnSettings CurrentSettings() => new(Server.Trim(), Username.Trim(), Protocol, RememberCredentials,
        RememberCredentials && Password.Length > 0 ? _protector.Protect(Password) : null,
        L2tpPsk.Length > 0 ? _protector.Protect(L2tpPsk) : null);

    private async Task SaveSettingsAsync()
    {
        await _settingsGate.WaitAsync();
        try { await _settingsStore.SaveAsync(CurrentSettings()); }
        finally { _settingsGate.Release(); }
    }

    public void Write(string message)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Logs.Add($"{DateTime.Now:HH:mm:ss}  {message}");
            while (Logs.Count > 500) Logs.RemoveAt(0);
        });
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
