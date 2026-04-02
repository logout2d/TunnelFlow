using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;

namespace TunnelFlow.UI.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly ServiceClient _client;

    [ObservableProperty] private bool _isEditingEnabled = true;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _serverAddress = "";
    [ObservableProperty] private int _serverPort = 443;
    [ObservableProperty] private string _userId = "";
    [ObservableProperty] private string _flow = "";
    [ObservableProperty] private string _network = "tcp";
    [ObservableProperty] private string _security = "tls";
    [ObservableProperty] private string _sni = "";
    [ObservableProperty] private string _fingerprint = "chrome";
    [ObservableProperty] private string _realityPublicKey = "";
    [ObservableProperty] private string _realityShortId = "";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _saveStatus = "";

    public Guid Id { get; set; } = Guid.NewGuid();

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand ActivateCommand { get; }
    public bool ShowEditHint => !IsEditingEnabled;
    public string EditHintText => "Stop the tunnel to edit profile settings.";

    public static IReadOnlyList<string> Networks { get; } = ["tcp", "ws", "grpc"];
    public static IReadOnlyList<string> Securities { get; } = ["tls", "reality", "none"];
    public static IReadOnlyList<string> Fingerprints { get; } = ["chrome", "firefox", "safari", "random"];

    private readonly RelayCommand _saveCmd;
    private readonly RelayCommand _activateCmd;

    public ProfileViewModel(ServiceClient client)
    {
        _client = client;

        _saveCmd = new RelayCommand(
            async () => await SaveAsync(),
            () => IsEditingEnabled &&
                  !string.IsNullOrWhiteSpace(ServerAddress) &&
                  !string.IsNullOrWhiteSpace(UserId) &&
                  ServerPort > 0 && ServerPort <= 65535);
        SaveCommand = _saveCmd;
        _activateCmd = new RelayCommand(
            async () => await ActivateAsync(),
            () => IsEditingEnabled);
        ActivateCommand = _activateCmd;
    }

    partial void OnServerAddressChanged(string value) => _saveCmd.NotifyCanExecuteChanged();
    partial void OnUserIdChanged(string value) => _saveCmd.NotifyCanExecuteChanged();
    partial void OnServerPortChanged(int value) => _saveCmd.NotifyCanExecuteChanged();
    partial void OnIsEditingEnabledChanged(bool value)
    {
        _saveCmd.NotifyCanExecuteChanged();
        _activateCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowEditHint));
    }

    public void LoadProfile(IReadOnlyList<VlessProfile> profiles, Guid? activeProfileId)
    {
        var active = profiles.FirstOrDefault(p => p.Id == activeProfileId)
                     ?? profiles.FirstOrDefault();

        if (active is null)
        {
            ClearProfile();
            return;
        }

        Id = active.Id;
        Name = active.Name;
        ServerAddress = active.ServerAddress;
        ServerPort = active.ServerPort;
        UserId = active.UserId;
        Flow = active.Flow;
        Network = active.Network;
        Security = active.Security;
        Sni = active.Tls?.Sni ?? "";
        Fingerprint = active.Tls?.Fingerprint ?? "chrome";
        RealityPublicKey = active.Tls?.RealityPublicKey ?? "";
        RealityShortId = active.Tls?.RealityShortId ?? "";
        IsActive = active.IsActive;
    }

    private void ClearProfile()
    {
        Id = Guid.NewGuid();
        Name = string.Empty;
        ServerAddress = string.Empty;
        ServerPort = 443;
        UserId = string.Empty;
        Flow = string.Empty;
        Network = "tcp";
        Security = "tls";
        Sni = string.Empty;
        Fingerprint = "chrome";
        RealityPublicKey = string.Empty;
        RealityShortId = string.Empty;
        IsActive = false;
        SaveStatus = string.Empty;
    }

    private async Task SaveAsync()
    {
        var profile = BuildProfile();
        try
        {
            await _client.SendCommandAsync("UpsertProfile", profile, CancellationToken.None);
            SaveStatus = "Saved \u2713";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Error: {ex.Message}";
        }
    }

    private async Task ActivateAsync()
    {
        await SaveAsync();
        if (SaveStatus.StartsWith("Error")) return;

        try
        {
            await _client.SendCommandAsync("ActivateProfile", new { profileId = Id }, CancellationToken.None);
            IsActive = true;
            SaveStatus = "Activated \u2713";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Error: {ex.Message}";
        }
    }

    private VlessProfile BuildProfile()
    {
        var flowTrim = Flow.Trim();
        return new VlessProfile
        {
            Id = Id,
            Name = Name,
            ServerAddress = ServerAddress,
            ServerPort = ServerPort,
            UserId = UserId,
            Flow = flowTrim,
            Network = Network,
            Security = Security,
            IsActive = IsActive,
            Tls = string.Equals(Security, "none", StringComparison.OrdinalIgnoreCase)
                ? null
                : string.Equals(Security, "reality", StringComparison.OrdinalIgnoreCase)
                    ? new TlsOptions
                    {
                        Sni = string.IsNullOrEmpty(Sni) ? ServerAddress : Sni,
                        AllowInsecure = false,
                        Fingerprint = string.IsNullOrEmpty(Fingerprint) ? null : Fingerprint,
                        RealityPublicKey = string.IsNullOrWhiteSpace(RealityPublicKey)
                            ? null
                            : RealityPublicKey.Trim(),
                        RealityShortId = string.IsNullOrWhiteSpace(RealityShortId)
                            ? null
                            : RealityShortId.Trim()
                    }
                    : new TlsOptions
                    {
                        Sni = string.IsNullOrEmpty(Sni) ? ServerAddress : Sni,
                        AllowInsecure = false,
                        Fingerprint = string.IsNullOrEmpty(Fingerprint) ? null : Fingerprint
                    }
        };
    }
}
