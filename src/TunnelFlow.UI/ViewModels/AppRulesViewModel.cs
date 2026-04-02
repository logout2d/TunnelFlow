using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;

namespace TunnelFlow.UI.ViewModels;

public partial class AppRulesViewModel : ObservableObject
{
    private readonly ServiceClient _client;
    private readonly RelayCommand _addRuleCmd;
    private readonly RelayCommand<AppRuleItemViewModel> _removeRuleCmd;

    [ObservableProperty] private bool _isEditingEnabled = true;

    public ObservableCollection<AppRuleItemViewModel> Rules { get; } = [];

    public IRelayCommand AddRuleCommand { get; }
    public IRelayCommand<AppRuleItemViewModel> RemoveRuleCommand { get; }
    public bool ShowEditHint => !IsEditingEnabled;
    public string EditHintText => "Stop the tunnel to edit rules.";

    public static IReadOnlyList<string> AvailableModes { get; } = ["Proxy", "Direct", "Block"];

    public AppRulesViewModel(ServiceClient client)
    {
        _client = client;

        _addRuleCmd = new RelayCommand(
            async () => await AddRuleAsync(),
            () => IsEditingEnabled);
        _removeRuleCmd = new RelayCommand<AppRuleItemViewModel>(
            async item => await RemoveRuleAsync(item!),
            item => IsEditingEnabled && item is not null);

        AddRuleCommand = _addRuleCmd;
        RemoveRuleCommand = _removeRuleCmd;
    }

    partial void OnIsEditingEnabledChanged(bool value)
    {
        _addRuleCmd.NotifyCanExecuteChanged();
        _removeRuleCmd.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowEditHint));
    }

    public void LoadRules(IReadOnlyList<AppRule> rules)
    {
        void ApplyRules()
        {
            foreach (var item in Rules)
                item.PropertyChanged -= OnItemPropertyChanged;

            Rules.Clear();

            foreach (var rule in rules)
            {
                var item = new AppRuleItemViewModel(rule);
                item.PropertyChanged += OnItemPropertyChanged;
                Rules.Add(item);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyRules();
            return;
        }

        dispatcher.Invoke(ApplyRules);
    }

    private async void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AppRuleItemViewModel item &&
            e.PropertyName is nameof(AppRuleItemViewModel.IsEnabled) or nameof(AppRuleItemViewModel.Mode))
        {
            await UpsertRuleAsync(item);
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
            Mode = RuleMode.Proxy,
            IsEnabled = true
        };

        try
        {
            await _client.SendCommandAsync("UpsertRule", rule, CancellationToken.None);
            var item = new AppRuleItemViewModel(rule);
            item.PropertyChanged += OnItemPropertyChanged;
            Application.Current.Dispatcher.Invoke(() => Rules.Add(item));
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
            await _client.SendCommandAsync("DeleteRule", new { ruleId = item.Id }, CancellationToken.None);
            item.PropertyChanged -= OnItemPropertyChanged;
            Application.Current.Dispatcher.Invoke(() => Rules.Remove(item));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to remove rule: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task UpsertRuleAsync(AppRuleItemViewModel item)
    {
        var rule = item.ToAppRule();
        try
        {
            await _client.SendCommandAsync("UpsertRule", rule, CancellationToken.None);
        }
        catch { }
    }
}

public partial class AppRuleItemViewModel : ObservableObject
{
    [ObservableProperty] private string _exePath;
    [ObservableProperty] private string _displayName;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _mode;

    public Guid Id { get; }

    public AppRuleItemViewModel(AppRule rule)
    {
        Id = rule.Id;
        _exePath = rule.ExePath;
        _displayName = rule.DisplayName;
        _isEnabled = rule.IsEnabled;
        _mode = rule.Mode.ToString();
    }

    public AppRule ToAppRule() => new()
    {
        Id = Id,
        ExePath = ExePath,
        DisplayName = DisplayName,
        IsEnabled = IsEnabled,
        Mode = Enum.TryParse<RuleMode>(Mode, out var m) ? m : RuleMode.Proxy
    };
}
