using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.Tests.UI;

public class ProfileViewModelTests
{
    private static Task<System.Text.Json.JsonElement?> SuccessfulCommandAsync(
        string type,
        object? payload,
        CancellationToken cancellationToken) =>
        Task.FromResult<System.Text.Json.JsonElement?>(null);

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
        Assert.False(viewModel.ShowActiveProfileSubscriptionState);
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
        Assert.False(viewModel.ShowActiveProfileSubscriptionState);
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
        Assert.False(viewModel.ShowActiveProfileSubscriptionState);
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
        Assert.Equal("VLESS Profile", viewModel.ProfileTitle);

        viewModel.IsServiceConnected = false;
        Assert.True(viewModel.ShowEditHint);
        Assert.Equal("Start the service to save profile changes", viewModel.EditHintText);
        Assert.Equal("VLESS Profile (Start the service to save profile changes)", viewModel.ProfileTitle);

        viewModel.IsEditingEnabled = false;
        Assert.True(viewModel.ShowEditHint);
        Assert.Equal("Stop the tunnel to edit profile settings", viewModel.EditHintText);
        Assert.Equal("VLESS Profile (Stop the tunnel to edit profile settings)", viewModel.ProfileTitle);
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

    [Fact]
    public void DeleteCommand_RequiresConnectedExistingSelectedProfile_AndIsDisabledInNewMode()
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

        Assert.True(viewModel.DeleteCommand.CanExecute(null));

        viewModel.IsServiceConnected = false;
        Assert.False(viewModel.DeleteCommand.CanExecute(null));

