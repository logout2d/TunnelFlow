using System.Windows;
using System.Text.Json;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;

namespace TunnelFlow.UI.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly ServiceClient _client;
    private readonly IProfileImportService _profileImportService;
    private readonly Func<string, string, bool> _confirmDelete;
    private readonly Func<string, object?, CancellationToken, Task<JsonElement?>> _sendCommandAsync;
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
    [ObservableProperty] private string _importUrl = "";
    [ObservableProperty] private string _importStatus = "";
    [ObservableProperty] private string _subscriptionSourceUrl = "";
    [ObservableProperty] private string _subscriptionProfileKey = "";
    [ObservableProperty] private bool _subscriptionMissingFromSource;

    public Guid Id { get; set; } = Guid.NewGuid();

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand ActivateCommand { get; }
    public IRelayCommand AddNewCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand ImportFromUrlCommand { get; }
    public IRelayCommand UpdateSubscriptionCommand { get; }
    public IRelayCommand CleanupMissingSubscriptionProfileCommand { get; }
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
    public bool ShowImportStatus => !string.IsNullOrWhiteSpace(ImportStatus);
    public ReadOnlyObservableCollection<ProfileChoiceItem> AvailableProfiles { get; }
    public bool ShowProfileSelector => AvailableProfiles.Count > 0;
    public bool HasSubscriptionSource => !string.IsNullOrWhiteSpace(SubscriptionSourceUrl);
    public string SubscriptionSourceSummary => string.IsNullOrWhiteSpace(SubscriptionSourceUrl)
        ? string.Empty
        : "Imported from subscription URL";
    public string SubscriptionSourceUrlDisplay => SubscriptionSourceUrl;
    public string SubscriptionUpdateSummary => !HasSubscriptionSource
        ? string.Empty
        : SubscriptionMissingFromSource
            ? "No longer present in the latest subscription update. Kept locally until you remove it."
            : "Use Update subscription to refresh profiles from this saved source.";
    public string UpdateSubscriptionButtonText => "Update subscription";
    public bool ShowMissingSubscriptionCleanupAction =>
        HasSubscriptionSource &&
        SubscriptionMissingFromSource &&
        !IsCreatingNewProfile;
    public string MissingSubscriptionCleanupSummary => ShowMissingSubscriptionCleanupAction
        ? "Remove this stale subscription profile locally if you no longer need it."
        : string.Empty;
    public string CleanupMissingSubscriptionButtonText => "Remove stale profile";
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
    private readonly RelayCommand _deleteCmd;
    private readonly RelayCommand _importFromUrlCmd;
    private readonly RelayCommand _updateSubscriptionCmd;
    private readonly RelayCommand _cleanupMissingSubscriptionProfileCmd;

    public ProfileViewModel(
        ServiceClient client,
        IProfileImportService? profileImportService = null,
        Func<string, string, bool>? confirmDelete = null,
        Func<string, object?, CancellationToken, Task<JsonElement?>>? sendCommandAsync = null)
    {
        _client = client;
        _profileImportService = profileImportService ?? new DirectUrlProfileImportService();
        _confirmDelete = confirmDelete ?? ((message, title) =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);
        _sendCommandAsync = sendCommandAsync ?? client.SendCommandAsync;
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
        _deleteCmd = new RelayCommand(
            async () => await DeleteSelectedProfileAsync(),
            CanDeleteSelectedProfile);
        DeleteCommand = _deleteCmd;
        _importFromUrlCmd = new RelayCommand(
            async () => await ImportFromUrlAsync(),
            CanImportFromUrl);
        ImportFromUrlCommand = _importFromUrlCmd;
        _updateSubscriptionCmd = new RelayCommand(
            async () => await UpdateSubscriptionAsync(),
            CanUpdateSubscription);
        UpdateSubscriptionCommand = _updateSubscriptionCmd;
        _cleanupMissingSubscriptionProfileCmd = new RelayCommand(
            async () => await CleanupMissingSubscriptionProfileAsync(),
            CanCleanupMissingSubscriptionProfile);
        CleanupMissingSubscriptionProfileCommand = _cleanupMissingSubscriptionProfileCmd;
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
    partial void OnImportUrlChanged(string value)
    {
        _importFromUrlCmd.NotifyCanExecuteChanged();
        if (!string.IsNullOrWhiteSpace(ImportStatus))
        {
            ImportStatus = string.Empty;
        }
    }
    partial void OnSubscriptionSourceUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasSubscriptionSource));
        OnPropertyChanged(nameof(SubscriptionSourceSummary));
        OnPropertyChanged(nameof(SubscriptionSourceUrlDisplay));
        OnPropertyChanged(nameof(SubscriptionUpdateSummary));
        OnPropertyChanged(nameof(ShowMissingSubscriptionCleanupAction));
        OnPropertyChanged(nameof(MissingSubscriptionCleanupSummary));
        _updateSubscriptionCmd.NotifyCanExecuteChanged();
        _cleanupMissingSubscriptionProfileCmd.NotifyCanExecuteChanged();
    }
    partial void OnSubscriptionMissingFromSourceChanged(bool value)
    {
        OnPropertyChanged(nameof(SubscriptionUpdateSummary));
        OnPropertyChanged(nameof(ShowMissingSubscriptionCleanupAction));
        OnPropertyChanged(nameof(MissingSubscriptionCleanupSummary));
        _cleanupMissingSubscriptionProfileCmd.NotifyCanExecuteChanged();
    }
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
        OnPropertyChanged(nameof(ShowMissingSubscriptionCleanupAction));
        OnPropertyChanged(nameof(MissingSubscriptionCleanupSummary));
        _activateCmd.NotifyCanExecuteChanged();
        _deleteCmd.NotifyCanExecuteChanged();
        _cleanupMissingSubscriptionProfileCmd.NotifyCanExecuteChanged();
    }
    partial void OnIsEditingEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEditHint));
        OnPropertyChanged(nameof(EditHintText));
        RefreshCommandStates();
    }
    partial void OnImportStatusChanged(string value) => OnPropertyChanged(nameof(ShowImportStatus));
    partial void OnSelectedProfileChanged(ProfileChoiceItem? value)
    {
        _activateCmd.NotifyCanExecuteChanged();
        _deleteCmd.NotifyCanExecuteChanged();
        _updateSubscriptionCmd.NotifyCanExecuteChanged();
        _cleanupMissingSubscriptionProfileCmd.NotifyCanExecuteChanged();

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
        SubscriptionSourceUrl = string.Empty;
        SubscriptionProfileKey = string.Empty;
        SubscriptionMissingFromSource = false;
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
            await _sendCommandAsync("UpsertProfile", profile, CancellationToken.None);
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

    internal async Task ImportFromUrlAsync()
    {
        if (!CanImportFromUrl())
        {
            return;
        }

        try
        {
            var importResult = await _profileImportService.ImportProfilesAsync(ImportUrl, CancellationToken.None);
            foreach (var importedProfile in importResult.Profiles)
            {
                await _sendCommandAsync("UpsertProfile", importedProfile, CancellationToken.None);
                UpsertProfile(importedProfile);
            }

            var selectedProfile = importResult.Profiles[0];
            RefreshProfileChoices(selectedProfile.Id);
            IsCreatingNewProfile = false;
            ApplyProfile(selectedProfile);
            SetSavedFormState(CaptureFormState(), clearStatus: true);
            ImportUrl = string.Empty;
            ImportStatus = BuildImportStatus(importResult);
        }
        catch (ArgumentException ex)
        {
            ImportStatus = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            ImportStatus = ex.Message;
        }
        catch (Exception)
        {
            ImportStatus = "Import failed. Check the URL and try again.";
        }
    }

    internal async Task UpdateSubscriptionAsync()
    {
        if (!CanUpdateSubscription())
        {
            return;
        }

        var sourceUrl = SubscriptionSourceUrl;
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return;
        }

        try
        {
            var importResult = await _profileImportService.ImportProfilesAsync(sourceUrl, CancellationToken.None);
            var existingProfilesFromSource = _profiles
                .Where(profile => string.Equals(profile.SubscriptionSourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(GetSubscriptionProfileKey, profile => profile, StringComparer.Ordinal);
            var selectedProfileIdBeforeUpdate = SelectedProfile?.Id;
            Guid? selectedProfileIdAfterUpdate = null;
            var updatedCount = 0;
            var addedCount = 0;
            var missingCount = 0;
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var importedProfile in importResult.Profiles)
            {
                var normalizedImportedProfile = EnsureSubscriptionMetadata(importedProfile, sourceUrl);
                var importedKey = GetSubscriptionProfileKey(normalizedImportedProfile);
                seenKeys.Add(importedKey);

                if (existingProfilesFromSource.TryGetValue(importedKey, out var existingProfile))
                {
                    var updatedProfile = normalizedImportedProfile with
                    {
                        Id = existingProfile.Id,
                        SubscriptionMissingFromSource = false,
                        IsActive = existingProfile.Id == _activeProfileId
                    };
                    await _sendCommandAsync("UpsertProfile", updatedProfile, CancellationToken.None);
                    UpsertProfile(updatedProfile);
                    updatedCount++;

                    if (selectedProfileIdBeforeUpdate == existingProfile.Id)
                    {
                        selectedProfileIdAfterUpdate = existingProfile.Id;
                    }

                    continue;
                }

                await _sendCommandAsync("UpsertProfile", normalizedImportedProfile, CancellationToken.None);
                UpsertProfile(normalizedImportedProfile);
                addedCount++;

                selectedProfileIdAfterUpdate ??= normalizedImportedProfile.Id;
            }

            foreach (var existingProfile in existingProfilesFromSource.Values)
            {
                var existingKey = GetSubscriptionProfileKey(existingProfile);
                if (seenKeys.Contains(existingKey))
                {
                    continue;
                }

                var staleProfile = existingProfile with { SubscriptionMissingFromSource = true };
                await _sendCommandAsync("UpsertProfile", staleProfile, CancellationToken.None);
                UpsertProfile(staleProfile);
                missingCount++;
            }

            var nextSelectedProfileId = selectedProfileIdAfterUpdate
                                        ?? selectedProfileIdBeforeUpdate
                                        ?? importResult.Profiles.FirstOrDefault()?.Id;
            RefreshProfileChoices(nextSelectedProfileId);

            var selectedProfile = _profiles.FirstOrDefault(profile => profile.Id == nextSelectedProfileId)
                                  ?? _profiles.FirstOrDefault(profile => string.Equals(profile.SubscriptionSourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase));
            if (selectedProfile is not null)
            {
                IsCreatingNewProfile = false;
                ApplyProfile(selectedProfile);
                SetSavedFormState(CaptureFormState(), clearStatus: true);
            }

            ImportStatus = BuildSubscriptionUpdateStatus(updatedCount, addedCount, importResult.SkippedProfileCount, missingCount);
        }
        catch (ArgumentException ex)
        {
            ImportStatus = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            ImportStatus = ex.Message;
        }
        catch (Exception)
        {
            ImportStatus = "Subscription update failed. Check the URL and try again.";
        }
    }

    private async Task ActivateAsync()
    {
        await SaveAsync();
        if (SaveStatus.StartsWith("Error")) return;

        try
        {
            await _sendCommandAsync("ActivateProfile", new { profileId = Id }, CancellationToken.None);
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

    internal async Task DeleteSelectedProfileAsync()
        => await DeleteSelectedProfileAsync(useMissingSubscriptionPrompt: false);

    internal async Task CleanupMissingSubscriptionProfileAsync()
        => await DeleteSelectedProfileAsync(useMissingSubscriptionPrompt: true);

    private async Task DeleteSelectedProfileAsync(bool useMissingSubscriptionPrompt)
    {
        if (useMissingSubscriptionPrompt ? !CanCleanupMissingSubscriptionProfile() : !CanDeleteSelectedProfile())
        {
            return;
        }

        var selectedProfile = SelectedProfile;
        if (selectedProfile is null)
        {
            return;
        }

        var profile = _profiles.FirstOrDefault(p => p.Id == selectedProfile.Id);
        if (profile is null)
        {
            return;
        }

        var displayName = ResolveProfileName(profile);
        var isMissingSubscriptionProfile = !string.IsNullOrWhiteSpace(profile.SubscriptionSourceUrl) &&
                                           profile.SubscriptionMissingFromSource;
        var confirmationTitle = useMissingSubscriptionPrompt && isMissingSubscriptionProfile
            ? "Remove Stale Subscription Profile"
            : "Delete Profile";
        var confirmationMessage = useMissingSubscriptionPrompt && isMissingSubscriptionProfile
            ? $"Profile \"{displayName}\" is no longer present in its source subscription. Remove it locally?"
            : $"Delete profile \"{displayName}\"?";
        if (!_confirmDelete(confirmationMessage, confirmationTitle))
        {
            return;
        }

        try
        {
            await _sendCommandAsync("DeleteProfile", new { profileId = profile.Id }, CancellationToken.None);
            RemoveProfile(profile.Id);
            SaveStatus = useMissingSubscriptionPrompt && isMissingSubscriptionProfile
                ? "Removed stale profile \u2713"
                : "Deleted \u2713";
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
            SubscriptionSourceUrl = string.IsNullOrWhiteSpace(SubscriptionSourceUrl) ? null : SubscriptionSourceUrl,
            SubscriptionProfileKey = string.IsNullOrWhiteSpace(SubscriptionProfileKey) ? null : SubscriptionProfileKey,
            SubscriptionMissingFromSource = SubscriptionMissingFromSource,
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
        SubscriptionSourceUrl = profile.SubscriptionSourceUrl ?? string.Empty;
        SubscriptionProfileKey = profile.SubscriptionProfileKey ?? string.Empty;
        SubscriptionMissingFromSource = profile.SubscriptionMissingFromSource;
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
        SubscriptionSourceUrl = string.Empty;
        SubscriptionProfileKey = string.Empty;
        SubscriptionMissingFromSource = false;
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
        _availableProfiles.Add(new ProfileChoiceItem(profile.Id, BuildProfileChoiceDisplayName(profile)));
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

    private static string BuildImportStatus(ProfileImportResult result)
    {
        var isSubscriptionImport = result.Profiles.Any(profile => !string.IsNullOrWhiteSpace(profile.SubscriptionSourceUrl));

        if (result.ImportedProfileCount == 1 && result.SkippedProfileCount == 0)
        {
            return isSubscriptionImport
                ? $"Imported 1 profile from subscription."
                : $"Imported \"{ResolveProfileName(result.Profiles[0])}\".";
        }

        var importedLabel = result.ImportedProfileCount == 1 ? "profile" : "profiles";
        if (result.SkippedProfileCount == 0)
        {
            return isSubscriptionImport
                ? $"Imported {result.ImportedProfileCount} {importedLabel} from subscription."
                : $"Imported {result.ImportedProfileCount} {importedLabel}.";
        }

        var skippedLabel = result.SkippedProfileCount == 1 ? "entry" : "entries";
        return isSubscriptionImport
            ? $"Imported {result.ImportedProfileCount} {importedLabel} from subscription; skipped {result.SkippedProfileCount} unsupported {skippedLabel}."
            : $"Imported {result.ImportedProfileCount} {importedLabel}; skipped {result.SkippedProfileCount} unsupported {skippedLabel}.";
    }

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
        _deleteCmd.NotifyCanExecuteChanged();
        _importFromUrlCmd.NotifyCanExecuteChanged();
        _updateSubscriptionCmd.NotifyCanExecuteChanged();
        _cleanupMissingSubscriptionProfileCmd.NotifyCanExecuteChanged();
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

    private bool CanDeleteSelectedProfile() =>
        IsEditingEnabled &&
        IsServiceConnected &&
        !IsCreatingNewProfile &&
        SelectedProfile is not null &&
        _profiles.Any(profile => profile.Id == SelectedProfile.Id);

    private bool CanImportFromUrl() =>
        IsEditingEnabled &&
        IsServiceConnected &&
        !string.IsNullOrWhiteSpace(ImportUrl);

    private bool CanUpdateSubscription() =>
        IsEditingEnabled &&
        IsServiceConnected &&
        !IsCreatingNewProfile &&
        !string.IsNullOrWhiteSpace(SubscriptionSourceUrl);

    private bool CanCleanupMissingSubscriptionProfile() =>
        CanDeleteSelectedProfile() &&
        !string.IsNullOrWhiteSpace(SubscriptionSourceUrl) &&
        SubscriptionMissingFromSource;

    private void RemoveProfile(Guid profileId)
    {
        _profiles.RemoveAll(profile => profile.Id == profileId);

        if (_activeProfileId == profileId)
        {
            _activeProfileId = _profiles.FirstOrDefault()?.Id;
        }

        var nextProfile = _profiles.FirstOrDefault(profile => profile.Id == _activeProfileId)
                          ?? _profiles.FirstOrDefault();

        if (nextProfile is null)
        {
            ClearProfile();
            return;
        }

        RefreshProfileChoices(nextProfile.Id);
        IsCreatingNewProfile = false;
        ApplyProfile(nextProfile);
    }

    private static string GetSubscriptionProfileKey(VlessProfile profile) =>
        string.IsNullOrWhiteSpace(profile.SubscriptionProfileKey)
            ? DirectUrlProfileImportService.BuildSubscriptionProfileKey(profile)
            : profile.SubscriptionProfileKey!;

    private static VlessProfile EnsureSubscriptionMetadata(VlessProfile profile, string subscriptionSourceUrl) =>
        profile with
        {
            SubscriptionSourceUrl = subscriptionSourceUrl,
            SubscriptionProfileKey = string.IsNullOrWhiteSpace(profile.SubscriptionProfileKey)
                ? DirectUrlProfileImportService.BuildSubscriptionProfileKey(profile)
                : profile.SubscriptionProfileKey,
            SubscriptionMissingFromSource = false
        };

    private static string BuildSubscriptionUpdateStatus(int updatedCount, int addedCount, int skippedCount, int missingCount)
    {
        var parts = new List<string>();
        if (updatedCount > 0)
        {
            parts.Add(updatedCount == 1 ? "1 updated" : $"{updatedCount} updated");
        }

        if (addedCount > 0)
        {
            parts.Add(addedCount == 1 ? "1 added" : $"{addedCount} added");
        }

        if (skippedCount > 0)
        {
            parts.Add(skippedCount == 1 ? "1 skipped" : $"{skippedCount} skipped");
        }

        if (missingCount > 0)
        {
            parts.Add(missingCount == 1 ? "1 missing from source" : $"{missingCount} missing from source");
        }

        if (parts.Count == 0)
        {
            return "Subscription is already up to date.";
        }

        return $"Subscription updated: {string.Join(", ", parts)}.";
    }

    private string BuildProfileChoiceDisplayName(VlessProfile profile)
    {
        var suffixes = new List<string>();
        if (profile.Id == _activeProfileId)
        {
            suffixes.Add("Active");
        }

        if (!string.IsNullOrWhiteSpace(profile.SubscriptionSourceUrl))
        {
            suffixes.Add("Subscription");
        }

        if (profile.SubscriptionMissingFromSource)
        {
            suffixes.Add("Missing from source");
        }

        var baseName = ResolveProfileName(profile);
        return suffixes.Count == 0
            ? baseName
            : $"{baseName} ({string.Join(", ", suffixes)})";
    }
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
