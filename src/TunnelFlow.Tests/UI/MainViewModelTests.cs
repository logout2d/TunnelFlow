using System.Text.Json;
using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.Tests.UI;

public class MainViewModelTests
{
    private sealed class FakeServiceControlManager : IServiceControlManager
    {
        public bool IsInstalled { get; set; } = true;
        public Func<string, Task>? OnActionAsync { get; set; }

        public int InstallCalls { get; private set; }

        public int RepairCalls { get; private set; }

        public int UninstallCalls { get; private set; }

        public int StartCalls { get; private set; }

        public int RestartCalls { get; private set; }

        public Exception? Failure { get; set; }

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken) =>
            Task.FromResult(IsInstalled);

        public Task InstallAsync(CancellationToken cancellationToken)
        {
            InstallCalls++;
            return OnActionAsync is not null
                ? OnActionAsync("install")
                : Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task RepairAsync(CancellationToken cancellationToken)
        {
            RepairCalls++;
            return OnActionAsync is not null
                ? OnActionAsync("repair")
                : Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task UninstallAsync(CancellationToken cancellationToken)
        {
            UninstallCalls++;
            return OnActionAsync is not null
                ? OnActionAsync("uninstall")
                : Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            return OnActionAsync is not null
                ? OnActionAsync("start-service")
                : Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task RestartAsync(CancellationToken cancellationToken)
        {
            RestartCalls++;
            return OnActionAsync is not null
                ? OnActionAsync("restart-service")
                : Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    [Fact]
    public void ApplyStatusPayload_UpdatesTunOrientedStatusSummary()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);

        viewModel.IsConnected = true;
        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = true,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true,
            ActiveProfileId = Guid.NewGuid(),
            ActiveProfileName = "Primary",
            ProxyRuleCount = 2,
            DirectRuleCount = 1,
            BlockRuleCount = 3,
            RuntimeWarning = RuntimeWarningEvidence.AuthenticationFailure
        });

        Assert.True(viewModel.CaptureRunning);
        Assert.Equal("Service: On", viewModel.ServiceConnectionSummary);
        Assert.Equal("Running", viewModel.SingBoxStatus);
        Assert.Equal(TunnelStatusMode.Tun, viewModel.SelectedMode);
        Assert.True(viewModel.SingBoxRunning);
        Assert.True(viewModel.TunnelInterfaceUp);
        Assert.Equal("Primary", viewModel.ActiveProfileName);
        Assert.Equal("TUN", viewModel.ModeSummary);
        Assert.Equal("Running", viewModel.EngineStatusSummary);
        Assert.Equal("Up", viewModel.TunnelStatusSummary);
        Assert.Equal("Proxy 2  Direct 1  Block 3", viewModel.RuleCountsSummary);
        Assert.True(viewModel.ShowRuntimeWarning);
        Assert.Equal("Authentication failed", viewModel.RuntimeWarningSummary);
        Assert.False(viewModel.AppRules.IsEditingEnabled);
        Assert.False(viewModel.Profile.IsEditingEnabled);
    }

