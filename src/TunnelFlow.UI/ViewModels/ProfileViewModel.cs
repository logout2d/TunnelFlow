using System.Windows;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;

namespace TunnelFlow.UI.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly ServiceClient _client;
    private readonly ObservableCollection<ProfileChoiceItem> _availableProfiles = [];
    private readonly List<VlessProfile> _profiles = [];
    private bool _suppressSelectionChange;
    private Guid? _activeProfileId;

    [ObservableProperty] private bool _isEditingEnabled = true;
    [ObservableProperty] private ProfileChoiceItem? _selectedProfile;
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
    public ReadOnlyObservableCollection<ProfileChoiceItem> AvailableProfiles { get; }
    public bool ShowProfileSelector => AvailableProfiles.Count > 0;
    public string ActiveProfileDisplayName => string.IsNullOrWhiteSpace(GetActiveProfile()?.Name)
        ? "None selected"
        : GetActiveProfile()!.Name;
    public string ActiveProfileSummary => $"Active profile: {ActiveProfileDisplayName}";

    public static IReadOnlyList<string> Networks { get; } = ["tcp", "ws", "grpc"];
    public static IReadOnlyList<string> Securities { get; } = ["tls", "reality", "none"];
    public static IReadOnlyList<string> Fingerprints { get; } = ["chrome", "firefox", "safari", "random"];

    private readonly RelayCommand _saveCmd;
    private readonly RelayCommand _activateCmd;

    public ProfileViewModel(ServiceClient client)
    {
        _client = client;
        AvailableProfiles = new ReadOnlyObservableCollection<ProfileChoiceItem>(_availableProfiles);

        _saveCmd = new RelayCommand(
            async () => await SaveAsync(),
            () => IsEditingEnabled &&
                  !string.IsNullOrWhiteSpace(ServerAddress) &&
                  !string.IsNullOrWhiteSpace(UserId) &&
                  ServerPort > 0 && ServerPort <= 65535);
        SaveCommand = _saveCmd;
        _activateCmd = new RelayCommand(
            async () => await ActivateAsync(),
            () => IsEditingEnabled && !IsActive);
        ActivateCommand = _activateCmd;
    }

    partial void OnServerAddressChanged(string value) => _saveCmd.NotifyCanExecuteChanged();
    partial void OnUserIdChanged(string value) => _saveCmd.NotifyCanExecuteChanged();
    partial void OnServerPortChanged(int value) => _saveCmd.NotifyCanExecuteChanged();
    partial void OnIsActiveChanged(bool value) => _activateCmd.NotifyCanExecuteChanged();
    partial void OnIsEditingEnabledChanged(bool value)
    {
        _saveCmd.NotifyCanExecuteChanged();
        _activateCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowEditHint));
    }
    partial void OnSelectedProfileChanged(ProfileChoiceItem? value)
    {
        if (_suppressSelectionChange || value is null)
        {
            return;
        }

        var profile = _profiles.FirstOrDefault(p => p.Id == value.Id);
        if (profile is null)
        {
            return;
        }

        ApplyProfile(profile);
    }

    public void LoadProfile(IReadOnlyList<VlessProfile> profiles, Guid? activeProfileId)
    {
        _profiles.Clear();
        _profiles.AddRange(profiles);
        _activeProfileId = activeProfileId
            ?? _profiles.FirstOrDefault(p => p.IsActive)?.Id
            ?? _profiles.FirstOrDefault()?.Id;

        RefreshProfileChoices(_activeProfileId);

        var selected = _profiles.FirstOrDefault(p => p.Id == _activeProfileId)
                       ?? _profiles.FirstOrDefault();

        if (selected is null)
        {
            ClearProfile();
            return;
        }

        ApplyProfile(selected);
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
        _activeProfileId = null;
        _availableProfiles.Clear();
        _suppressSelectionChange = true;
        SelectedProfile = null;
        _suppressSelectionChange = false;
        OnPropertyChanged(nameof(ShowProfileSelector));
        OnPropertyChanged(nameof(ActiveProfileDisplayName));
        OnPropertyChanged(nameof(ActiveProfileSummary));
    }

    private async Task SaveAsync()
    {
        var profile = BuildProfile();
        try
        {
            await _client.SendCommandAsync("UpsertProfile", profile, CancellationToken.None);
            UpsertProfile(profile);
            RefreshProfileChoices(profile.Id);
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
            _activeProfileId = Id;
            for (var i = 0; i < _profiles.Count; i++)
            {
                _profiles[i] = _profiles[i] with { IsActive = _profiles[i].Id == Id };
            }

            RefreshProfileChoices(Id);
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

    private void ApplyProfile(VlessProfile profile)
    {
        Id = profile.Id;
        Name = profile.Name;
        ServerAddress = profile.ServerAddress;
        ServerPort = profile.ServerPort;
        UserId = profile.UserId;
        Flow = profile.Flow;
        Network = profile.Network;
        Security = profile.Security;
        Sni = profile.Tls?.Sni ?? "";
        Fingerprint = profile.Tls?.Fingerprint ?? "chrome";
        RealityPublicKey = profile.Tls?.RealityPublicKey ?? "";
        RealityShortId = profile.Tls?.RealityShortId ?? "";
        IsActive = profile.Id == _activeProfileId;
    }

    private void RefreshProfileChoices(Guid? selectedProfileId)
    {
        _availableProfiles.Clear();
        foreach (var profile in _profiles)
        {
            _availableProfiles.Add(new ProfileChoiceItem(
                profile.Id,
                profile.Id == _activeProfileId
                    ? $"{ResolveProfileName(profile)} (Active)"
                    : ResolveProfileName(profile)));
        }

        _suppressSelectionChange = true;
        SelectedProfile = _availableProfiles.FirstOrDefault(p => p.Id == selectedProfileId)
            ?? _availableProfiles.FirstOrDefault();
        _suppressSelectionChange = false;

        OnPropertyChanged(nameof(ShowProfileSelector));
        OnPropertyChanged(nameof(ActiveProfileDisplayName));
        OnPropertyChanged(nameof(ActiveProfileSummary));
    }

    private void UpsertProfile(VlessProfile profile)
    {
        var index = _profiles.FindIndex(existing => existing.Id == profile.Id);
        if (index >= 0)
        {
            _profiles[index] = profile;
            return;
        }

        _profiles.Add(profile);
    }

    private VlessProfile? GetActiveProfile() =>
        _profiles.FirstOrDefault(profile => profile.Id == _activeProfileId);

    private static string ResolveProfileName(VlessProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Name) ? "Unnamed profile" : profile.Name;
}

public sealed record ProfileChoiceItem(Guid Id, string DisplayName);
