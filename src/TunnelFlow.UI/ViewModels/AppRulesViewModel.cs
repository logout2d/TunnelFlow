using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;

namespace TunnelFlow.UI.ViewModels;

public partial class AppRulesViewModel : ObservableObject
{
    private readonly ServiceClient _client;
    private readonly Func<string, object?, CancellationToken, Task<JsonElement?>> _sendCommandAsync;
    private readonly Func<string?> _promptExeName;
    private readonly RelayCommand _addRuleCmd;
    private readonly RelayCommand _addExeRuleCmd;
    private readonly RelayCommand<AppRuleItemViewModel> _removeRuleCmd;
    private readonly RelayCommand _applyPendingRulesCmd;
    private readonly List<AppRule> _lastServiceRules = [];
    private bool _isReloadingRules;

    [ObservableProperty] private bool _isEditingEnabled = true;
    [ObservableProperty] private bool _isServiceConnected;
    [ObservableProperty] private bool _hasServiceRuleSnapshot;
    [ObservableProperty] private bool _hasPendingLocalChanges;
    [ObservableProperty] private bool _isApplyingPendingRules;

    public ObservableCollection<AppRuleItemViewModel> Rules { get; } = [];

    public IRelayCommand AddRuleCommand { get; }
    public IRelayCommand AddExeRuleCommand { get; }
    public IRelayCommand<AppRuleItemViewModel> RemoveRuleCommand { get; }
    public IRelayCommand ApplyPendingRulesCommand { get; }
    public bool ShowEditHint => !IsEditingEnabled;
    public string EditHintText => "Stop the tunnel to edit rules.";
    public bool ShowPendingRulesStatus => HasPendingLocalChanges;
    public bool ShowApplyPendingRulesAction => HasPendingLocalChanges && IsServiceConnected && HasServiceRuleSnapshot;
    public string ApplyPendingRulesLabel => IsApplyingPendingRules ? "Applying..." : "Apply Pending Rules";
    public string PendingRulesStatusText => IsApplyingPendingRules
        ? "Applying pending App Rules to the service..."
        : IsServiceConnected && !HasServiceRuleSnapshot
            ? "Local App Rules changes are pending. Waiting for the current service rules before you can apply them."
            : IsServiceConnected
            ? "Local App Rules changes are pending. Apply them to sync with the service."
            : "Local App Rules changes are pending. Connect the service to apply them.";

    public static IReadOnlyList<string> AvailableModes { get; } = ["Proxy", "Direct", "Block"];

    public event EventHandler? RulesChanged;

    public AppRulesViewModel(
        ServiceClient client,
        Func<string, object?, CancellationToken, Task<JsonElement?>>? sendCommandAsync = null,
        Func<string?>? promptExeName = null)
    {
        _client = client;
        _sendCommandAsync = sendCommandAsync ?? ((type, payload, cancellationToken) =>
            _client.SendCommandAsync(type, payload, cancellationToken));
        _promptExeName = promptExeName ?? ShowExeNameDialog;

        _addRuleCmd = new RelayCommand(
            async () => await AddRuleAsync(),
            () => IsEditingEnabled);
        _addExeRuleCmd = new RelayCommand(
            async () => await AddExeRuleAsync(),
            () => IsEditingEnabled);
        _removeRuleCmd = new RelayCommand<AppRuleItemViewModel>(
            async item => await RemoveRuleAsync(item!),
            item => IsEditingEnabled && item is not null);
        _applyPendingRulesCmd = new RelayCommand(
            async () => await ApplyPendingRulesAsync(),
            CanExecuteApplyPendingRules);

        AddRuleCommand = _addRuleCmd;
        AddExeRuleCommand = _addExeRuleCmd;
        RemoveRuleCommand = _removeRuleCmd;
        ApplyPendingRulesCommand = _applyPendingRulesCmd;
    }

