using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelFlow.Core.IPC.Messages;
using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;

namespace TunnelFlow.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ServiceClient _client;
    private readonly LocalConfigSnapshotLoader _configLoader;
    private readonly IServiceControlManager _serviceControlManager;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _captureRunning;
    [ObservableProperty] private string _singBoxStatus = "Stopped";
    [ObservableProperty] private TunnelStatusMode _selectedMode = TunnelStatusMode.Legacy;
    [ObservableProperty] private bool _singBoxRunning;
    [ObservableProperty] private bool _tunnelInterfaceUp;
    [ObservableProperty] private string _activeProfileName = "None selected";
    [ObservableProperty] private int _proxyRuleCount;
    [ObservableProperty] private int _directRuleCount;
    [ObservableProperty] private int _blockRuleCount;
    [ObservableProperty] private string _connectionStatus = "Connecting to service...";
    [ObservableProperty] private string _serviceActionStatus = string.Empty;
    [ObservableProperty] private bool _isServiceInstalled = true;
    [ObservableProperty] private object _currentView;
    [ObservableProperty] private ServiceActionKind _pendingServiceAction;

    public string ServiceConnectionSummary => IsConnected ? "Service: On" : "Service: Off";

    public string ServiceActionLabel => PendingServiceAction switch
    {
        ServiceActionKind.Start => "Starting Service...",
        ServiceActionKind.Restart => "Restarting Service...",
        _ => IsConnected ? "Restart Service" : "Start Service"
    };

    public bool ShowServiceActionStatus => !string.IsNullOrWhiteSpace(ServiceActionStatus);

    public string ModeSummary => SelectedMode == TunnelStatusMode.Tun ? "TUN" : "Legacy";

    public string EngineStatusSummary =>
        !IsConnected ? "Unavailable" : (SingBoxRunning ? "Running" : SingBoxStatus);

    public string TunnelStatusSummary =>
        !IsConnected
            ? "Unavailable"
            : SelectedMode == TunnelStatusMode.Tun
                ? (TunnelInterfaceUp ? "Up" : "Down")
                : "Not enabled";

    public string RuleCountsSummary =>
        $"Proxy {ProxyRuleCount}  Direct {DirectRuleCount}  Block {BlockRuleCount}";

    public AppRulesViewModel AppRules { get; }
    public ProfileViewModel Profile { get; }
    public SessionsViewModel Sessions { get; }
    public LogViewModel Log { get; }

    public IRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand ManageServiceCommand { get; }
    public IRelayCommand NavigateToRulesCommand { get; }
    public IRelayCommand NavigateToProfileCommand { get; }
    public IRelayCommand NavigateToLogCommand { get; }

    private RelayCommand _startCmd = null!;
    private RelayCommand _stopCmd = null!;
    private RelayCommand _manageServiceCmd = null!;

    public MainViewModel(
        ServiceClient client,
        LocalConfigSnapshotLoader? configLoader = null,
        IServiceControlManager? serviceControlManager = null)
    {
        _client = client;
        _configLoader = configLoader ?? new LocalConfigSnapshotLoader();
        _serviceControlManager = serviceControlManager ?? new WindowsServiceControlManager();

        AppRules = new AppRulesViewModel(client);
        Profile = new ProfileViewModel(client);
        Sessions = new SessionsViewModel();
        Log = new LogViewModel();
        _currentView = AppRules;

        _startCmd = new RelayCommand(async () => await StartAsync(), () => !CaptureRunning && IsConnected);
        _stopCmd = new RelayCommand(async () => await StopAsync(), () => CaptureRunning && IsConnected);
        _manageServiceCmd = new RelayCommand(
            async () => await RequestServiceActionAsync(),
            () => PendingServiceAction == ServiceActionKind.None && IsServiceInstalled);

        StartCommand = _startCmd;
        StopCommand = _stopCmd;
        ManageServiceCommand = _manageServiceCmd;

        NavigateToRulesCommand = new RelayCommand(() => CurrentView = AppRules);
        NavigateToProfileCommand = new RelayCommand(() => CurrentView = Profile);
        NavigateToLogCommand = new RelayCommand(() => CurrentView = Log);
    }

    partial void OnIsConnectedChanged(bool value)
    {
        _startCmd.NotifyCanExecuteChanged();
        _stopCmd.NotifyCanExecuteChanged();
        _manageServiceCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ServiceConnectionSummary));
        OnPropertyChanged(nameof(ServiceActionLabel));
        OnPropertyChanged(nameof(EngineStatusSummary));
        OnPropertyChanged(nameof(TunnelStatusSummary));
    }

    partial void OnCaptureRunningChanged(bool value)
    {
        _startCmd.NotifyCanExecuteChanged();
        _stopCmd.NotifyCanExecuteChanged();
    }

    partial void OnSelectedModeChanged(TunnelStatusMode value)
    {
        Sessions.SetMode(value);
        OnPropertyChanged(nameof(ModeSummary));
        OnPropertyChanged(nameof(TunnelStatusSummary));
    }

    partial void OnSingBoxStatusChanged(string value) =>
        OnPropertyChanged(nameof(EngineStatusSummary));

    partial void OnSingBoxRunningChanged(bool value) =>
        OnPropertyChanged(nameof(EngineStatusSummary));

    partial void OnTunnelInterfaceUpChanged(bool value) =>
        OnPropertyChanged(nameof(TunnelStatusSummary));

    partial void OnProxyRuleCountChanged(int value) =>
        OnPropertyChanged(nameof(RuleCountsSummary));

    partial void OnDirectRuleCountChanged(int value) =>
        OnPropertyChanged(nameof(RuleCountsSummary));

    partial void OnBlockRuleCountChanged(int value) =>
        OnPropertyChanged(nameof(RuleCountsSummary));

    partial void OnServiceActionStatusChanged(string value) =>
        OnPropertyChanged(nameof(ShowServiceActionStatus));

    partial void OnPendingServiceActionChanged(ServiceActionKind value)
    {
        _manageServiceCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ServiceActionLabel));
    }

    partial void OnIsServiceInstalledChanged(bool value) =>
        _manageServiceCmd.NotifyCanExecuteChanged();

    public async Task InitializeAsync()
    {
        _client.Connected += OnClientConnected;
        _client.Disconnected += OnClientDisconnected;
        _client.EventReceived += OnEventReceived;
        _client.DiagnosticMessage += (_, msg) => Log.AddLine("client", "Debug", msg);

        // ConnectAsync blocks until first connection (runs on background thread via Task.Run).
        // OnClientConnected fires during ConnectAsync and calls LoadStateAsync — do NOT call it here.
        await LoadOfflineConfigAsync();
        await _client.ConnectAsync(CancellationToken.None);
    }

    private void OnClientConnected(object? sender, EventArgs e)
    {
        // Runs on the pipe background thread — marshal ALL property sets to the UI thread.
        _ = HandleConnectedAsync();
    }

    private void OnClientDisconnected(object? sender, EventArgs e)
    {
        _ = HandleDisconnectedAsync();
    }

    private void OnEventReceived(object? sender, EventMessage evt)
    {
        if (!evt.Payload.HasValue) return;
        // Clone before capture: the JsonElement is backed by a pooled buffer on the read thread.
        var payload = evt.Payload.Value.Clone();
        var type = evt.Type;

        // Always dispatch: this handler runs on the pipe read thread.
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            switch (type)
            {
                case "StatusChanged":
                    var status = JsonSerializer.Deserialize<StatusPayload>(payload, _jsonOptions);
                    if (status is not null)
                    {
                        ApplyStatusPayload(status);
                    }
                    break;

                case "SessionCreated":
                    Sessions.AddSessionFromJson(payload);
                    break;

                case "SessionClosed":
                    ulong flowId = payload.TryGetProperty("flowId", out var fi) ? fi.GetUInt64() : 0;
                    Sessions.RemoveSession(flowId);
                    break;

                case "LogLine":
                    string source = payload.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "";
                    string level = payload.TryGetProperty("level", out var lvl) ? lvl.GetString() ?? "Info" : "Info";
                    string message = payload.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
                    Log.AddLine(source, level, message);
                    break;

                case "SingBoxCrashed":
                    int attempt = payload.TryGetProperty("attempt", out var at) ? at.GetInt32() : 0;
                    int retrying = payload.TryGetProperty("retryingIn", out var ri) ? ri.GetInt32() : 0;
                    Log.AddLine("service", "Warning",
                        $"sing-box crashed (attempt {attempt}) — retrying in {retrying}s");
                    break;
            }
        });
    }

    private async Task LoadStateAsync()
    {
        try
        {
            var result = await _client.SendCommandAsync("GetState", null, CancellationToken.None);
            if (!result.HasValue) return;

            var state = JsonSerializer.Deserialize<StatePayload>(result.Value, _jsonOptions);
            if (state is null) return;

            await DispatchAsync(() =>
            {
                ApplyStatePayload(state);
                AppRules.LoadRules(state.Rules);
                Profile.LoadProfile(state.Profiles, state.ActiveProfileId);
                IsConnected = true;
                ConnectionStatus = "Connected";
            });
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
            {
                IsConnected = false;
                ConnectionStatus = $"Error: {ex.Message}";
                ApplyServiceUnavailableRuntimeState();
            });

            await LoadOfflineConfigAsync();
        }
    }

    private async Task StartAsync()
    {
        try
        {
            await _client.SendCommandAsync("StartCapture", null, CancellationToken.None);
            Log.AddLine("ui", "Info", "Start capture requested");
        }
        catch (Exception ex)
        {
            Log.AddLine("ui", "Error", $"Start capture failed: {ex.Message}");
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await _client.SendCommandAsync("StopCapture", null, CancellationToken.None);
            Log.AddLine("ui", "Info", "Stop capture requested");
        }
        catch (Exception ex)
        {
            Log.AddLine("ui", "Error", $"Stop capture failed: {ex.Message}");
        }
    }

    internal void ApplyStatePayload(StatePayload state)
    {
        CaptureRunning = state.CaptureRunning;
        SingBoxStatus = state.SingBoxStatus.ToString();
        SelectedMode = state.SelectedMode;
        SingBoxRunning = state.SingBoxRunning;
        TunnelInterfaceUp = state.TunnelInterfaceUp;
        ActiveProfileName = string.IsNullOrWhiteSpace(state.ActiveProfileName)
            ? "None selected"
            : state.ActiveProfileName;
        ProxyRuleCount = state.ProxyRuleCount;
        DirectRuleCount = state.DirectRuleCount;
        BlockRuleCount = state.BlockRuleCount;
    }

    internal void ApplyStatusPayload(StatusPayload status)
    {
        CaptureRunning = status.CaptureRunning;
        SingBoxStatus = status.SingBoxStatus.ToString();
        SelectedMode = status.SelectedMode;
        SingBoxRunning = status.SingBoxRunning;
        TunnelInterfaceUp = status.TunnelInterfaceUp;
        ActiveProfileName = string.IsNullOrWhiteSpace(status.ActiveProfileName)
            ? "None selected"
            : status.ActiveProfileName;
        ProxyRuleCount = status.ProxyRuleCount;
        DirectRuleCount = status.DirectRuleCount;
        BlockRuleCount = status.BlockRuleCount;
    }

    internal void ApplyOfflineConfigSnapshot(LocalConfigSnapshot snapshot)
    {
        if (IsConnected)
        {
            return;
        }

        AppRules.LoadRules(snapshot.Rules);
        Profile.LoadProfile(snapshot.Profiles, snapshot.ActiveProfileId);

        SelectedMode = snapshot.UseTunMode ? TunnelStatusMode.Tun : TunnelStatusMode.Legacy;
        ActiveProfileName = ResolveActiveProfileName(snapshot.Profiles, snapshot.ActiveProfileId);

        var enabledRules = snapshot.Rules.Where(rule => rule.IsEnabled).ToList();
        ProxyRuleCount = enabledRules.Count(rule => rule.Mode == RuleMode.Proxy);
        DirectRuleCount = enabledRules.Count(rule => rule.Mode == RuleMode.Direct);
        BlockRuleCount = enabledRules.Count(rule => rule.Mode == RuleMode.Block);

        ApplyServiceUnavailableRuntimeState();
    }

    private async Task HandleConnectedAsync()
    {
        await DispatchAsync(() =>
        {
            IsConnected = true;
            IsServiceInstalled = true;
            ConnectionStatus = "Connected";
            ServiceActionStatus = string.Empty;
            PendingServiceAction = ServiceActionKind.None;
        });

        await LoadStateAsync();
    }

    private async Task HandleDisconnectedAsync()
    {
        await DispatchAsync(() =>
        {
            IsConnected = false;
            ConnectionStatus = "Reconnecting...";
            ApplyServiceUnavailableRuntimeState();
        });

        await LoadOfflineConfigAsync();
    }

    private async Task LoadOfflineConfigAsync()
    {
        try
        {
            var snapshot = await _configLoader.LoadAsync(CancellationToken.None);
            await DispatchAsync(() => ApplyOfflineConfigSnapshot(snapshot));
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
                Log.AddLine("ui", "Warning", $"Offline config load failed: {ex.Message}"));
        }
    }

    private void ApplyServiceUnavailableRuntimeState()
    {
        CaptureRunning = false;
        SingBoxRunning = false;
        TunnelInterfaceUp = false;
        SingBoxStatus = "Unavailable";
    }

    internal async Task RequestServiceActionAsync()
    {
        if (PendingServiceAction != ServiceActionKind.None)
        {
            return;
        }

        var requestedAction = IsConnected ? ServiceActionKind.Restart : ServiceActionKind.Start;
        PendingServiceAction = requestedAction;
        ServiceActionStatus = requestedAction == ServiceActionKind.Start
            ? "Starting the Windows service..."
            : "Restarting the Windows service...";

        try
        {
            if (requestedAction == ServiceActionKind.Start)
            {
                await _serviceControlManager.StartAsync(CancellationToken.None);
                Log.AddLine("ui", "Info", "Start service requested");
            }
            else
            {
                await _serviceControlManager.RestartAsync(CancellationToken.None);
                Log.AddLine("ui", "Info", "Restart service requested");
            }

            ServiceActionStatus = "Waiting for service connection...";
        }
        catch (ServiceNotInstalledException ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            IsServiceInstalled = false;
            ServiceActionStatus = "Service not installed";
            Log.AddLine("ui", "Warning", $"Service action failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            ServiceActionStatus = "Service action failed";
            Log.AddLine("ui", "Error", $"Service action failed: {ex.Message}");
        }
    }

    private static string ResolveActiveProfileName(IReadOnlyList<VlessProfile> profiles, Guid? activeProfileId)
    {
        var active = profiles.FirstOrDefault(profile => profile.Id == activeProfileId)
                     ?? profiles.FirstOrDefault();

        return string.IsNullOrWhiteSpace(active?.Name) ? "None selected" : active.Name;
    }

    private static Task DispatchAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}

public enum ServiceActionKind
{
    None,
    Start,
    Restart
}
