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
        var viewModel = new ProfileViewModel(client)
        {
            IsServiceConnected = true
        };
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
        Assert.False(viewModel.IsCreatingNewProfile);
        Assert.Equal("Primary", viewModel.Name);
        Assert.True(viewModel.IsActive);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.Equal(2, viewModel.AvailableProfiles.Count);
        Assert.Contains(viewModel.AvailableProfiles, option => option.DisplayName == "Primary (Active)");
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.False(viewModel.ActivateCommand.CanExecute(null));
    }

    [Fact]
    public void SelectingDifferentProfile_UpdatesFormFields_AndEnablesActivation()
    {
        using var client = new ServiceClient();
        var viewModel = new ProfileViewModel(client)
        {
            IsServiceConnected = true
        };
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
        Assert.False(viewModel.IsCreatingNewProfile);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.True(viewModel.ActivateCommand.CanExecute(null));
        Assert.Equal("Active profile: Primary", viewModel.ActiveProfileSummary);
    }

    [Fact]
    public void AddNew_ClearsEditableFields_AndKeepsExistingActiveProfileSummary()
    {
        using var client = new ServiceClient();
        var viewModel = new ProfileViewModel(client)
        {
            IsServiceConnected = true
        };
        var primaryId = Guid.NewGuid();

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
            }
        ], primaryId);

        var originalId = viewModel.Id;

        viewModel.AddNewCommand.Execute(null);

        Assert.True(viewModel.IsCreatingNewProfile);
        Assert.True(viewModel.ShowProfileSelector);
        Assert.Null(viewModel.SelectedProfile);
        Assert.NotEqual(originalId, viewModel.Id);
        Assert.Equal(string.Empty, viewModel.Name);
        Assert.Equal(string.Empty, viewModel.ServerAddress);
        Assert.Equal(443, viewModel.ServerPort);
        Assert.Equal(string.Empty, viewModel.UserId);
        Assert.Equal("tcp", viewModel.Network);
        Assert.Equal("tls", viewModel.Security);
        Assert.False(viewModel.IsActive);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
        Assert.False(viewModel.ActivateCommand.CanExecute(null));
        Assert.Equal("Active profile: Primary", viewModel.ActiveProfileSummary);
    }

    [Fact]
    public void SaveCommand_RequiresServiceConnection_ValidFields_AndUnsavedChanges()
    {
        using var client = new ServiceClient();
        var viewModel = new ProfileViewModel(client)
        {
            IsEditingEnabled = true,
            IsServiceConnected = false,
            ServerAddress = "server.example.com",
            ServerPort = 443,
            UserId = "11111111-1111-1111-1111-111111111111"
        };

        Assert.True(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        viewModel.IsServiceConnected = true;

        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void ActivateCommand_RequiresConnectedExistingNonActiveSelection()
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

        Assert.False(viewModel.ActivateCommand.CanExecute(null));

        viewModel.IsServiceConnected = true;
        Assert.True(viewModel.ActivateCommand.CanExecute(null));

        viewModel.AddNewCommand.Execute(null);
        Assert.False(viewModel.ActivateCommand.CanExecute(null));
    }

    [Fact]
    public void EditHintText_PrefersTunnelHint_AndFallsBackToOfflineHint()
    {
        using var client = new ServiceClient();
        var viewModel = new ProfileViewModel(client)
        {
            IsEditingEnabled = true,
            IsServiceConnected = true
        };

        Assert.False(viewModel.ShowEditHint);
        Assert.Equal(string.Empty, viewModel.EditHintText);

        viewModel.IsServiceConnected = false;
        Assert.True(viewModel.ShowEditHint);
        Assert.Equal("Start the service to save profile changes.", viewModel.EditHintText);

        viewModel.IsEditingEnabled = false;
        Assert.True(viewModel.ShowEditHint);
        Assert.Equal("Stop the tunnel to edit profile settings.", viewModel.EditHintText);
    }

    [Fact]
    public void SaveCommand_IsDisabledWhenLoadedProfileHasNoChanges_AndReEnablesAfterEdit()
    {
        using var client = new ServiceClient();
        var viewModel = new ProfileViewModel(client)
        {
            IsServiceConnected = true
        };
        var primaryId = Guid.NewGuid();

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
            }
        ], primaryId);

        Assert.False(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        viewModel.Name = "Primary updated";

        Assert.True(viewModel.HasUnsavedChanges);
        Assert.Equal("Unsaved changes", viewModel.SaveStatus);
        Assert.True(viewModel.SaveCommand.CanExecute(null));

        viewModel.Name = "Primary";

        Assert.False(viewModel.HasUnsavedChanges);
        Assert.Equal(string.Empty, viewModel.SaveStatus);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveCommand_BlocksRealityProfilesUntilRequiredFieldsAreFilled()
    {
        using var client = new ServiceClient();
        var viewModel = new ProfileViewModel(client)
        {
            IsEditingEnabled = true,
            IsServiceConnected = true,
            ServerAddress = "reality.example.com",
            ServerPort = 443,
            UserId = "11111111-1111-1111-1111-111111111111",
            Security = "reality"
        };

        Assert.True(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        viewModel.RealityPublicKey = "public-key";
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        viewModel.RealityShortId = "short-id";
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }
}