    partial void OnIsEditingEnabledChanged(bool value)
    {
        _addRuleCmd.NotifyCanExecuteChanged();
        _addExeRuleCmd.NotifyCanExecuteChanged();
        _removeRuleCmd.NotifyCanExecuteChanged();
        _applyPendingRulesCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowEditHint));
    }

    partial void OnIsServiceConnectedChanged(bool value)
    {
        if (!value)
        {
            HasServiceRuleSnapshot = false;
        }

        _applyPendingRulesCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowApplyPendingRulesAction));
        OnPropertyChanged(nameof(PendingRulesStatusText));
    }

    partial void OnHasServiceRuleSnapshotChanged(bool value)
    {
        _applyPendingRulesCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowApplyPendingRulesAction));
        OnPropertyChanged(nameof(PendingRulesStatusText));
    }

    partial void OnHasPendingLocalChangesChanged(bool value)
    {
        _applyPendingRulesCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowPendingRulesStatus));
        OnPropertyChanged(nameof(ShowApplyPendingRulesAction));
        OnPropertyChanged(nameof(PendingRulesStatusText));
    }

    partial void OnIsApplyingPendingRulesChanged(bool value)
    {
        _applyPendingRulesCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ApplyPendingRulesLabel));
        OnPropertyChanged(nameof(PendingRulesStatusText));
    }

    public void LoadPersistedRules(IReadOnlyList<AppRule> rules)
    {
        if (HasPendingLocalChanges)
        {
            RaiseRulesChanged();
            return;
        }

        ReplaceRules(rules);
    }

    public void LoadServiceRules(IReadOnlyList<AppRule> rules)
    {
        HasServiceRuleSnapshot = true;
        _lastServiceRules.Clear();
        _lastServiceRules.AddRange(rules.Select(CloneRule));

        if (HasPendingLocalChanges)
        {
            RaiseRulesChanged();
            return;
        }

        ReplaceRules(rules);
    }

    public IReadOnlyList<AppRule> GetRulesSnapshot() => Rules.Select(item => item.ToAppRule()).ToList();

    public async Task ApplyPendingRulesAsync()
    {
        if (!CanExecuteApplyPendingRules())
        {
            return;
        }

        IsApplyingPendingRules = true;

        try
        {
            var localRules = GetRulesSnapshot();
            var localRuleIds = localRules.Select(rule => rule.Id).ToHashSet();

            foreach (var serviceRule in _lastServiceRules)
            {
                if (!localRuleIds.Contains(serviceRule.Id))
                {
                    await _sendCommandAsync("DeleteRule", new { ruleId = serviceRule.Id }, CancellationToken.None);
                }
            }

            foreach (var localRule in localRules)
            {
                await _sendCommandAsync("UpsertRule", localRule, CancellationToken.None);
            }

            _lastServiceRules.Clear();
            _lastServiceRules.AddRange(localRules.Select(CloneRule));
            HasPendingLocalChanges = false;
        }
        catch
        {
            HasPendingLocalChanges = true;
        }
        finally
        {
            IsApplyingPendingRules = false;
            RaiseRulesChanged();
        }
    }

    private async void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isReloadingRules)
        {
            return;
        }

        if (sender is AppRuleItemViewModel item &&
            e.PropertyName is nameof(AppRuleItemViewModel.IsEnabled) or nameof(AppRuleItemViewModel.Mode))
        {
            RaiseRulesChanged();
            await QueueOrPersistUpsertAsync(item.ToAppRule());
        }
    }

    private async Task AddRuleAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executables|*.exe|All files|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        var exePath = dialog.FileName;
        var displayName = System.IO.Path.GetFileNameWithoutExtension(exePath);

        var rule = new AppRule
        {
            Id = Guid.NewGuid(),
            ExePath = exePath,
            DisplayName = displayName,
            MatchType = AppRuleMatchType.FullPath,
            Mode = RuleMode.Proxy,
            IsEnabled = true
        };

        await AddRuleCoreAsync(rule);
    }

    private async Task AddExeRuleAsync()
    {
        var input = _promptExeName();
        if (input is null)
        {
            return;
        }

        if (!TryNormalizeExeName(input, out var exeName, out var error))
        {
            MessageBox.Show(error, "Invalid executable name",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rule = new AppRule
        {
            Id = Guid.NewGuid(),
            ExePath = exeName,
            DisplayName = System.IO.Path.GetFileNameWithoutExtension(exeName),
            MatchType = AppRuleMatchType.ExeName,
            Mode = RuleMode.Proxy,
            IsEnabled = true
        };

        await AddRuleCoreAsync(rule);
    }

    private async Task AddRuleCoreAsync(AppRule rule)
    {
        try
        {
            var item = new AppRuleItemViewModel(rule);
            Dispatch(() =>
            {
                item.PropertyChanged += OnItemPropertyChanged;
                Rules.Add(item);
            });
            RaiseRulesChanged();
            await QueueOrPersistUpsertAsync(rule);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to add rule: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RemoveRuleAsync(AppRuleItemViewModel item)
    {
        try
        {
            Dispatch(() =>
            {
                item.PropertyChanged -= OnItemPropertyChanged;
                Rules.Remove(item);
            });
            RaiseRulesChanged();
            await QueueOrPersistDeleteAsync(item.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to remove rule: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task QueueOrPersistUpsertAsync(AppRule rule)
    {
        if (!IsServiceConnected || HasPendingLocalChanges)
        {
            HasPendingLocalChanges = true;
            return;
        }

        try
        {
            await _sendCommandAsync("UpsertRule", rule, CancellationToken.None);
            TrackServiceRule(rule);
        }
        catch
        {
            HasPendingLocalChanges = true;
        }
    }

    private async Task QueueOrPersistDeleteAsync(Guid ruleId)
    {
        if (!IsServiceConnected || HasPendingLocalChanges)
        {
            HasPendingLocalChanges = true;
            return;
        }

        try
        {
            await _sendCommandAsync("DeleteRule", new { ruleId }, CancellationToken.None);
            _lastServiceRules.RemoveAll(rule => rule.Id == ruleId);
        }
        catch
        {
            HasPendingLocalChanges = true;
        }
    }

    private bool CanExecuteApplyPendingRules() =>
        HasPendingLocalChanges &&
        IsServiceConnected &&
        HasServiceRuleSnapshot &&
        IsEditingEnabled &&
        !IsApplyingPendingRules;

    private void ReplaceRules(IReadOnlyList<AppRule> rules)
    {
        void ApplyRules()
        {
            _isReloadingRules = true;
            try
            {
                foreach (var item in Rules)
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                }

                Rules.Clear();

                foreach (var rule in rules)
                {
                    var item = new AppRuleItemViewModel(rule);
                    item.PropertyChanged += OnItemPropertyChanged;
                    Rules.Add(item);
                }
            }
            finally
            {
                _isReloadingRules = false;
            }

            RaiseRulesChanged();
        }

        Dispatch(ApplyRules);
    }

    private void TrackServiceRule(AppRule rule)
    {
        var existingIndex = _lastServiceRules.FindIndex(existing => existing.Id == rule.Id);
        if (existingIndex >= 0)
        {
            _lastServiceRules[existingIndex] = CloneRule(rule);
            return;
        }

        _lastServiceRules.Add(CloneRule(rule));
    }

    private void RaiseRulesChanged() => RulesChanged?.Invoke(this, EventArgs.Empty);

    private static AppRule CloneRule(AppRule rule) => new()
    {
        Id = rule.Id,
        ExePath = rule.ExePath,
        DisplayName = rule.DisplayName,
        MatchType = rule.MatchType,
        Mode = rule.Mode,
        IsEnabled = rule.IsEnabled
    };

    public static bool TryNormalizeExeName(string? input, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        var trimmed = input?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = "Enter an executable name.";
            return false;
        }

        if (trimmed.Contains('\\') || trimmed.Contains('/'))
        {
            error = "Enter only the executable file name, not a path.";
            return false;
        }

        normalized = trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".exe";
        return true;
    }

    private static string? ShowExeNameDialog()
    {
        var window = new Window
        {
            Title = "Add Exe Rule",
            Width = 320,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current?.MainWindow
        };

        var input = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 12),
            MinHeight = 28
        };

        var ok = new Button
        {
            Content = "Add",
            MinWidth = 72,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 72,
            IsCancel = true
        };

        ok.Click += (_, _) =>
        {
            window.DialogResult = true;
            window.Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel }
        };

        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                input,
                buttons
            }
        };
        window.Loaded += (_, _) =>
        {
            input.Focus();
            Keyboard.Focus(input);
        };

        return window.ShowDialog() == true ? input.Text : null;
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}

public partial class AppRuleItemViewModel : ObservableObject
{
    [ObservableProperty] private string _exePath;
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private AppRuleMatchType _matchType;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _mode;

    public Guid Id { get; }

    public AppRuleItemViewModel(AppRule rule)
    {
        Id = rule.Id;
        _exePath = rule.ExePath;
        _displayName = rule.DisplayName;
        _matchType = rule.MatchType;
        _isEnabled = rule.IsEnabled;
        _mode = rule.Mode.ToString();
    }

    public string MatchTypeLabel => MatchType == AppRuleMatchType.ExeName ? "Exe" : "Path";

    public AppRule ToAppRule() => new()
    {
        Id = Id,
        ExePath = ExePath,
        DisplayName = DisplayName,
        MatchType = MatchType,
        IsEnabled = IsEnabled,
        Mode = Enum.TryParse<RuleMode>(Mode, out var m) ? m : RuleMode.Proxy
    };
}
