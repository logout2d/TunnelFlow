using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.Tests.UI;

public class MainViewModelTests
{
    private sealed class FakeServiceControlManager : IServiceControlManager
    {
        public int StartCalls { get; private set; }

        public int RestartCalls { get; private set; }

        public Exception? Failure { get; set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task RestartAsync(CancellationToken cancellationToken)
        {
            RestartCalls++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
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
            BlockRuleCount = 3
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
        Assert.False(viewModel.Sessions.IsAvailable);
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
        Assert.True(viewModel.Sessions.IsAvailable);
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
        Assert.Equal("Offline Tun", viewModel.Profile.Name);
        Assert.Equal(3, viewModel.AppRules.Rules.Count);
        Assert.False(viewModel.Sessions.IsAvailable);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenDisconnected_StartsService()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager);

        viewModel.IsConnected = false;
        await viewModel.RequestServiceActionAsync();

        Assert.Equal(1, serviceManager.StartCalls);
        Assert.Equal(0, serviceManager.RestartCalls);
        Assert.Equal(ServiceActionKind.Start, viewModel.PendingServiceAction);
        Assert.Equal("Starting Service...", viewModel.ServiceActionLabel);
        Assert.Equal("Waiting for service connection...", viewModel.ServiceActionStatus);
        Assert.True(viewModel.ShowServiceActionStatus);
    }

    [Fact]
    public async Task RequestServiceActionAsync_WhenConnected_RestartsService()
    {
        using var client = new ServiceClient();
        var serviceManager = new FakeServiceControlManager();
        var viewModel = new MainViewModel(client, serviceControlManager: serviceManager);

        viewModel.IsConnected = true;
        await viewModel.RequestServiceActionAsync();

        Assert.Equal(0, serviceManager.StartCalls);
        Assert.Equal(1, serviceManager.RestartCalls);
        Assert.Equal(ServiceActionKind.Restart, viewModel.PendingServiceAction);
        Assert.Equal("Restarting Service...", viewModel.ServiceActionLabel);
        Assert.Equal("Waiting for service connection...", viewModel.ServiceActionStatus);
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
        Assert.False(viewModel.ManageServiceCommand.CanExecute(null));
        Assert.Equal("Service not installed", viewModel.ServiceActionStatus);
        Assert.Equal("Start Service", viewModel.ServiceActionLabel);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("TunnelFlow service is not installed."));
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
        Assert.Equal("Service action failed", viewModel.ServiceActionStatus);
        Assert.DoesNotContain("Raw detailed failure", viewModel.ServiceActionStatus);
        Assert.Contains(viewModel.Log.Lines, line => line.Message.Contains("Raw detailed failure"));
    }
}
