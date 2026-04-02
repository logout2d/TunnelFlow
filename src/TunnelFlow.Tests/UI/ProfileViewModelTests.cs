using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.Tests.UI;

public class ProfileViewModelTests
{
    [Fact]
    public void LoadProfile_PopulatesChoices_AndSelectsActiveProfile()
    {
        using var client = new ServiceClient();
        var viewModel = new ProfileViewModel(client);
        var primaryId = Guid.NewGuid();
        var backupId = Guid.NewGuid();

        viewModel.LoadProfile(
        [
            new VlessProfile
            {
                Id = primaryId,
                Name = "Primary",
                ServerAddress = "primary.example.com",
                ServerPort = 443,
                UserId = "11111111-1111-1111-1111-111111111111",
                Network = "tcp",
                Security = "tls",
                IsActive = true
            },
            new VlessProfile
            {
                Id = backupId,
                Name = "Backup",
                ServerAddress = "backup.example.com",
                ServerPort = 8443,
                UserId = "22222222-2222-2222-2222-222222222222",
                Network = "ws",
                Security = "reality"
            }
        ], primaryId);

        Assert.True(viewModel.ShowProfileSelector);
        Assert.Equal("Primary", viewModel.ActiveProfileDisplayName);
        Assert.Equal("Active profile: Primary", viewModel.ActiveProfileSummary);
        Assert.Equal(primaryId, viewModel.SelectedProfile?.Id);
        Assert.Equal("Primary", viewModel.Name);
        Assert.True(viewModel.IsActive);
        Assert.Equal(2, viewModel.AvailableProfiles.Count);
        Assert.Contains(viewModel.AvailableProfiles, option => option.DisplayName == "Primary (Active)");
        Assert.False(viewModel.ActivateCommand.CanExecute(null));
    }

    [Fact]
    public void SelectingDifferentProfile_UpdatesFormFields_AndEnablesActivation()
    {
        using var client = new ServiceClient();
        var viewModel = new ProfileViewModel(client);
        var primaryId = Guid.NewGuid();
        var backupId = Guid.NewGuid();

        viewModel.LoadProfile(
        [
            new VlessProfile
            {
                Id = primaryId,
                Name = "Primary",
                ServerAddress = "primary.example.com",
                ServerPort = 443,
                UserId = "11111111-1111-1111-1111-111111111111",
                Network = "tcp",
                Security = "tls",
                IsActive = true
            },
            new VlessProfile
            {
                Id = backupId,
                Name = "Backup",
                ServerAddress = "backup.example.com",
                ServerPort = 8443,
                UserId = "22222222-2222-2222-2222-222222222222",
                Network = "ws",
                Security = "reality"
            }
        ], primaryId);

        viewModel.SelectedProfile = viewModel.AvailableProfiles.Single(option => option.Id == backupId);

        Assert.Equal(backupId, viewModel.SelectedProfile?.Id);
        Assert.Equal("Backup", viewModel.Name);
        Assert.Equal("backup.example.com", viewModel.ServerAddress);
        Assert.Equal(8443, viewModel.ServerPort);
        Assert.Equal("ws", viewModel.Network);
        Assert.Equal("reality", viewModel.Security);
        Assert.False(viewModel.IsActive);
        Assert.True(viewModel.ActivateCommand.CanExecute(null));
        Assert.Equal("Active profile: Primary", viewModel.ActiveProfileSummary);
    }
}
