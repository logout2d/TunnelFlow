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
    private bool _suppressFormStateTracking;
    private Guid? _activeProfileId;
    private ProfileFormState _savedFormState = ProfileFormState.Empty;
    private bool _hasUnsavedChanges;

    [ObservableProperty] private bool _isEditingEnabled = true;
    [ObservableProperty] private bool _isServiceConnected;
    [ObservableProperty] private ProfileChoiceItem? _selectedProfile;
    [ObservableProperty] private bool _isCreatingNewProfile;
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
    public IRelayCommand AddNewCommand { get; }
    public bool ShowEditHint => !IsEditingEnabled || !IsServiceConnected;
    public string EditHintText => !IsEditingEnabled
        ? "Stop the tunnel to edit profile settings."
        : !IsServiceConnected
            ? "Start the service to save profile changes."
            : string.Empty;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }
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
    private readonly RelayCommand _addNewCmd;

    public ProfileViewModel(ServiceClient client)
    {
        _client = client;
        AvailableProfiles = new ReadOnlyObservableCollection<ProfileChoiceItem>(_availableProfiles);

        _saveCmd = new RelayCommand(
            async () => await SaveAsync(),
            () => IsEditingEnabled &&
                  IsServiceConnected &&
                  HasUnsavedChanges &&
                  IsCurrentFormValid());
        SaveCommand = _saveCmd;
        _activateCmd = new RelayCommand(
            async () => await ActivateAsync(),
            () => IsEditingEnabled &&
                  IsServiceConnected &&
                  !IsCreatingNewProfile &&
                  SelectedProfile is not null &&
                  !IsActive);
        ActivateCommand = _activateCmd;
        _addNewCmd = new RelayCommand(
            BeginCreateNewProfile,
            () => IsEditingEnabled);
        AddNewCommand = _addNewCmd;
    }

    partial void OnNameChanged(string value) => HandleEditableFieldChanged();
    partial void OnServerAddressChanged(string value) => HandleEditableFieldChanged();
    partial void OnServerPortChanged(int value) => HandleEditableFieldChanged();
    partial void OnUserIdChanged(string value) => HandleEditableFieldChanged();
    partial void OnFlowChanged(string value) => HandleEditableFieldChanged();
    partial void OnNetworkChanged(string value) => HandleEditableFieldChanged();
    partial void OnSecurityChanged(string value) => HandleEditableFieldChanged();
    partial void OnSniChanged(string value) => HandleEditableFieldChanged();
    partial void OnFingerprintChanged(string value) => HandleEditableFieldChanged();
    partial void OnRealityPublicKeyChanged(string value) => HandleEditableFieldChanged();
    partial void OnRealityShortIdChanged(string value) => HandleEditableFieldChanged();
    partial void OnIsActiveChanged(bool value) => _activateCmd.NotifyCanExecuteChanged();
    partial void OnIsServiceConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEditHint));
        OnPropertyChanged(nameof(EditHintText));
        RefreshCommandStates();
    }
    partial void OnIsCreatingNewProfileChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowProfileSelector));
        _activateCmd.NotifyCanExecuteChanged();
    }
    partial void OnIsEditingEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEditHint));
        OnPropertyChanged(nameof(EditHintText));
        RefreshCommandStates();
    }
    partial void OnSelectedProfileChanged(ProfileChoiceItem? value)
    {
        _activateCmd.NotifyCanExecuteChanged();

        if (_suppressSelectionChange || value is null)
        {
            return;
        }

        var profile = _profiles.FirstOrDefault(p => p.Id == value.Id);
        if (profile is null)
        {
            return;
        }

        IsCreatingNewProfile = false;
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

        IsCreatingNewProfile = false;
        ApplyProfile(selected);
    }

    private void ClearProfile()
    {
        _suppressFormStateTracking = true;
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
        IsCreatingNewProfile = false;
        _suppressFormStateTracking = false;
        SetSavedFormState(ProfileFormState.Empty, clearStatus: true);
        OnPropertyChanged(nameof(ShowProfileSelector));
        OnPropertyChanged(nameof(ActiveProfileDisplayName));
        OnPropertyChanged(nameof(ActiveProfileSummary));
    }

    private async Task SaveAsync()
    {
        if (!HasUnsavedChanges)
        {
            return;
        }

        var profile = BuildProfile();
        try
        {
            await _client.SendCommandAsync("UpsertProfile", profile, CancellationToken.None);
            UpsertProfile(profile);
            RefreshProfileChoices(profile.Id);
            IsCreatingNewProfile = false;
            SetSavedFormState(CaptureFormState(), clearStatus: false);
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
        _suppressFormStateTracking = true;
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
        _suppressFormStateTracking = false;
        SetSavedFormState(CaptureFormState(), clearStatus: true);
    }

    private void BeginCreateNewProfile()
    {
        _suppressSelectionChange = true;
        SelectedProfile = null;
        _suppressSelectionChange = false;

        _suppressFormStateTracking = true;
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
        IsCreatingNewProfile = true;
        _suppressFormStateTracking = false;
        SetSavedFormState(ProfileFormState.Empty, clearStatus: true);
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

    private void HandleEditableFieldChanged()
    {
        if (_suppressFormStateTracking)
        {
            return;
        }

        ReevaluateFormState(updateStatus: true);
    }

    private void RefreshCommandStates()
    {
        _saveCmd.NotifyCanExecuteChanged();
        _activateCmd.NotifyCanExecuteChanged();
        _addNewCmd.NotifyCanExecuteChanged();
    }

    private void SetSavedFormState(ProfileFormState savedFormState, bool clearStatus)
    {
        _savedFormState = savedFormState;
        ReevaluateFormState(updateStatus: !clearStatus);

        if (clearStatus)
        {
            SaveStatus = string.Empty;
        }
    }

    private void ReevaluateFormState(bool updateStatus)
    {
        HasUnsavedChanges = CaptureFormState() != _savedFormState;

        if (updateStatus)
        {
            if (HasUnsavedChanges)
            {
                SaveStatus = "Unsaved changes";
            }
            else if (SaveStatus == "Unsaved changes")
            {
                SaveStatus = string.Empty;
            }
        }

        RefreshCommandStates();
    }

    private bool IsCurrentFormValid()
    {
        if (string.IsNullOrWhiteSpace(ServerAddress) ||
            string.IsNullOrWhiteSpace(UserId) ||
            ServerPort <= 0 ||
            ServerPort > 65535)
        {
            return false;
        }

        if (!string.Equals(Security, "reality", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(RealityPublicKey) &&
               !string.IsNullOrWhiteSpace(RealityShortId);
    }

    private ProfileFormState CaptureFormState() =>
        new(
            Name.Trim(),
            ServerAddress.Trim(),
            ServerPort,
            UserId.Trim(),
            Flow.Trim(),
            Network.Trim(),
            Security.Trim(),
            Sni.Trim(),
            Fingerprint.Trim(),
            RealityPublicKey.Trim(),
            RealityShortId.Trim());
}

public sealed record ProfileChoiceItem(Guid Id, string DisplayName);

internal sealed record ProfileFormState(
    string Name,
    string ServerAddress,
    int ServerPort,
    string UserId,
    string Flow,
    string Network,
    string Security,
    string Sni,
    string Fingerprint,
    string RealityPublicKey,
    string RealityShortId)
{
    public static ProfileFormState Empty { get; } = new(
        string.Empty,
        string.Empty,
        443,
        string.Empty,
        string.Empty,
        "tcp",
        "tls",
        string.Empty,
        "chrome",
        string.Empty,
        string.Empty);
}
