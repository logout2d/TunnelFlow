using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;

namespace TunnelFlow.UI.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly ServiceClient _client;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _serverAddress = "";
    [ObservableProperty] private int _serverPort = 443;
    [ObservableProperty] private string _userId = "";
    [ObservableProperty] private string _network = "tcp";
    [ObservableProperty] private string _security = "tls";
    [ObservableProperty] private string _sni = "";
    [ObservableProperty] private string _fingerprint = "chrome";
    [ObservableProperty] private bool _isActive;

    public Guid Id { get; set; } = Guid.NewGuid();

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand ActivateCommand { get; }

    public static IReadOnlyList<string> Networks { get; } = ["tcp", "ws", "grpc"];
    public static IReadOnlyList<string> Securities { get; } = ["tls", "reality", "none"];
    public static IReadOnlyList<string> Fingerprints { get; } = ["chrome", "firefox", "safari", "random"];

    public ProfileViewModel(ServiceClient client)
    {
        _client = client;

        SaveCommand = new RelayCommand(async () => await SaveAsync());
        ActivateCommand = new RelayCommand(async () => await ActivateAsync());
    }

    public void LoadProfile(IReadOnlyList<VlessProfile> profiles, Guid? activeProfileId)
    {
        var active = profiles.FirstOrDefault(p => p.Id == activeProfileId)
                     ?? profiles.FirstOrDefault();

        if (active is null) return;

        Id = active.Id;
        Name = active.Name;
        ServerAddress = active.ServerAddress;
        ServerPort = active.ServerPort;
        UserId = active.UserId;
        Network = active.Network;
        Security = active.Security;
        Sni = active.Tls?.Sni ?? "";
        Fingerprint = active.Tls?.Fingerprint ?? "chrome";
        IsActive = active.IsActive;
    }

    private async Task SaveAsync()
    {
        var profile = BuildProfile();
        try
        {
            await _client.SendCommandAsync("UpsertProfile", profile, CancellationToken.None);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save profile: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ActivateAsync()
    {
        await SaveAsync();
        try
        {
            await _client.SendCommandAsync("ActivateProfile", new { profileId = Id }, CancellationToken.None);
            IsActive = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to activate profile: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private VlessProfile BuildProfile() => new()
    {
        Id = Id,
        Name = Name,
        ServerAddress = ServerAddress,
        ServerPort = ServerPort,
        UserId = UserId,
        Network = Network,
        Security = Security,
        IsActive = IsActive,
        Tls = string.Equals(Security, "none", StringComparison.OrdinalIgnoreCase)
            ? null
            : new TlsOptions
            {
                Sni = string.IsNullOrEmpty(Sni) ? ServerAddress : Sni,
                AllowInsecure = false,
                Fingerprint = string.IsNullOrEmpty(Fingerprint) ? null : Fingerprint
            }
    };
}