        viewModel.IsServiceConnected = true;
        viewModel.AddNewCommand.Execute(null);
        Assert.False(viewModel.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteSelectedProfileAsync_RemovesProfile_AndSelectsRemainingActiveProfile()
    {
        using var client = new ServiceClient();
        var commands = new List<string>();
        var viewModel = new ProfileViewModel(
            client,
            confirmDelete: (_, _) => true,
            sendCommandAsync: (type, payload, cancellationToken) =>
            {
                commands.Add(type);
                return SuccessfulCommandAsync(type, payload, cancellationToken);
            })
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

        await viewModel.DeleteSelectedProfileAsync();

        Assert.Contains("DeleteProfile", commands);
        Assert.Single(viewModel.AvailableProfiles);
        Assert.Equal(primaryId, viewModel.SelectedProfile?.Id);
        Assert.Equal("Primary", viewModel.Name);
        Assert.Equal("Active profile: Primary", viewModel.ActiveProfileSummary);
        Assert.False(viewModel.ShowActiveProfileSubscriptionState);
        Assert.Equal("Deleted \u2713", viewModel.SaveStatus);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.True(viewModel.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteSelectedProfileAsync_WhenDeletingLastProfile_ClearsToEmptyState()
    {
        using var client = new ServiceClient();
        var viewModel = new ProfileViewModel(
            client,
            confirmDelete: (_, _) => true,
            sendCommandAsync: SuccessfulCommandAsync)
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

        await viewModel.DeleteSelectedProfileAsync();

        Assert.False(viewModel.ShowProfileSelector);
        Assert.Null(viewModel.SelectedProfile);
        Assert.Equal("None selected", viewModel.ActiveProfileDisplayName);
        Assert.Equal(string.Empty, viewModel.Name);
        Assert.Equal(string.Empty, viewModel.ServerAddress);
        Assert.Equal(string.Empty, viewModel.UserId);
        Assert.False(viewModel.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportFromUrlAsync_ImportsProfile_SavesIt_AndSelectsIt()
    {
        using var client = new ServiceClient();
        var commands = new List<string>();
        var importedProfile = new VlessProfile
        {
            Id = Guid.NewGuid(),
            Name = "Imported",
            ServerAddress = "imported.example.com",
            ServerPort = 443,
            UserId = "33333333-3333-3333-3333-333333333333",
            Network = "tcp",
            Security = "tls",
            Tls = new TlsOptions
            {
                Sni = "imported.example.com",
                AllowInsecure = false,
                Fingerprint = "chrome"
            }
        };
        var viewModel = new ProfileViewModel(
            client,
            profileImportService: new ProfileImportTestService((url, cancellationToken) =>
            {
                Assert.Equal("https://example.com/profile.txt", url);
                return Task.FromResult(new ProfileImportResult([importedProfile], 0));
            }),
            sendCommandAsync: (type, payload, cancellationToken) =>
            {
                commands.Add(type);
                return SuccessfulCommandAsync(type, payload, cancellationToken);
            })
        {
            IsServiceConnected = true,
            ImportUrl = "https://example.com/profile.txt"
        };

        await viewModel.ImportFromUrlAsync();

        Assert.Contains("UpsertProfile", commands);
        Assert.False(viewModel.IsCreatingNewProfile);
        Assert.Equal(importedProfile.Id, viewModel.SelectedProfile?.Id);
        Assert.Equal("Imported", viewModel.Name);
        Assert.Equal("imported.example.com", viewModel.ServerAddress);
        Assert.Equal("Imported \"Imported\".", viewModel.ImportStatus);
        Assert.Equal(string.Empty, viewModel.ImportUrl);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportFromUrlAsync_WithDirectVlessUri_ImportsProfile_SavesIt_AndSelectsIt()
    {
        using var client = new ServiceClient();
        var commands = new List<string>();
        var directUri = "vless://33333333-3333-3333-3333-333333333333@direct.example.com:8443?security=reality&sni=cdn.example.com&fp=chrome&pbk=public-key&sid=short-id&type=grpc#Direct%20VLESS";
        var importedProfile = new VlessProfile
        {
            Id = Guid.NewGuid(),
            Name = "Direct VLESS",
            ServerAddress = "direct.example.com",
            ServerPort = 8443,
            UserId = "33333333-3333-3333-3333-333333333333",
            Network = "grpc",
            Security = "reality",
            Tls = new TlsOptions
            {
                Sni = "cdn.example.com",
                AllowInsecure = false,
                Fingerprint = "chrome",
                RealityPublicKey = "public-key",
                RealityShortId = "short-id"
            }
        };
        var viewModel = new ProfileViewModel(
            client,
            profileImportService: new ProfileImportTestService((url, cancellationToken) =>
            {
                Assert.Equal(directUri, url);
                return Task.FromResult(new ProfileImportResult([importedProfile], 0));
            }),
            sendCommandAsync: (type, payload, cancellationToken) =>
            {
                commands.Add(type);
                return SuccessfulCommandAsync(type, payload, cancellationToken);
            })
        {
            IsServiceConnected = true,
            ImportUrl = directUri
        };

        await viewModel.ImportFromUrlAsync();

        Assert.Contains("UpsertProfile", commands);
        Assert.False(viewModel.IsCreatingNewProfile);
        Assert.Equal(importedProfile.Id, viewModel.SelectedProfile?.Id);
        Assert.Equal("Direct VLESS", viewModel.Name);
        Assert.Equal("direct.example.com", viewModel.ServerAddress);
        Assert.Equal("Imported \"Direct VLESS\".", viewModel.ImportStatus);
        Assert.Equal(string.Empty, viewModel.ImportUrl);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportFromUrlAsync_WithSubscriptionUrl_ImportsMultipleProfiles_AndShowsSummary()
    {
        using var client = new ServiceClient();
        var commands = new List<string>();
        var importedProfiles = new[]
        {
            new VlessProfile
            {
                Id = Guid.NewGuid(),
                Name = "Alpha",
                ServerAddress = "alpha.example.com",
                ServerPort = 443,
                UserId = "11111111-1111-1111-1111-111111111111",
                Network = "tcp",
                Security = "tls",
                Tls = new TlsOptions
                {
                    Sni = "alpha.example.com",
                    AllowInsecure = false,
                    Fingerprint = "chrome"
                },
                SubscriptionSourceUrl = "https://example.com/subscription.txt",
                SubscriptionProfileKey = "11111111-1111-1111-1111-111111111111|alpha.example.com|443|tcp"
            },
            new VlessProfile
            {
                Id = Guid.NewGuid(),
                Name = "Beta",
                ServerAddress = "beta.example.com",
                ServerPort = 8443,
                UserId = "22222222-2222-2222-2222-222222222222",
                Network = "grpc",
                Security = "reality",
                Tls = new TlsOptions
                {
                    Sni = "beta.example.com",
                    AllowInsecure = false,
                    Fingerprint = "chrome",
                    RealityPublicKey = "public-key",
                    RealityShortId = "short-id"
                },
                SubscriptionSourceUrl = "https://example.com/subscription.txt",
                SubscriptionProfileKey = "22222222-2222-2222-2222-222222222222|beta.example.com|8443|grpc"
            }
        };
        var viewModel = new ProfileViewModel(
            client,
            profileImportService: new ProfileImportTestService((url, cancellationToken) =>
            {
                Assert.Equal("https://example.com/subscription.txt", url);
                return Task.FromResult(new ProfileImportResult(importedProfiles, 1));
            }),
            sendCommandAsync: (type, payload, cancellationToken) =>
            {
                commands.Add(type);
                return SuccessfulCommandAsync(type, payload, cancellationToken);
            })
        {
            IsServiceConnected = true,
            ImportUrl = "https://example.com/subscription.txt"
        };

        await viewModel.ImportFromUrlAsync();

        Assert.Equal(2, commands.Count(command => command == "UpsertProfile"));
        Assert.False(viewModel.IsCreatingNewProfile);
        Assert.Equal(importedProfiles[0].Id, viewModel.SelectedProfile?.Id);
        Assert.Equal("Alpha", viewModel.Name);
        Assert.Equal("alpha.example.com", viewModel.ServerAddress);
        Assert.Equal("Imported 2 profiles from subscription; skipped 1 unsupported entry.", viewModel.ImportStatus);
        Assert.Contains(viewModel.AvailableProfiles, option => option.DisplayName == "Alpha (Subscription)");
        Assert.Contains(viewModel.AvailableProfiles, option => option.DisplayName == "Beta (Subscription)");
        Assert.Equal(string.Empty, viewModel.ImportUrl);
        Assert.True(viewModel.HasSubscriptionSource);
        Assert.Equal("Present in subscription", viewModel.SubscriptionStateText);
        Assert.Equal("https://example.com/subscription.txt", viewModel.SubscriptionSourceUrl);
        Assert.Equal(string.Empty, viewModel.SubscriptionUpdateSummary);
        Assert.False(viewModel.ShowActiveProfileSubscriptionState);
        Assert.Equal(string.Empty, viewModel.ActiveProfileSubscriptionStateText);
        Assert.Equal("Active profile: None selected", viewModel.ActiveProfileSummary);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_UpdatesMatchingProfiles_AndAddsNewProfiles()
    {
        using var client = new ServiceClient();
        var commands = new List<string>();
        var sourceUrl = "https://example.com/subscription.txt";
        var alphaId = Guid.NewGuid();
        var alphaKey = "11111111-1111-1111-1111-111111111111|alpha.example.com|443|tcp";

        var viewModel = new ProfileViewModel(
            client,
            profileImportService: new ProfileImportTestService((url, cancellationToken) =>
            {
                Assert.Equal(sourceUrl, url);
                return Task.FromResult(new ProfileImportResult(
                [
                    new VlessProfile
                    {
                        Id = Guid.NewGuid(),
                        Name = "Alpha Updated",
                        ServerAddress = "alpha.example.com",
                        ServerPort = 443,
                        UserId = "11111111-1111-1111-1111-111111111111",
                        Network = "tcp",
                        Security = "tls",
                        Tls = new TlsOptions
                        {
                            Sni = "alpha-new.example.com",
                            AllowInsecure = false,
                            Fingerprint = "firefox"
                        },
                        SubscriptionSourceUrl = sourceUrl,
                        SubscriptionProfileKey = alphaKey
                    },
                    new VlessProfile
                    {
                        Id = Guid.NewGuid(),
                        Name = "Beta",
                        ServerAddress = "beta.example.com",
                        ServerPort = 8443,
                        UserId = "22222222-2222-2222-2222-222222222222",
                        Network = "grpc",
                        Security = "reality",
                        Tls = new TlsOptions
                        {
                            Sni = "beta.example.com",
                            AllowInsecure = false,
                            Fingerprint = "chrome",
                            RealityPublicKey = "public-key",
                            RealityShortId = "short-id"
                        },
                        SubscriptionSourceUrl = sourceUrl,
                        SubscriptionProfileKey = "22222222-2222-2222-2222-222222222222|beta.example.com|8443|grpc"
                    }
                ], 1));
            }),
            sendCommandAsync: (type, payload, cancellationToken) =>
            {
                commands.Add(type);
                return SuccessfulCommandAsync(type, payload, cancellationToken);
            })
        {
            IsServiceConnected = true
        };

        viewModel.LoadProfile(
        [
            new VlessProfile
            {
                Id = alphaId,
                Name = "Alpha",
                ServerAddress = "alpha.example.com",
                ServerPort = 443,
                UserId = "11111111-1111-1111-1111-111111111111",
                Network = "tcp",
                Security = "tls",
                Tls = new TlsOptions
                {
                    Sni = "alpha.example.com",
                    AllowInsecure = false,
                    Fingerprint = "chrome"
                },
                SubscriptionSourceUrl = sourceUrl,
                SubscriptionProfileKey = alphaKey,
                IsActive = true
            }
        ], alphaId);

        await viewModel.UpdateSubscriptionAsync();

        Assert.Equal(2, commands.Count(command => command == "UpsertProfile"));
        Assert.Equal(alphaId, viewModel.SelectedProfile?.Id);
        Assert.Equal("Alpha Updated", viewModel.Name);
        Assert.Equal("alpha-new.example.com", viewModel.Sni);
        Assert.Equal("Subscription updated: 1 updated, 1 added, 1 skipped.", viewModel.ImportStatus);
        Assert.Contains(viewModel.AvailableProfiles, option => option.DisplayName == "Alpha Updated (Active, Subscription)");
        Assert.Equal(2, viewModel.AvailableProfiles.Count);
        Assert.False(viewModel.SubscriptionMissingFromSource);
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_MarksMissingProfilesAsMissingFromSource_AndKeepsThem()
    {
        using var client = new ServiceClient();
        var commands = new List<string>();
        var sourceUrl = "https://example.com/subscription.txt";
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        var alphaKey = "11111111-1111-1111-1111-111111111111|alpha.example.com|443|tcp";
        var betaKey = "22222222-2222-2222-2222-222222222222|beta.example.com|8443|grpc";

        var viewModel = new ProfileViewModel(
            client,
            profileImportService: new ProfileImportTestService((url, cancellationToken) =>
            {
                Assert.Equal(sourceUrl, url);
                return Task.FromResult(new ProfileImportResult(
                [
                    new VlessProfile
                    {
                        Id = Guid.NewGuid(),
                        Name = "Alpha Updated",
                        ServerAddress = "alpha.example.com",
                        ServerPort = 443,
                        UserId = "11111111-1111-1111-1111-111111111111",
                        Network = "tcp",
                        Security = "tls",
                        Tls = new TlsOptions
                        {
                            Sni = "alpha-updated.example.com",
                            AllowInsecure = false,
                            Fingerprint = "firefox"
                        },
                        SubscriptionSourceUrl = sourceUrl,
                        SubscriptionProfileKey = alphaKey
                    }
                ], 0));
            }),
            sendCommandAsync: (type, payload, cancellationToken) =>
            {
                commands.Add(type);
                return SuccessfulCommandAsync(type, payload, cancellationToken);
            })
        {
            IsServiceConnected = true
        };

        viewModel.LoadProfile(
        [
            new VlessProfile
            {
                Id = alphaId,
                Name = "Alpha",
                ServerAddress = "alpha.example.com",
                ServerPort = 443,
                UserId = "11111111-1111-1111-1111-111111111111",
                Network = "tcp",
                Security = "tls",
                Tls = new TlsOptions
                {
                    Sni = "alpha.example.com",
                    AllowInsecure = false,
                    Fingerprint = "chrome"
                },
                SubscriptionSourceUrl = sourceUrl,
                SubscriptionProfileKey = alphaKey,
                IsActive = true
            },
            new VlessProfile
            {
                Id = betaId,
                Name = "Beta",
                ServerAddress = "beta.example.com",
                ServerPort = 8443,
                UserId = "22222222-2222-2222-2222-222222222222",
                Network = "grpc",
                Security = "reality",
                Tls = new TlsOptions
                {
                    Sni = "beta.example.com",
                    AllowInsecure = false,
                    Fingerprint = "chrome",
                    RealityPublicKey = "public-key",
                    RealityShortId = "short-id"
                },
                SubscriptionSourceUrl = sourceUrl,
                SubscriptionProfileKey = betaKey
            }
        ], betaId);

        await viewModel.UpdateSubscriptionAsync();

        Assert.Equal(2, commands.Count(command => command == "UpsertProfile"));
        Assert.Equal(betaId, viewModel.SelectedProfile?.Id);
        Assert.Equal("Beta", viewModel.Name);
        Assert.True(viewModel.SubscriptionMissingFromSource);
        Assert.Equal("Missing from subscription", viewModel.SubscriptionStateText);
        Assert.Equal("No longer present in the latest subscription update. Kept locally until you remove it.", viewModel.SubscriptionUpdateSummary);
        Assert.Equal("Subscription updated: 1 updated, 1 missing from source.", viewModel.ImportStatus);
        Assert.True(viewModel.ShowActiveProfileSubscriptionState);
        Assert.Equal("Missing from subscription", viewModel.ActiveProfileSubscriptionStateText);
        Assert.Equal("Active profile: Beta (Missing from subscription)", viewModel.ActiveProfileSummary);
        Assert.Contains(viewModel.AvailableProfiles, option => option.DisplayName == "Beta (Active, Subscription, Missing from source)");
        Assert.Contains(viewModel.AvailableProfiles, option => option.DisplayName == "Alpha Updated (Subscription)");
    }

    [Fact]
    public void UpdateSubscriptionAsync_IsUnavailableWithoutSavedSubscriptionSource()
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

        Assert.False(viewModel.HasSubscriptionSource);
        Assert.False(viewModel.UpdateSubscriptionCommand.CanExecute(null));
    }

    [Fact]
    public void CleanupMissingSubscriptionProfileCommand_IsAvailableOnlyForMissingFromSourceProfiles()
    {
        using var client = new ServiceClient();
        var sourceUrl = "https://example.com/subscription.txt";
        var staleId = Guid.NewGuid();
        var healthyId = Guid.NewGuid();
        var viewModel = new ProfileViewModel(client)
        {
            IsServiceConnected = true
        };

        viewModel.LoadProfile(
        [
            new VlessProfile
            {
                Id = healthyId,
                Name = "Healthy",
                ServerAddress = "healthy.example.com",
                ServerPort = 443,
                UserId = "11111111-1111-1111-1111-111111111111",
                Network = "tcp",
                Security = "tls",
                SubscriptionSourceUrl = sourceUrl,
                SubscriptionProfileKey = "healthy-key",
                IsActive = true
            },
            new VlessProfile
            {
                Id = staleId,
                Name = "Stale",
                ServerAddress = "stale.example.com",
                ServerPort = 8443,
                UserId = "22222222-2222-2222-2222-222222222222",
                Network = "grpc",
                Security = "reality",
                SubscriptionSourceUrl = sourceUrl,
                SubscriptionProfileKey = "stale-key",
                SubscriptionMissingFromSource = true
            }
        ], healthyId);

        Assert.False(viewModel.ShowMissingSubscriptionCleanupAction);
        Assert.False(viewModel.CleanupMissingSubscriptionProfileCommand.CanExecute(null));

        viewModel.SelectedProfile = viewModel.AvailableProfiles.Single(option => option.Id == staleId);

        Assert.True(viewModel.SubscriptionMissingFromSource);
        Assert.True(viewModel.ShowMissingSubscriptionCleanupAction);
        Assert.Equal("Remove this stale subscription profile locally if you no longer need it.", viewModel.MissingSubscriptionCleanupSummary);
        Assert.True(viewModel.CleanupMissingSubscriptionProfileCommand.CanExecute(null));

        viewModel.AddNewCommand.Execute(null);

        Assert.False(viewModel.ShowMissingSubscriptionCleanupAction);
        Assert.False(viewModel.CleanupMissingSubscriptionProfileCommand.CanExecute(null));
    }

    [Fact]
    public async Task CleanupMissingSubscriptionProfileAsync_WhenConfirmed_RemovesProfile()
    {
        using var client = new ServiceClient();
        var commands = new List<string>();
        var sourceUrl = "https://example.com/subscription.txt";
        var staleId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var viewModel = new ProfileViewModel(
            client,
            confirmDelete: (message, title) =>
            {
                Assert.Equal("Remove Stale Subscription Profile", title);
                Assert.Equal("Profile \"Stale\" is no longer present in its source subscription. Remove it locally?", message);
                return true;
            },
            sendCommandAsync: (type, payload, cancellationToken) =>
            {
                commands.Add(type);
                return SuccessfulCommandAsync(type, payload, cancellationToken);
            })
        {
            IsServiceConnected = true
        };

        viewModel.LoadProfile(
        [
            new VlessProfile
            {
                Id = activeId,
                Name = "Active",
                ServerAddress = "active.example.com",
                ServerPort = 443,
                UserId = "11111111-1111-1111-1111-111111111111",
                Network = "tcp",
                Security = "tls",
                SubscriptionSourceUrl = sourceUrl,
                SubscriptionProfileKey = "active-key",
                IsActive = true
            },
            new VlessProfile
            {
                Id = staleId,
                Name = "Stale",
                ServerAddress = "stale.example.com",
                ServerPort = 8443,
                UserId = "22222222-2222-2222-2222-222222222222",
                Network = "grpc",
                Security = "reality",
                SubscriptionSourceUrl = sourceUrl,
                SubscriptionProfileKey = "stale-key",
                SubscriptionMissingFromSource = true
            }
        ], staleId);

        await viewModel.CleanupMissingSubscriptionProfileAsync();

        Assert.Contains("DeleteProfile", commands);
        Assert.Single(viewModel.AvailableProfiles);
        Assert.Equal(activeId, viewModel.SelectedProfile?.Id);
        Assert.Equal("Removed stale profile \u2713", viewModel.SaveStatus);
    }

    [Fact]
    public async Task CleanupMissingSubscriptionProfileAsync_WhenCanceled_DoesNothing()
    {
        using var client = new ServiceClient();
        var commands = new List<string>();
        var sourceUrl = "https://example.com/subscription.txt";
        var staleId = Guid.NewGuid();
        var viewModel = new ProfileViewModel(
            client,
            confirmDelete: (message, title) => false,
            sendCommandAsync: (type, payload, cancellationToken) =>
            {
                commands.Add(type);
                return SuccessfulCommandAsync(type, payload, cancellationToken);
            })
        {
            IsServiceConnected = true
        };

        viewModel.LoadProfile(
        [
            new VlessProfile
            {
                Id = staleId,
                Name = "Stale",
                ServerAddress = "stale.example.com",
                ServerPort = 8443,
                UserId = "22222222-2222-2222-2222-222222222222",
                Network = "grpc",
                Security = "reality",
                SubscriptionSourceUrl = sourceUrl,
                SubscriptionProfileKey = "stale-key",
                SubscriptionMissingFromSource = true,
                IsActive = true
            }
        ], staleId);

        await viewModel.CleanupMissingSubscriptionProfileAsync();

        Assert.Empty(commands);
        Assert.Single(viewModel.AvailableProfiles);
        Assert.Equal(staleId, viewModel.SelectedProfile?.Id);
        Assert.Equal(string.Empty, viewModel.SaveStatus);
    }
}
