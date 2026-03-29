using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelFlow.Core.IPC.Messages;
using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.UI.Services;

namespace TunnelFlow.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ServiceClient _client;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _captureRunning;
    [ObservableProperty] private string _singBoxStatus = "Stopped";
    [ObservableProperty] private string _connectionStatus = "Connecting to service...";
    [ObservableProperty] private object _currentView;

    public AppRulesViewModel AppRules { get; }
    public ProfileViewModel Profile { get; }
    public SessionsViewModel Sessions { get; }
    public LogViewModel Log { get; }

    public IRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand NavigateToRulesCommand { get; }
    public IRelayCommand NavigateToProfileCommand { get; }
    public IRelayCommand NavigateToSessionsCommand { get; }
    public IRelayCommand NavigateToLogCommand { get; }

    private RelayCommand _startCmd = null!;
    private RelayCommand _stopCmd = null!;

    public MainViewModel(ServiceClient client)
    {
        _client = client;

        AppRules = new AppRulesViewModel(client);
        Profile = new ProfileViewModel(client);
        Sessions = new SessionsViewModel();
        Log = new LogViewModel();
        _currentView = AppRules;

        _startCmd = new RelayCommand(async () => await StartAsync(), () => !CaptureRunning && IsConnected);
        _stopCmd = new RelayCommand(async () => await StopAsync(), () => CaptureRunning && IsConnected);

        StartCommand = _startCmd;
        StopCommand = _stopCmd;

        NavigateToRulesCommand = new RelayCommand(() => CurrentView = AppRules);
        NavigateToProfileCommand = new RelayCommand(() => CurrentView = Profile);
        NavigateToSessionsCommand = new RelayCommand(() => CurrentView = Sessions);
        NavigateToLogCommand = new RelayCommand(() => CurrentView = Log);
    }

    partial void OnIsConnectedChanged(bool value)
    {
        _startCmd.NotifyCanExecuteChanged();
        _stopCmd.NotifyCanExecuteChanged();
    }

    partial void OnCaptureRunningChanged(bool value)
    {
        _startCmd.NotifyCanExecuteChanged();
        _stopCmd.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        _client.Connected += OnClientConnected;
        _client.Disconnected += OnClientDisconnected;
        _client.EventReceived += OnEventReceived;
        _client.DiagnosticMessage += (_, msg) => Log.AddLine("client", "Debug", msg);

        // ConnectAsync blocks until first connection (runs on background thread via Task.Run).
        // OnClientConnected fires during ConnectAsync and calls LoadStateAsync — do NOT call it here.
        await _client.ConnectAsync(CancellationToken.None);
    }

    private void OnClientConnected(object? sender, EventArgs e)
    {
        // Runs on the pipe background thread — marshal ALL property sets to the UI thread.
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            IsConnected = true;
            ConnectionStatus = "Connected";
            await LoadStateAsync();
        });
    }

    private void OnClientDisconnected(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsConnected = false;
            CaptureRunning = false;
            ConnectionStatus = "Reconnecting...";
        });
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
                    CaptureRunning = payload.TryGetProperty("captureRunning", out var cr) && cr.GetBoolean();
                    SingBoxStatus = payload.TryGetProperty("singboxStatus", out var ss)
                        ? ss.GetString() ?? "Unknown" : "Unknown";
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

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CaptureRunning = state.CaptureRunning;
                SingBoxStatus = state.SingBoxStatus.ToString();
                AppRules.LoadRules(state.Rules);
                Profile.LoadProfile(state.Profiles, state.ActiveProfileId);
                IsConnected = true;
                ConnectionStatus = "Connected";
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
                ConnectionStatus = $"Error: {ex.Message}");
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
}
