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
    private readonly Func<string, string, bool> _confirmServiceAction;
    private readonly Func<string, object?, CancellationToken, Task<JsonElement?>> _sendCommandAsync;
    private readonly Action _disposeClient;
    private readonly TimeSpan _ownerHeartbeatInterval;
    private bool _waitingForServiceLogged;
    private Task? _shutdownTask;
    private TaskCompletionSource<bool>? _lifecycleWaiter;
    private Func<bool>? _lifecycleWaitPredicate;
    private CancellationTokenSource? _ownerHeartbeatCts;
    private Task? _ownerHeartbeatTask;
    private string? _activeOwnerSessionId;
    private int _ownerHeartbeatLoopVersion;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _captureRunning;
    [ObservableProperty] private TunnelLifecycleState _lifecycleState = TunnelLifecycleState.Stopped;
    [ObservableProperty] private string _singBoxStatus = "Stopped";
    [ObservableProperty] private TunnelStatusMode _selectedMode = TunnelStatusMode.Legacy;
    [ObservableProperty] private bool _singBoxRunning;
    [ObservableProperty] private bool _tunnelInterfaceUp;
    [ObservableProperty] private string _activeProfileName = "None selected";
    [ObservableProperty] private int _proxyRuleCount;
    [ObservableProperty] private int _directRuleCount;
    [ObservableProperty] private int _blockRuleCount;
    [ObservableProperty] private RuntimeWarningEvidence _runtimeWarning = RuntimeWarningEvidence.None;
    [ObservableProperty] private string _connectionStatus = "Connecting to service...";
    [ObservableProperty] private string _serviceActionStatus = string.Empty;
    [ObservableProperty] private bool _isServiceInstalled = true;
    [ObservableProperty] private object _currentView;
    [ObservableProperty] private ServiceActionKind _pendingServiceAction;
    [ObservableProperty] private bool _isTunnelActionPending;

    private bool IsTunnelActive => CaptureRunning || SingBoxRunning;

    public string ServiceConnectionSummary => IsConnected ? "Service: On" : "Service: Off";

    public string ServiceActionLabel => PendingServiceAction switch
    {
        ServiceActionKind.Install => "Installing Service...",
        ServiceActionKind.Repair => "Repairing Service...",
        ServiceActionKind.Start => "Starting Service...",
        ServiceActionKind.Restart => "Restarting Service...",
        _ => !IsServiceInstalled ? "Install Service" : (IsConnected ? "Restart Service" : "Repair Service")
    };

    public string UninstallServiceLabel => PendingServiceAction == ServiceActionKind.Uninstall
        ? "Uninstalling Service..."
        : "Uninstall Service";

    public bool ShowServiceActionStatus => !string.IsNullOrWhiteSpace(ServiceActionStatus);

    public bool ShowUninstallServiceAction => IsServiceInstalled;

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

    public bool ShowRuntimeWarning => RuntimeWarning != RuntimeWarningEvidence.None;

    public string RuntimeWarningSummary => RuntimeWarning switch
    {
        RuntimeWarningEvidence.AuthenticationFailure => "Authentication failed",
        RuntimeWarningEvidence.ConnectionProblem => "Connection problem",
        _ => string.Empty
    };

    public AppRulesViewModel AppRules { get; }
    public ProfileViewModel Profile { get; }
    public LogViewModel Log { get; }
    public AboutViewModel About { get; }

    public IRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand ManageServiceCommand { get; }
    public IRelayCommand UninstallServiceCommand { get; }
    public IRelayCommand NavigateToRulesCommand { get; }
    public IRelayCommand NavigateToProfileCommand { get; }
    public IRelayCommand NavigateToLogCommand { get; }
    public IRelayCommand NavigateToAboutCommand { get; }

    private RelayCommand _startCmd = null!;
    private RelayCommand _stopCmd = null!;
    private RelayCommand _manageServiceCmd = null!;
    private RelayCommand _uninstallServiceCmd = null!;

    public MainViewModel(
        ServiceClient client,
        LocalConfigSnapshotLoader? configLoader = null,
        IServiceControlManager? serviceControlManager = null,
        Func<string, string, bool>? confirmServiceAction = null,
        Func<string, object?, CancellationToken, Task<JsonElement?>>? sendCommandAsync = null,
        Action? disposeClient = null,
        TimeSpan? ownerHeartbeatInterval = null)
    {
        _client = client;
        _configLoader = configLoader ?? new LocalConfigSnapshotLoader();
        _serviceControlManager = serviceControlManager ?? new WindowsServiceControlManager();
        _confirmServiceAction = confirmServiceAction ?? ((message, title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
        _sendCommandAsync = sendCommandAsync ?? ((type, payload, cancellationToken) =>
            _client.SendCommandAsync(type, payload, cancellationToken));
        _disposeClient = disposeClient ?? _client.Dispose;
        _ownerHeartbeatInterval = ownerHeartbeatInterval ?? TimeSpan.FromSeconds(5);

        AppRules = new AppRulesViewModel(client);
        Profile = new ProfileViewModel(client);
        Log = new LogViewModel();
        About = new AboutViewModel();
        _currentView = AppRules;
        UpdateConfigEditingState();

        _startCmd = new RelayCommand(async () => await StartAsync(), CanExecuteStartTunnelAction);
        _stopCmd = new RelayCommand(async () => await StopAsync(), CanExecuteStopTunnelAction);
        _manageServiceCmd = new RelayCommand(
            async () => await RequestServiceActionAsync(),
            CanExecuteManageServiceAction);
        _uninstallServiceCmd = new RelayCommand(
            async () => await RequestUninstallServiceAsync(),
            CanExecuteUninstallServiceAction);

        StartCommand = _startCmd;
        StopCommand = _stopCmd;
        ManageServiceCommand = _manageServiceCmd;
        UninstallServiceCommand = _uninstallServiceCmd;

        NavigateToRulesCommand = new RelayCommand(() => CurrentView = AppRules);
        NavigateToProfileCommand = new RelayCommand(() => CurrentView = Profile);
        NavigateToLogCommand = new RelayCommand(() => CurrentView = Log);
        NavigateToAboutCommand = new RelayCommand(() => CurrentView = About);
    }

    partial void OnIsConnectedChanged(bool value)
    {
        _startCmd.NotifyCanExecuteChanged();
        _stopCmd.NotifyCanExecuteChanged();
        _manageServiceCmd.NotifyCanExecuteChanged();
        _uninstallServiceCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ServiceConnectionSummary));
        OnPropertyChanged(nameof(ServiceActionLabel));
        OnPropertyChanged(nameof(UninstallServiceLabel));
        OnPropertyChanged(nameof(EngineStatusSummary));
        OnPropertyChanged(nameof(TunnelStatusSummary));
        UpdateConfigEditingState();
        UpdateOwnerHeartbeatLoop();
        TryCompleteLifecycleWait();
    }

    partial void OnCaptureRunningChanged(bool value)
    {
        _startCmd.NotifyCanExecuteChanged();
        _stopCmd.NotifyCanExecuteChanged();
        _manageServiceCmd.NotifyCanExecuteChanged();
        _uninstallServiceCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowUninstallServiceAction));
        UpdateConfigEditingState();
    }

    partial void OnLifecycleStateChanged(TunnelLifecycleState value)
    {
        _startCmd.NotifyCanExecuteChanged();
        _stopCmd.NotifyCanExecuteChanged();
        UpdateOwnerHeartbeatLoop();
        TryCompleteLifecycleWait();
    }

    partial void OnSelectedModeChanged(TunnelStatusMode value)
    {
        OnPropertyChanged(nameof(ModeSummary));
        OnPropertyChanged(nameof(TunnelStatusSummary));
    }

    partial void OnSingBoxStatusChanged(string value) =>
        OnPropertyChanged(nameof(EngineStatusSummary));

    partial void OnSingBoxRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(EngineStatusSummary));
        _manageServiceCmd.NotifyCanExecuteChanged();
        _uninstallServiceCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowUninstallServiceAction));
        UpdateConfigEditingState();
    }

    partial void OnTunnelInterfaceUpChanged(bool value) =>
        OnPropertyChanged(nameof(TunnelStatusSummary));

    partial void OnProxyRuleCountChanged(int value) =>
        OnPropertyChanged(nameof(RuleCountsSummary));

    partial void OnDirectRuleCountChanged(int value) =>
        OnPropertyChanged(nameof(RuleCountsSummary));

    partial void OnBlockRuleCountChanged(int value) =>
        OnPropertyChanged(nameof(RuleCountsSummary));

    partial void OnRuntimeWarningChanged(RuntimeWarningEvidence value)
    {
        OnPropertyChanged(nameof(ShowRuntimeWarning));
        OnPropertyChanged(nameof(RuntimeWarningSummary));
    }

    partial void OnServiceActionStatusChanged(string value) =>
        OnPropertyChanged(nameof(ShowServiceActionStatus));

    partial void OnPendingServiceActionChanged(ServiceActionKind value)
    {
        _manageServiceCmd.NotifyCanExecuteChanged();
        _uninstallServiceCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ServiceActionLabel));
        OnPropertyChanged(nameof(UninstallServiceLabel));
    }

    partial void OnIsTunnelActionPendingChanged(bool value)
    {
        _startCmd.NotifyCanExecuteChanged();
        _stopCmd.NotifyCanExecuteChanged();
    }

    partial void OnIsServiceInstalledChanged(bool value)
    {
        _manageServiceCmd.NotifyCanExecuteChanged();
        _uninstallServiceCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ServiceActionLabel));
        OnPropertyChanged(nameof(ShowUninstallServiceAction));
    }

    public async Task InitializeAsync()
    {
        _client.Connected += OnClientConnected;
        _client.Disconnected += OnClientDisconnected;
        _client.EventReceived += OnEventReceived;
        _client.DiagnosticMessage += (_, msg) => HandleClientDiagnosticMessage(msg);

        // ConnectAsync blocks until first connection (runs on background thread via Task.Run).
        // OnClientConnected fires during ConnectAsync and calls LoadStateAsync — do NOT call it here.
        await RefreshServiceInstallationStateAsync();
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
        if (!CanExecuteStartTunnelAction())
        {
            return;
        }

        IsTunnelActionPending = true;
        try
        {
            await _sendCommandAsync(
                "StartCapture",
                new StartCapturePayload { OwnerSessionId = _client.SessionId },
                CancellationToken.None);
            Log.AddLine("ui", "Info", "Start capture requested");
        }
        catch (Exception ex)
        {
            Log.AddLine("ui", "Error", $"Start capture failed: {ex.Message}");
        }
        finally
        {
            IsTunnelActionPending = false;
        }
    }

    private async Task StopAsync()
    {
        if (!CanExecuteStopTunnelAction())
        {
            return;
        }

        IsTunnelActionPending = true;
        try
        {
            await _sendCommandAsync("StopCapture", null, CancellationToken.None);
            Log.AddLine("ui", "Info", "Stop capture requested");
        }
        catch (Exception ex)
        {
            Log.AddLine("ui", "Error", $"Stop capture failed: {ex.Message}");
        }
        finally
        {
            IsTunnelActionPending = false;
        }
    }

    internal void ApplyStatePayload(StatePayload state)
    {
        _activeOwnerSessionId = state.ActiveOwnerSessionId;
        CaptureRunning = state.CaptureRunning;
        LifecycleState = state.LifecycleState;
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
        RuntimeWarning = state.RuntimeWarning;
        UpdateOwnerHeartbeatLoop();
    }

    internal void ApplyStatusPayload(StatusPayload status)
    {
        _activeOwnerSessionId = status.ActiveOwnerSessionId;
        CaptureRunning = status.CaptureRunning;
        LifecycleState = status.LifecycleState;
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
        RuntimeWarning = status.RuntimeWarning;
        UpdateOwnerHeartbeatLoop();
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
            RecordServiceConnectedForUi();
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
            RecordServiceDisconnectedForUi();
        });

        await RefreshServiceInstallationStateAsync();
        await LoadOfflineConfigAsync();
    }

    internal async Task RefreshServiceInstallationStateAsync()
    {
        try
        {
            var isInstalled = await _serviceControlManager.IsInstalledAsync(CancellationToken.None);
            await DispatchAsync(() =>
            {
                IsServiceInstalled = isInstalled;
                if (!IsConnected)
                {
                    ConnectionStatus = isInstalled ? "Reconnecting..." : "Service not installed";
                }
            });
        }
        catch (Exception ex)
        {
            await DispatchAsync(() =>
                Log.AddLine("ui", "Warning", $"Service install-state probe failed: {ex.Message}"));
        }
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
        _activeOwnerSessionId = null;
        CaptureRunning = false;
        SingBoxRunning = false;
        TunnelInterfaceUp = false;
        SingBoxStatus = "Unavailable";
        RuntimeWarning = RuntimeWarningEvidence.None;
        UpdateOwnerHeartbeatLoop();
        UpdateConfigEditingState();
    }

    internal bool RequiresGracefulShutdown => LifecycleState != TunnelLifecycleState.Stopped;

    internal Task ShutdownForApplicationExitAsync()
    {
        _shutdownTask ??= ShutdownForApplicationExitCoreAsync();
        return _shutdownTask;
    }

    internal async Task RequestServiceActionAsync()
    {
        if (!CanExecuteManageServiceAction())
        {
            return;
        }

        var requestedAction = !IsServiceInstalled
            ? ServiceActionKind.Install
            : IsConnected
                ? ServiceActionKind.Restart
                : ServiceActionKind.Repair;
        PendingServiceAction = requestedAction;
        ServiceActionStatus = requestedAction switch
        {
            ServiceActionKind.Install => "Installing the Windows service...",
            ServiceActionKind.Repair => "Repairing the Windows service...",
            ServiceActionKind.Start => "Starting the Windows service...",
            _ => "Restarting the Windows service..."
        };

        try
        {
            switch (requestedAction)
            {
                case ServiceActionKind.Install:
                    await _serviceControlManager.InstallAsync(CancellationToken.None);
                    Log.AddLine("ui", "Info", "Install service requested");
                    break;
                case ServiceActionKind.Repair:
                    await _serviceControlManager.RepairAsync(CancellationToken.None);
                    Log.AddLine("ui", "Info", "Repair service requested");
                    break;
                case ServiceActionKind.Start:
                    await _serviceControlManager.StartAsync(CancellationToken.None);
                    Log.AddLine("ui", "Info", "Start service requested");
                    break;
                default:
                    await _serviceControlManager.RestartAsync(CancellationToken.None);
                    Log.AddLine("ui", "Info", "Restart service requested");
                    break;
            }

            await DispatchAsync(() =>
            {
                IsServiceInstalled = true;
                if (!IsConnected && PendingServiceAction == requestedAction)
                {
                    ServiceActionStatus = "Waiting for service connection...";
                }
            });
        }
        catch (ServiceNotInstalledException ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            IsServiceInstalled = false;
            ServiceActionStatus = "Service not installed";
            Log.AddLine("ui", "Warning", $"Service action failed: {ex.Message}");
        }
        catch (ServiceControlAccessDeniedException ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            ServiceActionStatus = "Administrator approval required";
            Log.AddLine("ui", "Warning", $"Service action failed: {ex.Message}");
        }
        catch (ServiceControlTimeoutException ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            ServiceActionStatus = $"{GetServiceActionDisplayName(requestedAction)} timed out";
            Log.AddLine("ui", "Error", $"Service action failed: {ex.Message}");
        }
        catch (ServiceBootstrapperMissingException ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            ServiceActionStatus = "Service bootstrapper not available";
            Log.AddLine("ui", "Error", $"Service action failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            ServiceActionStatus = $"{GetServiceActionDisplayName(requestedAction)} failed";
            Log.AddLine("ui", "Error", $"Service action failed: {ex.Message}");
        }
    }

    internal async Task RequestUninstallServiceAsync()
    {
        if (!CanExecuteUninstallServiceAction())
        {
            return;
        }

        if (!_confirmServiceAction(
                "Uninstall the TunnelFlow Windows service? Saved configuration in ProgramData will be kept.",
                "Uninstall Service"))
        {
            return;
        }

        PendingServiceAction = ServiceActionKind.Uninstall;
        ServiceActionStatus = "Uninstalling the Windows service...";

        try
        {
            await _serviceControlManager.UninstallAsync(CancellationToken.None);
            Log.AddLine("ui", "Info", "Uninstall service requested");
            PendingServiceAction = ServiceActionKind.None;
            IsConnected = false;
            IsServiceInstalled = false;
            ConnectionStatus = "Service not installed";
            ApplyServiceUnavailableRuntimeState();
            ServiceActionStatus = "Service not installed";
        }
        catch (ServiceNotInstalledException ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            IsConnected = false;
            IsServiceInstalled = false;
            ServiceActionStatus = "Service not installed";
            Log.AddLine("ui", "Warning", $"Service action failed: {ex.Message}");
        }
        catch (ServiceControlAccessDeniedException ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            ServiceActionStatus = "Administrator approval required";
            Log.AddLine("ui", "Warning", $"Service action failed: {ex.Message}");
        }
        catch (ServiceControlTimeoutException ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            ServiceActionStatus = "Uninstall timed out";
            Log.AddLine("ui", "Error", $"Service action failed: {ex.Message}");
        }
        catch (ServiceBootstrapperMissingException ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            ServiceActionStatus = "Service bootstrapper not available";
            Log.AddLine("ui", "Error", $"Service action failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            PendingServiceAction = ServiceActionKind.None;
            ServiceActionStatus = "Uninstall failed";
            Log.AddLine("ui", "Error", $"Service action failed: {ex.Message}");
        }
    }

    internal void HandleClientDiagnosticMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (IsExpectedReconnectDiagnostic(message))
        {
            LogWaitingForServiceIfNeeded();
            return;
        }

        Log.AddLine("client", "Debug", message);
    }

    internal void RecordServiceConnectedForUi()
    {
        _waitingForServiceLogged = false;
        Log.AddLine("ui", "Info", "Service connected");
    }

    internal void RecordServiceDisconnectedForUi()
    {
        Log.AddLine("ui", "Warning", "Service disconnected");
        LogWaitingForServiceIfNeeded();
    }

    private void LogWaitingForServiceIfNeeded()
    {
        if (_waitingForServiceLogged)
        {
            return;
        }

        _waitingForServiceLogged = true;
        Log.AddLine("ui", "Info", "Waiting for service...");
    }

    private static bool IsExpectedReconnectDiagnostic(string message) =>
        message.StartsWith("Connect failed", StringComparison.OrdinalIgnoreCase) ||
        message.StartsWith("ReadLoop error", StringComparison.OrdinalIgnoreCase);

    private static string ResolveActiveProfileName(IReadOnlyList<VlessProfile> profiles, Guid? activeProfileId)
    {
        var active = profiles.FirstOrDefault(profile => profile.Id == activeProfileId)
                     ?? profiles.FirstOrDefault();

        return string.IsNullOrWhiteSpace(active?.Name) ? "None selected" : active.Name;
    }

    private void UpdateConfigEditingState()
    {
        AppRules.IsEditingEnabled = !IsTunnelActive;
        Profile.IsEditingEnabled = !IsTunnelActive;
        Profile.IsServiceConnected = IsConnected;
    }

    private void UpdateOwnerHeartbeatLoop()
    {
        if (ShouldRunOwnerHeartbeatLoop())
        {
            EnsureOwnerHeartbeatLoop();
            return;
        }

        StopOwnerHeartbeatLoop();
    }

    private bool ShouldRunOwnerHeartbeatLoop() =>
        IsConnected &&
        LifecycleState != TunnelLifecycleState.Stopped &&
        !string.IsNullOrWhiteSpace(_activeOwnerSessionId) &&
        string.Equals(_activeOwnerSessionId, _client.SessionId, StringComparison.Ordinal);

    private void EnsureOwnerHeartbeatLoop()
    {
        if (_ownerHeartbeatTask is { IsCompleted: false })
        {
            return;
        }

        _ownerHeartbeatCts?.Dispose();
        _ownerHeartbeatCts = new CancellationTokenSource();
        var loopVersion = ++_ownerHeartbeatLoopVersion;
        _ownerHeartbeatTask = RunOwnerHeartbeatLoopAsync(loopVersion, _ownerHeartbeatCts.Token);
    }

    private void StopOwnerHeartbeatLoop()
    {
        _ownerHeartbeatLoopVersion++;
        _ownerHeartbeatCts?.Cancel();
        _ownerHeartbeatCts?.Dispose();
        _ownerHeartbeatCts = null;
        _ownerHeartbeatTask = null;
    }

    private async Task RunOwnerHeartbeatLoopAsync(int loopVersion, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && loopVersion == _ownerHeartbeatLoopVersion)
            {
                if (ShouldRunOwnerHeartbeatLoop() && loopVersion == _ownerHeartbeatLoopVersion)
                {
                    try
                    {
                        await _sendCommandAsync(
                            "OwnerHeartbeat",
                            new OwnerHeartbeatPayload { OwnerSessionId = _client.SessionId },
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex) when (IsConnected)
                    {
                        Log.AddLine("ui", "Debug", $"Owner heartbeat failed: {ex.Message}");
                    }
                }

                await Task.Delay(_ownerHeartbeatInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool CanExecuteManageServiceAction()
    {
        if (PendingServiceAction != ServiceActionKind.None)
        {
            return false;
        }

        return !IsTunnelActive;
    }

    private bool CanExecuteUninstallServiceAction() =>
        IsServiceInstalled && !IsTunnelActive && PendingServiceAction == ServiceActionKind.None;

    private bool CanExecuteStartTunnelAction() =>
        IsConnected &&
        !IsTunnelActionPending &&
        !CaptureRunning &&
        LifecycleState == TunnelLifecycleState.Stopped;

    private bool CanExecuteStopTunnelAction() =>
        IsConnected &&
        !IsTunnelActionPending &&
        CaptureRunning &&
        LifecycleState == TunnelLifecycleState.Running;

    private async Task ShutdownForApplicationExitCoreAsync()
    {
        try
        {
            await WaitForStoppedLifecycleAsync();
        }
        catch (Exception ex)
        {
            Log.AddLine("ui", "Error", $"Application shutdown coordination failed: {ex.Message}");
        }
        finally
        {
            StopOwnerHeartbeatLoop();
            _disposeClient();
        }
    }

    private async Task WaitForStoppedLifecycleAsync()
    {
        while (true)
        {
            if (!IsConnected)
            {
                return;
            }

            switch (LifecycleState)
            {
                case TunnelLifecycleState.Stopped:
                    return;

                case TunnelLifecycleState.Starting:
                    await WaitForLifecycleConditionAsync(() =>
                        !IsConnected || LifecycleState is TunnelLifecycleState.Running or TunnelLifecycleState.Stopped);
                    continue;

                case TunnelLifecycleState.Running:
                    Log.AddLine("ui", "Info", "Stopping tunnel before application exit...");
                    await _sendCommandAsync("StopCapture", null, CancellationToken.None);
                    await WaitForLifecycleConditionAsync(() =>
                        !IsConnected || LifecycleState == TunnelLifecycleState.Stopped);
                    return;

                case TunnelLifecycleState.Stopping:
                    await WaitForLifecycleConditionAsync(() =>
                        !IsConnected || LifecycleState == TunnelLifecycleState.Stopped);
                    return;
            }
        }
    }

    private Task WaitForLifecycleConditionAsync(Func<bool> predicate)
    {
        if (predicate())
        {
            return Task.CompletedTask;
        }

        var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _lifecycleWaitPredicate = predicate;
        _lifecycleWaiter = waiter;
        TryCompleteLifecycleWait();
        return waiter.Task;
    }

    private void TryCompleteLifecycleWait()
    {
        if (_lifecycleWaiter is null || _lifecycleWaitPredicate is null || !_lifecycleWaitPredicate())
        {
            return;
        }

        var waiter = _lifecycleWaiter;
        _lifecycleWaiter = null;
        _lifecycleWaitPredicate = null;
        waiter.TrySetResult(true);
    }

    private static string GetServiceActionDisplayName(ServiceActionKind action) => action switch
    {
        ServiceActionKind.Install => "Install",
        ServiceActionKind.Repair => "Repair",
        ServiceActionKind.Uninstall => "Uninstall",
        ServiceActionKind.Start => "Start",
        ServiceActionKind.Restart => "Restart",
        _ => "Service action"
    };

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
    Install,
    Repair,
    Uninstall,
    Start,
    Restart
}