    [Fact]
    public void ApplyStatePayload_UsesFallbackProfileTextAndLegacyTunnelSummary()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);

        viewModel.IsConnected = false;
        viewModel.ApplyStatePayload(new StatePayload
        {
            Rules = Array.Empty<AppRule>(),
            Profiles = Array.Empty<VlessProfile>(),
            ActiveProfileId = null,
            ActiveProfileName = null,
            CaptureRunning = false,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Legacy,
            SingBoxRunning = false,
            TunnelInterfaceUp = false,
            ProxyRuleCount = 0,
            DirectRuleCount = 0,
            BlockRuleCount = 0
        });

        Assert.Equal(TunnelStatusMode.Legacy, viewModel.SelectedMode);
        Assert.Equal("Service: Off", viewModel.ServiceConnectionSummary);
        Assert.Equal("Legacy", viewModel.ModeSummary);
        Assert.Equal("Unavailable", viewModel.EngineStatusSummary);
        Assert.Equal("Unavailable", viewModel.TunnelStatusSummary);
        Assert.Equal("None selected", viewModel.ActiveProfileName);
        Assert.Equal("Proxy 0  Direct 0  Block 0", viewModel.RuleCountsSummary);
        Assert.False(viewModel.ShowRuntimeWarning);
        Assert.True(viewModel.AppRules.IsEditingEnabled);
        Assert.True(viewModel.Profile.IsEditingEnabled);
    }

    [Fact]
    public void ApplyOfflineConfigSnapshot_LoadsSavedConfigurationWhileServiceIsUnavailable()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);
        var activeProfileId = Guid.NewGuid();

        viewModel.IsConnected = false;
        viewModel.ApplyOfflineConfigSnapshot(new LocalConfigSnapshot
        {
            UseTunMode = true,
            ActiveProfileId = activeProfileId,
            Rules =
            [
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Floorp.exe",
                    DisplayName = "Floorp",
                    Mode = RuleMode.Proxy,
                    IsEnabled = true
                },
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Discord.exe",
                    DisplayName = "Discord",
                    Mode = RuleMode.Direct,
                    IsEnabled = true
                },
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Game.exe",
                    DisplayName = "Game",
                    Mode = RuleMode.Block,
                    IsEnabled = true
                }
            ],
            Profiles =
            [
                new VlessProfile
                {
                    Id = activeProfileId,
                    Name = "Offline Tun",
                    ServerAddress = "vpn.example.com",
                    ServerPort = 443,
                    UserId = "11111111-1111-1111-1111-111111111111",
                    Network = "tcp",
                    Security = "tls"
                }
            ]
        });

        Assert.Equal(TunnelStatusMode.Tun, viewModel.SelectedMode);
        Assert.Equal("Offline Tun", viewModel.ActiveProfileName);
        Assert.Equal("TUN", viewModel.ModeSummary);
        Assert.Equal("Unavailable", viewModel.EngineStatusSummary);
        Assert.Equal("Unavailable", viewModel.TunnelStatusSummary);
        Assert.Equal("Proxy 1  Direct 1  Block 1", viewModel.RuleCountsSummary);
        Assert.False(viewModel.ShowRuntimeWarning);
        Assert.Equal("Offline Tun", viewModel.Profile.Name);
        Assert.Equal(3, viewModel.AppRules.Rules.Count);
    }

    [Fact]
    public void ApplyStatePayload_WhenConnectionProblemExists_ShowsFriendlyWarning()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);

        viewModel.IsConnected = true;
        viewModel.ApplyStatePayload(new StatePayload
        {
            Rules = Array.Empty<AppRule>(),
            Profiles = Array.Empty<VlessProfile>(),
            CaptureRunning = true,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true,
            ProxyRuleCount = 0,
            DirectRuleCount = 0,
            BlockRuleCount = 0,
            RuntimeWarning = RuntimeWarningEvidence.ConnectionProblem
        });

        Assert.True(viewModel.ShowRuntimeWarning);
        Assert.Equal("Connection problem", viewModel.RuntimeWarningSummary);
    }

    [Fact]
    public void ApplyStatePayload_WhenNewSnapshotHasNoWarning_ClearsPreviousWarning()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);

        viewModel.IsConnected = true;
        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = true,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true,
            RuntimeWarning = RuntimeWarningEvidence.AuthenticationFailure
        });

        Assert.True(viewModel.ShowRuntimeWarning);

        viewModel.ApplyStatePayload(new StatePayload
        {
            Rules = Array.Empty<AppRule>(),
            Profiles = Array.Empty<VlessProfile>(),
            CaptureRunning = false,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = false,
            TunnelInterfaceUp = false,
            ProxyRuleCount = 0,
            DirectRuleCount = 0,
            BlockRuleCount = 0,
            RuntimeWarning = RuntimeWarningEvidence.None
        });

        Assert.False(viewModel.ShowRuntimeWarning);
        Assert.Equal(string.Empty, viewModel.RuntimeWarningSummary);
    }

    [Fact]
    public async Task RefreshServiceInstallationStateAsync_WhenServiceIsNotInstalled_ShowsInstallAndHidesUninstall()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager { IsInstalled = false };
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager);

        viewModel.IsConnected = false;
        await viewModel.RefreshServiceInstallationStateAsync();

        Assert.False(viewModel.IsServiceInstalled);
        Assert.Equal("Install Service", viewModel.ServiceActionLabel);
        Assert.False(viewModel.ShowUninstallServiceAction);
        Assert.Equal("Service not installed", viewModel.ConnectionStatus);
    }

    [Fact]
    public async Task RefreshServiceInstallationStateAsync_WhenServiceIsInstalled_ShowsRepairAndShowsUninstall()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager { IsInstalled = true };
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager);

        viewModel.IsConnected = false;
        await viewModel.RefreshServiceInstallationStateAsync();

        Assert.True(viewModel.IsServiceInstalled);
        Assert.Equal("Repair Service", viewModel.ServiceActionLabel);
        Assert.True(viewModel.ShowUninstallServiceAction);
        Assert.Equal("Reconnecting...", viewModel.ConnectionStatus);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenDisconnected_RepairsService()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager);

        viewModel.IsConnected = false;
        viewModel.IsServiceInstalled = true;
        await viewModel.RequestServiceActionAsync();

        Assert.Equal(0, serviceManager.InstallCalls);
        Assert.Equal(1, serviceManager.RepairCalls);
        Assert.Equal(0, serviceManager.UninstallCalls);
        Assert.Equal(0, serviceManager.StartCalls);
        Assert.Equal(0, serviceManager.RestartCalls);
        Assert.Equal(ServiceActionKind.Repair, viewModel.PendingServiceAction);
        Assert.Equal("Repairing Service...", viewModel.ServiceActionLabel);
        Assert.Equal("Waiting for service connection...", viewModel.ServiceActionStatus);
        Assert.True(viewModel.ShowServiceActionStatus);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenTunnelIsRunning_DoesNotAllowRepair()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager)
        {
            IsConnected = false,
            IsServiceInstalled = true
        };

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true
        });

        Assert.False(viewModel.ManageServiceCommand.CanExecute(null));

        await viewModel.RequestServiceActionAsync();

        Assert.Equal(0, serviceManager.RepairCalls);
        Assert.Equal(ServiceActionKind.None, viewModel.PendingServiceAction);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenTunnelIsRunning_DoesNotAllowRestart()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager)
        {
            IsConnected = true,
            IsServiceInstalled = true
        };

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true
        });

        Assert.False(viewModel.ManageServiceCommand.CanExecute(null));

        await viewModel.RequestServiceActionAsync();

        Assert.Equal(0, serviceManager.RestartCalls);
        Assert.Equal(ServiceActionKind.None, viewModel.PendingServiceAction);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenServiceIsNotInstalled_InstallsService()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager);

        viewModel.IsConnected = false;
        viewModel.IsServiceInstalled = false;
        await viewModel.RequestServiceActionAsync();

        Assert.Equal(1, serviceManager.InstallCalls);
        Assert.Equal(0, serviceManager.RepairCalls);
        Assert.Equal(0, serviceManager.UninstallCalls);
        Assert.Equal(0, serviceManager.StartCalls);
        Assert.Equal(0, serviceManager.RestartCalls);
        Assert.Equal(ServiceActionKind.Install, viewModel.PendingServiceAction);
        Assert.Equal("Installing Service...", viewModel.ServiceActionLabel);
        Assert.Equal("Waiting for service connection...", viewModel.ServiceActionStatus);
        Assert.True(viewModel.IsServiceInstalled);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenConnected_RestartsService()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager);

        viewModel.IsConnected = true;
        await viewModel.RequestServiceActionAsync();

        Assert.Equal(0, serviceManager.InstallCalls);
        Assert.Equal(0, serviceManager.RepairCalls);
        Assert.Equal(0, serviceManager.UninstallCalls);
        Assert.Equal(0, serviceManager.StartCalls);
        Assert.Equal(1, serviceManager.RestartCalls);
        Assert.Equal(ServiceActionKind.Restart, viewModel.PendingServiceAction);
        Assert.Equal("Restarting Service...", viewModel.ServiceActionLabel);
        Assert.Equal("Restarting the Windows service...", viewModel.ServiceActionStatus);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenReconnectWins_DoesNotRestoreWaitingStatus()
    {
        using var client = new ServiceClient();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MainViewModel? viewModel = null;
        var serviceManager = new FakeServiceControlManager
        {
            OnActionAsync = async verb =>
            {
                Assert.Equal("repair", verb);
                viewModel!.IsConnected = true;
                viewModel.IsServiceInstalled = true;
                viewModel.ConnectionStatus = "Connected";
                viewModel.ServiceActionStatus = string.Empty;
                viewModel.PendingServiceAction = ServiceActionKind.None;
                gate.SetResult();
                await Task.Yield();
            }
        };
        viewModel = new MainViewModel(client, serviceControlManager: serviceManager)
        {
            IsConnected = false,
            IsServiceInstalled = true
        };

        await viewModel.RequestServiceActionAsync();
        await gate.Task;

        Assert.True(viewModel.IsConnected);
        Assert.Equal(ServiceActionKind.None, viewModel.PendingServiceAction);
        Assert.Equal(string.Empty, viewModel.ServiceActionStatus);
        Assert.Equal("Restart Service", viewModel.ServiceActionLabel);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenServiceControlFails_ShowsError()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager
        {
            Failure = new ServiceNotInstalledException("TunnelFlow service is not installed.")
        };
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager);

        viewModel.IsConnected = false;
        await viewModel.RequestServiceActionAsync();

        Assert.Equal(ServiceActionKind.None, viewModel.PendingServiceAction);
        Assert.True(viewModel.ManageServiceCommand.CanExecute(null));
        Assert.Equal("Service not installed", viewModel.ServiceActionStatus);
        Assert.Equal("Install Service", viewModel.ServiceActionLabel);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("TunnelFlow service is not installed."));
    }

    [Fact]
    public async Task RequestUninstallServiceAsync_WhenConfirmed_UninstallsAndTransitionsToNotInstalled()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var confirmations = new List<(string Message, string Title)>();
        var viewModel = new MainViewModel(
            client,
            serviceControlManager: serviceManager,
            confirmServiceAction: (message, title) =>
            {
                confirmations.Add((message, title));
                return true;
            });

        viewModel.IsConnected = true;
        viewModel.IsServiceInstalled = true;

        await viewModel.RequestUninstallServiceAsync();

        Assert.Single(confirmations);
        Assert.Equal("Uninstall Service", confirmations[0].Title);
        Assert.Equal(0, serviceManager.InstallCalls);
        Assert.Equal(0, serviceManager.RepairCalls);
        Assert.Equal(1, serviceManager.UninstallCalls);
        Assert.Equal(0, serviceManager.StartCalls);
        Assert.Equal(0, serviceManager.RestartCalls);
        Assert.Equal(ServiceActionKind.None, viewModel.PendingServiceAction);
        Assert.False(viewModel.IsConnected);
        Assert.False(viewModel.IsServiceInstalled);
        Assert.Equal("Service not installed", viewModel.ServiceActionStatus);
        Assert.Equal("Install Service", viewModel.ServiceActionLabel);
        Assert.False(viewModel.ShowUninstallServiceAction);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("Uninstall service requested"));
    }

    [Fact]
    public async Task RequestUninstallServiceAsync_WhenTunnelIsRunning_DoesNotAllowUninstall()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager)
        {
            IsConnected = true,
            IsServiceInstalled = true
        };

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true
        });

        Assert.True(viewModel.ShowUninstallServiceAction);
        Assert.False(viewModel.UninstallServiceCommand.CanExecute(null));

        await viewModel.RequestUninstallServiceAsync();

        Assert.Equal(0, serviceManager.UninstallCalls);
        Assert.Equal(ServiceActionKind.None, viewModel.PendingServiceAction);
    }

    [Fact]
    public async Task RequestUninstallServiceAsync_WhenCanceled_DoesNothing()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var viewModel = new MainViewModel(
            client,
            serviceControlManager: serviceManager,
            confirmServiceAction: (_, _) => false);

        viewModel.IsConnected = true;
        viewModel.IsServiceInstalled = true;

        await viewModel.RequestUninstallServiceAsync();

        Assert.Equal(0, serviceManager.UninstallCalls);
        Assert.True(viewModel.IsConnected);
        Assert.True(viewModel.IsServiceInstalled);
        Assert.Equal(ServiceActionKind.None, viewModel.PendingServiceAction);
        Assert.False(viewModel.ShowServiceActionStatus);
        Assert.True(viewModel.ShowUninstallServiceAction);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenGenericServiceControlFails_ShowsFriendlyStatus_AndLogsDetails()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager
        {
            Failure = new InvalidOperationException("Raw detailed failure")
        };
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager);

        viewModel.IsConnected = false;
        await viewModel.RequestServiceActionAsync();

        Assert.Equal(ServiceActionKind.None, viewModel.PendingServiceAction);
        Assert.True(viewModel.ManageServiceCommand.CanExecute(null));
        Assert.Equal("Repair failed", viewModel.ServiceActionStatus);
        Assert.DoesNotContain("Raw detailed failure", viewModel.ServiceActionStatus);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("Raw detailed failure"));
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenAccessDenied_ShowsFriendlyStatus_AndLogsDetails()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager
        {
            Failure = new ServiceControlAccessDeniedException("Administrator approval was denied.")
        };
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager)
        {
            IsConnected = false,
            IsServiceInstalled = true
        };

        await viewModel.RequestServiceActionAsync();

        Assert.Equal("Administrator approval required", viewModel.ServiceActionStatus);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("Administrator approval was denied."));
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenTimeout_ShowsFriendlyStatus_AndLogsDetails()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager
        {
            Failure = new ServiceControlTimeoutException("Repair timed out.")
        };
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager)
        {
            IsConnected = false,
            IsServiceInstalled = true
        };

        await viewModel.RequestServiceActionAsync();

        Assert.Equal("Repair timed out", viewModel.ServiceActionStatus);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("Repair timed out."));
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenBootstrapperMissing_ShowsFriendlyStatus_AndLogsDetails()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager
        {
            Failure = new ServiceBootstrapperMissingException("TunnelFlow.Bootstrapper.exe was not found.")
        };
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager)
        {
            IsConnected = false,
            IsServiceInstalled = false
        };

        await viewModel.RequestServiceActionAsync();

        Assert.Equal("Service bootstrapper not available", viewModel.ServiceActionStatus);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("TunnelFlow.Bootstrapper.exe was not found."));
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenInstallFails_ShowsFriendlyStatus_AndLogsDetails()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager
        {
            Failure = new InvalidOperationException("Create service failed.")
        };
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager)
        {
            IsConnected = false,
            IsServiceInstalled = false
        };

        await viewModel.RequestServiceActionAsync();

        Assert.Equal("Install failed", viewModel.ServiceActionStatus);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("Create service failed."));
    }

    [Fact]
    public async Task RequestUninstallServiceAsync_WhenGenericFailure_ShowsFriendlyStatus_AndLogsDetails()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager
        {
            Failure = new InvalidOperationException("Delete failed with raw details.")
        };
        var viewModel = new MainViewModel(
            client,
            serviceControlManager: serviceManager,
            confirmServiceAction: (_, _) => true)
        {
            IsConnected = true,
            IsServiceInstalled = true
        };

        await viewModel.RequestUninstallServiceAsync();

        Assert.Equal("Uninstall failed", viewModel.ServiceActionStatus);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("Delete failed with raw details."));
    }

    [Fact]
    public async Task ShutdownForApplicationExitAsync_WhenLifecycleIsStopped_DisposesClientWithoutStopRequest()
    {
        using var client = new ServiceClient();
        var sendCalls = 0;
        var disposeCalls = 0;
        var viewModel = new MainViewModel(
            client,
            sendCommandAsync: (_, _, _) =>
            {
                sendCalls++;
                return Task.FromResult<JsonElement?>(null);
            },
            disposeClient: () => disposeCalls++);

        viewModel.IsConnected = true;
        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            LifecycleState = TunnelLifecycleState.Stopped,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = false,
            TunnelInterfaceUp = false
        });

        await viewModel.ShutdownForApplicationExitAsync();

        Assert.Equal(0, sendCalls);
        Assert.Equal(1, disposeCalls);
    }

    [Fact]
    public async Task ShutdownForApplicationExitAsync_WhenRunning_StopsThenWaitsForStoppedBeforeDispose()
    {
        using var client = new ServiceClient();
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopReply = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCalls = 0;
        var viewModel = new MainViewModel(
            client,
            sendCommandAsync: (type, _, _) =>
            {
                Assert.Equal("StopCapture", type);
                stopRequested.TrySetResult();
                return stopReply.Task;
            },
            disposeClient: () => disposeCalls++);

        viewModel.IsConnected = true;
        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = true,
            LifecycleState = TunnelLifecycleState.Running,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true
        });

        var shutdownTask = viewModel.ShutdownForApplicationExitAsync();
        await stopRequested.Task;

        Assert.False(shutdownTask.IsCompleted);
        Assert.Equal(0, disposeCalls);

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            LifecycleState = TunnelLifecycleState.Stopped,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = false,
            TunnelInterfaceUp = false
        });

        Assert.False(shutdownTask.IsCompleted);

        stopReply.SetResult(null);
        await shutdownTask;

        Assert.Equal(1, disposeCalls);
        Assert.Contains(viewModel.Log.Lines, line => line.Message == "Stopping tunnel before application exit...");
    }

    [Fact]
    public async Task ShutdownForApplicationExitAsync_WhenStarting_WaitsForRunningThenStops()
    {
        using var client = new ServiceClient();
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopReply = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCalls = 0;
        var stopCalls = 0;
        var viewModel = new MainViewModel(
            client,
            sendCommandAsync: (type, _, _) =>
            {
                stopCalls++;
                Assert.Equal("StopCapture", type);
                stopRequested.TrySetResult();
                return stopReply.Task;
            },
            disposeClient: () => disposeCalls++);

        viewModel.IsConnected = true;
        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            LifecycleState = TunnelLifecycleState.Starting,
            SingBoxStatus = SingBoxStatus.Starting,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = false,
            TunnelInterfaceUp = false
        });

        var shutdownTask = viewModel.ShutdownForApplicationExitAsync();

        Assert.Equal(0, stopCalls);
        Assert.False(shutdownTask.IsCompleted);

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = true,
            LifecycleState = TunnelLifecycleState.Running,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true
        });

        await stopRequested.Task;
        Assert.Equal(1, stopCalls);
        Assert.Equal(0, disposeCalls);

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            LifecycleState = TunnelLifecycleState.Stopped,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = false,
            TunnelInterfaceUp = false
        });

        stopReply.SetResult(null);
        await shutdownTask;

        Assert.Equal(1, disposeCalls);
    }

    [Fact]
    public async Task ShutdownForApplicationExitAsync_WhenCalledTwice_ReusesSingleFlow()
    {
        using var client = new ServiceClient();
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopReply = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCalls = 0;
        var stopCalls = 0;
        var viewModel = new MainViewModel(
            client,
            sendCommandAsync: (type, _, _) =>
            {
                stopCalls++;
                Assert.Equal("StopCapture", type);
                stopRequested.TrySetResult();
                return stopReply.Task;
            },
            disposeClient: () => disposeCalls++);

        viewModel.IsConnected = true;
        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = true,
            LifecycleState = TunnelLifecycleState.Running,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true
        });

        var first = viewModel.ShutdownForApplicationExitAsync();
        var second = viewModel.ShutdownForApplicationExitAsync();

        await stopRequested.Task;

        Assert.Same(first, second);
        Assert.Equal(1, stopCalls);

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            LifecycleState = TunnelLifecycleState.Stopped,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = false,
            TunnelInterfaceUp = false
        });

        stopReply.SetResult(null);
        await Task.WhenAll(first, second);

        Assert.Equal(1, disposeCalls);
    }

    [Fact]
    public async Task ApplyStatusPayload_WhenOwnedTunnelIsRunning_StartsHeartbeatLoop()
    {
        using var client = new ServiceClient();
        var sentCommands = new List<string>();
        var viewModel = new MainViewModel(
            client,
            sendCommandAsync: (type, _, _) =>
            {
                lock (sentCommands)
                {
                    sentCommands.Add(type);
                }

                return Task.FromResult<JsonElement?>(null);
            },
            ownerHeartbeatInterval: TimeSpan.FromMilliseconds(20))
        {
            IsConnected = true
        };

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = true,
            LifecycleState = TunnelLifecycleState.Running,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true,
            ActiveOwnerSessionId = client.SessionId
        });

        await Task.Delay(70);

        lock (sentCommands)
        {
            Assert.Contains("OwnerHeartbeat", sentCommands);
        }

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            LifecycleState = TunnelLifecycleState.Stopped,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = false,
            TunnelInterfaceUp = false,
            ActiveOwnerSessionId = null
        });

        await viewModel.ShutdownForApplicationExitAsync();
    }

    [Fact]
    public async Task ApplyStatePayload_WhenOwnedTunnelReconnects_RestartsHeartbeatLoop()
    {
        using var client = new ServiceClient();
        var sentCommands = new List<string>();
        var viewModel = new MainViewModel(
            client,
            sendCommandAsync: (type, _, _) =>
            {
                lock (sentCommands)
                {
                    sentCommands.Add(type);
                }

                return Task.FromResult<JsonElement?>(null);
            },
            ownerHeartbeatInterval: TimeSpan.FromMilliseconds(20));

        viewModel.IsConnected = true;
        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = true,
            LifecycleState = TunnelLifecycleState.Running,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true,
            ActiveOwnerSessionId = client.SessionId
        });

        await Task.Delay(50);
        viewModel.IsConnected = false;

        int countAfterDisconnect;
        lock (sentCommands)
        {
            countAfterDisconnect = sentCommands.Count(command => command == "OwnerHeartbeat");
        }

        await Task.Delay(50);

        lock (sentCommands)
        {
            Assert.Equal(countAfterDisconnect, sentCommands.Count(command => command == "OwnerHeartbeat"));
        }

        viewModel.IsConnected = true;
        viewModel.ApplyStatePayload(new StatePayload
        {
            Rules = Array.Empty<AppRule>(),
            Profiles = Array.Empty<VlessProfile>(),
            ActiveProfileId = null,
            ActiveProfileName = null,
            ActiveOwnerSessionId = client.SessionId,
            CaptureRunning = true,
            LifecycleState = TunnelLifecycleState.Running,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true,
            ProxyRuleCount = 0,
            DirectRuleCount = 0,
            BlockRuleCount = 0,
            RuntimeWarning = RuntimeWarningEvidence.None
        });

        await Task.Delay(50);

        lock (sentCommands)
        {
            Assert.True(sentCommands.Count(command => command == "OwnerHeartbeat") > countAfterDisconnect);
        }

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            LifecycleState = TunnelLifecycleState.Stopped,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = false,
            TunnelInterfaceUp = false,
            ActiveOwnerSessionId = null
        });

        await viewModel.ShutdownForApplicationExitAsync();
    }

    [Fact]
    public void RemainingNavigationCommands_SwitchBetweenVisibleMainViews()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);

        Assert.Same(viewModel.AppRules, viewModel.CurrentView);

        viewModel.NavigateToProfileCommand.Execute(null);
        Assert.Same(viewModel.Profile, viewModel.CurrentView);

        viewModel.NavigateToLogCommand.Execute(null);
        Assert.Same(viewModel.Log, viewModel.CurrentView);

        viewModel.NavigateToRulesCommand.Execute(null);
        Assert.Same(viewModel.AppRules, viewModel.CurrentView);
    }

    [Fact]
    public void HandleClientDiagnosticMessage_ReconnectNoise_IsSummarizedOnce()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);

        viewModel.HandleClientDiagnosticMessage("Connect failed (retry in 1s): IOException: pipe unavailable");
        viewModel.HandleClientDiagnosticMessage("Connect failed (retry in 2s): IOException: pipe unavailable");
        viewModel.HandleClientDiagnosticMessage("ReadLoop error: IOException: broken pipe");

        var waitingLines = viewModel.Log.Lines.Where(line => line.Message == "Waiting for service...").ToList();

        Assert.Single(waitingLines);
        Assert.DoesNotContain(viewModel.Log.Lines, line => line.Message.Contains("Connect failed"));
        Assert.DoesNotContain(viewModel.Log.Lines, line => line.Message.Contains("ReadLoop error"));
    }

    [Fact]
    public void RecordServiceConnectionTransitions_LogsFriendlyMessages()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);

        viewModel.RecordServiceDisconnectedForUi();
        viewModel.RecordServiceConnectedForUi();

        Assert.Contains(viewModel.Log.Lines, line => line.Message == "Service disconnected");
        Assert.Contains(viewModel.Log.Lines, line => line.Message == "Waiting for service...");
        Assert.Contains(viewModel.Log.Lines, line => line.Message == "Service connected");
    }

    [Fact]
    public void EditingCommands_DisableWhileTunnelIsRunning_AndReEnableWhenStopped()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);
        var activeProfileId = Guid.NewGuid();
        var inactiveProfileId = Guid.NewGuid();

        viewModel.IsConnected = true;
        viewModel.Profile.LoadProfile(
        [
            new VlessProfile
            {
                Id = activeProfileId,
                Name = "Active",
                ServerAddress = "active.example.com",
                ServerPort = 443,
                UserId = "11111111-1111-1111-1111-111111111111",
                Network = "tcp",
                Security = "tls"
            },
            new VlessProfile
            {
                Id = inactiveProfileId,
                Name = "Inactive",
                ServerAddress = "inactive.example.com",
                ServerPort = 443,
                UserId = "22222222-2222-2222-2222-222222222222",
                Network = "tcp",
                Security = "tls"
            }
        ], activeProfileId);
        viewModel.Profile.SelectedProfile = viewModel.Profile.AvailableProfiles.Single(profile => profile.Id == inactiveProfileId);

        viewModel.Profile.ServerAddress = "vpn.example.com";
        viewModel.Profile.UserId = "11111111-1111-1111-1111-111111111111";
        viewModel.Profile.ServerPort = 443;

        Assert.True(viewModel.AppRules.AddRuleCommand.CanExecute(null));
        Assert.True(viewModel.Profile.SaveCommand.CanExecute(null));
        Assert.True(viewModel.Profile.ActivateCommand.CanExecute(null));

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = false,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true,
            ActiveProfileName = "Primary"
        });

        Assert.False(viewModel.AppRules.IsEditingEnabled);
        Assert.False(viewModel.Profile.IsEditingEnabled);
        Assert.False(viewModel.AppRules.AddRuleCommand.CanExecute(null));
        Assert.False(viewModel.Profile.SaveCommand.CanExecute(null));
        Assert.False(viewModel.Profile.ActivateCommand.CanExecute(null));
        Assert.True(viewModel.AppRules.ShowEditHint);
        Assert.True(viewModel.Profile.ShowEditHint);
        Assert.Equal("Stop the tunnel to edit rules.", viewModel.AppRules.EditHintText);
        Assert.Equal("Stop the tunnel to edit profile settings.", viewModel.Profile.EditHintText);

        viewModel.ApplyStatePayload(new StatePayload
        {
            Rules = Array.Empty<AppRule>(),
            Profiles = Array.Empty<VlessProfile>(),
            CaptureRunning = false,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = false,
            TunnelInterfaceUp = false
        });

        viewModel.Profile.ServerAddress = "vpn.example.com";
        viewModel.Profile.UserId = "11111111-1111-1111-1111-111111111111";
        viewModel.Profile.ServerPort = 443;

        Assert.True(viewModel.AppRules.IsEditingEnabled);
        Assert.True(viewModel.Profile.IsEditingEnabled);
        Assert.True(viewModel.AppRules.AddRuleCommand.CanExecute(null));
        Assert.True(viewModel.Profile.SaveCommand.CanExecute(null));
        Assert.True(viewModel.Profile.ActivateCommand.CanExecute(null));
        Assert.False(viewModel.AppRules.ShowEditHint);
        Assert.False(viewModel.Profile.ShowEditHint);
    }
}
