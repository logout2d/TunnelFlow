using Microsoft.Extensions.Logging.Abstractions;
using TunnelFlow.Service;
using TunnelFlow.Service.Tun;
using TunnelFlow.Core.Models;
using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Service.Configuration;
using TunnelFlow.Service.Ipc;

namespace TunnelFlow.Tests.Service;

public class OrchestratorServiceTests
{
    [Fact]
    public void GetTunOnlyStartBlockReason_WhenTunNotRequested_ReturnsTunOnlyMessage()
    {
        var selection = new TunModeSelectionResult(
            UseTunModeRequested: false,
            TunPrerequisitesSatisfied: true,
            TunActivationSupported: true,
            SelectedMode: TunnelMode.Legacy,
            SelectionReason: "tun-not-requested",
            WintunPath: @"C:\Windows\System32\wintun.dll");

        var reason = OrchestratorService.GetTunOnlyStartBlockReason(selection);

        Assert.Equal("Cannot start: TUN-only runtime requires UseTunMode=true.", reason);
    }

    [Fact]
    public void GetTunOnlyStartBlockReason_WhenTunPrerequisitesMissing_ReturnsSelectionReason()
    {
        var selection = new TunModeSelectionResult(
            UseTunModeRequested: true,
            TunPrerequisitesSatisfied: false,
            TunActivationSupported: true,
            SelectedMode: TunnelMode.Legacy,
            SelectionReason: "wintun-missing",
            WintunPath: @"C:\missing\wintun.dll");

        var reason = OrchestratorService.GetTunOnlyStartBlockReason(selection);

        Assert.Equal("Cannot start: TUN-only runtime prerequisites not met (wintun-missing).", reason);
    }

    [Fact]
    public void GetTunOnlyStartBlockReason_WhenTunIsSelected_ReturnsNull()
    {
        var selection = new TunModeSelectionResult(
            UseTunModeRequested: true,
            TunPrerequisitesSatisfied: true,
            TunActivationSupported: true,
            SelectedMode: TunnelMode.Tun,
            SelectionReason: "tun-selected",
            WintunPath: @"C:\Windows\System32\wintun.dll");

        var reason = OrchestratorService.GetTunOnlyStartBlockReason(selection);

        Assert.Null(reason);
    }

    [Fact]
    public void BuildTunPolicySummaries_MapsProxyDirectAndBlockRules()
    {
        var rules = new[]
        {
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\ProxyMe.exe",
                DisplayName = "ProxyMe",
                Mode = RuleMode.Proxy,
                IsEnabled = true
            },
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\DirectMe.exe",
                DisplayName = "DirectMe",
                Mode = RuleMode.Direct,
                IsEnabled = true
            },
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\BlockMe.exe",
                DisplayName = "BlockMe",
                Mode = RuleMode.Block,
                IsEnabled = true
            },
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\Disabled.exe",
                DisplayName = "Disabled",
                Mode = RuleMode.Proxy,
                IsEnabled = false
            }
        };

        var summaries = OrchestratorService.BuildTunPolicySummaries(rules);

        Assert.Equal(3, summaries.Count);
        Assert.Contains(summaries, summary =>
            summary.AppPath == @"C:\Apps\ProxyMe.exe" &&
            summary.RuleMode == RuleMode.Proxy &&
            summary.MappedAction == "route" &&
            summary.MappedOutbound == "vless-out");
        Assert.Contains(summaries, summary =>
            summary.AppPath == @"C:\Apps\DirectMe.exe" &&
            summary.RuleMode == RuleMode.Direct &&
            summary.MappedAction == "route" &&
            summary.MappedOutbound == "direct");
        Assert.Contains(summaries, summary =>
            summary.AppPath == @"C:\Apps\BlockMe.exe" &&
            summary.RuleMode == RuleMode.Block &&
            summary.MappedAction == "reject" &&
            summary.MappedOutbound is null);
    }

    [Fact]
    public void BuildStatusSummary_TunMode_BuildsTunOrientedRuntimeSnapshot()
    {
        var activeProfileId = Guid.NewGuid();
        var config = new TunnelFlowConfig
        {
            ActiveProfileId = activeProfileId,
            Profiles =
            {
                new VlessProfile
                {
                    Id = activeProfileId,
                    Name = "Primary",
                    ServerAddress = "example.com",
                    ServerPort = 443,
                    UserId = Guid.NewGuid().ToString()
                }
            },
            Rules =
            {
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Proxy.exe",
                    DisplayName = "Proxy",
                    Mode = RuleMode.Proxy,
                    IsEnabled = true
                },
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Direct.exe",
                    DisplayName = "Direct",
                    Mode = RuleMode.Direct,
                    IsEnabled = true
                },
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Block.exe",
                    DisplayName = "Block",
                    Mode = RuleMode.Block,
                    IsEnabled = true
                },
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"",
                    DisplayName = "Ignored",
                    Mode = RuleMode.Proxy,
                    IsEnabled = true
                }
            }
        };

        var summary = OrchestratorService.BuildStatusSummary(
            config,
            TunnelLifecycleState.Running,
            TunnelMode.Tun,
            tunModeActive: true,
            SingBoxStatus.Running,
            RuntimeWarningEvidence.ConnectionProblem);

        Assert.Equal(TunnelStatusMode.Tun, summary.SelectedMode);
        Assert.True(summary.CaptureRunning);
        Assert.Equal(SingBoxStatus.Running, summary.SingBoxStatus);
        Assert.True(summary.SingBoxRunning);
        Assert.True(summary.TunnelInterfaceUp);
        Assert.Equal(activeProfileId, summary.ActiveProfileId);
        Assert.Equal("Primary", summary.ActiveProfileName);
        Assert.Equal(1, summary.ProxyRuleCount);
        Assert.Equal(1, summary.DirectRuleCount);
        Assert.Equal(1, summary.BlockRuleCount);
        Assert.Equal(RuntimeWarningEvidence.ConnectionProblem, summary.RuntimeWarning);
        Assert.Equal(TunnelLifecycleState.Running, summary.LifecycleState);
    }

    [Fact]
    public void BuildStatusSummary_LegacyMode_KeepsLegacySelectionAndInterfaceDown()
    {
        var config = new TunnelFlowConfig
        {
            Rules =
            {
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Disabled.exe",
                    DisplayName = "Disabled",
                    Mode = RuleMode.Proxy,
                    IsEnabled = false
                }
            }
        };

        var summary = OrchestratorService.BuildStatusSummary(
            config,
            TunnelLifecycleState.Stopped,
            TunnelMode.Legacy,
            tunModeActive: false,
            SingBoxStatus.Stopped);

        Assert.Equal(TunnelStatusMode.Legacy, summary.SelectedMode);
        Assert.False(summary.CaptureRunning);
        Assert.Equal(SingBoxStatus.Stopped, summary.SingBoxStatus);
        Assert.False(summary.SingBoxRunning);
        Assert.False(summary.TunnelInterfaceUp);
        Assert.Null(summary.ActiveProfileId);
        Assert.Null(summary.ActiveProfileName);
        Assert.Equal(0, summary.ProxyRuleCount);
        Assert.Equal(0, summary.DirectRuleCount);
        Assert.Equal(0, summary.BlockRuleCount);
        Assert.Equal(RuntimeWarningEvidence.None, summary.RuntimeWarning);
        Assert.Equal(TunnelLifecycleState.Stopped, summary.LifecycleState);
    }

    [Theory]
    [InlineData(TunnelLifecycleState.Stopped, true)]
    [InlineData(TunnelLifecycleState.Starting, false)]
    [InlineData(TunnelLifecycleState.Running, false)]
    [InlineData(TunnelLifecycleState.Stopping, false)]
    public void CanStartCapture_OnlyAllowsStopped(TunnelLifecycleState lifecycleState, bool expected)
    {
        var result = OrchestratorService.CanStartCapture(lifecycleState);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(TunnelLifecycleState.Stopped, false)]
    [InlineData(TunnelLifecycleState.Starting, false)]
    [InlineData(TunnelLifecycleState.Running, true)]
    [InlineData(TunnelLifecycleState.Stopping, false)]
    public void CanStopCapture_OnlyAllowsRunning(TunnelLifecycleState lifecycleState, bool expected)
    {
        var result = OrchestratorService.CanStopCapture(lifecycleState);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("outbound/vless[vless-out]: authentication failed", (int)RuntimeWarningDetail.AuthenticationFailure)]
    [InlineData("dial tcp 1.2.3.4:443: connectex: No route to host", (int)RuntimeWarningDetail.StrongConnectionProblem)]
    [InlineData("connection download closed: wsarecv: An existing connection was forcibly closed by the remote host", (int)RuntimeWarningDetail.WeakConnectionNoise)]
    [InlineData("started at 127.0.0.1", (int)RuntimeWarningDetail.None)]
    public void ClassifyRuntimeWarningDetail_UsesConservativeBuckets(string line, int expectedValue)
    {
        var expected = (RuntimeWarningDetail)expectedValue;

        var detail = OrchestratorService.ClassifyRuntimeWarningDetail(line);

        Assert.Equal(expected, detail);
    }

    [Theory]
    [InlineData((int)RuntimeWarningDetail.AuthenticationFailure, RuntimeWarningEvidence.AuthenticationFailure)]
    [InlineData((int)RuntimeWarningDetail.StrongConnectionProblem, RuntimeWarningEvidence.ConnectionProblem)]
    [InlineData((int)RuntimeWarningDetail.WeakConnectionNoise, RuntimeWarningEvidence.ConnectionProblem)]
    [InlineData((int)RuntimeWarningDetail.None, RuntimeWarningEvidence.None)]
    public void MapRuntimeWarningEvidence_CollapsesDetailedCategories(int detailValue, RuntimeWarningEvidence expected)
    {
        var detail = (RuntimeWarningDetail)detailValue;

        var warning = OrchestratorService.MapRuntimeWarningEvidence(detail);

        Assert.Equal(expected, warning);
    }

    [Fact]
    public void ApplyRuntimeWarningEvidence_SingleWeakLine_DoesNotRaiseWarning()
    {
        var tracker = OrchestratorService.ApplyRuntimeWarningEvidence(
            RuntimeWarningTracker.Empty,
            "connection download closed: wsarecv: An existing connection was forcibly closed by the remote host");

        Assert.Equal(RuntimeWarningEvidence.None, tracker.Warning);
        Assert.Equal(RuntimeWarningStrength.None, tracker.Strength);
        Assert.Equal(1, tracker.WeakConnectionEvidenceCount);
    }

    [Fact]
    public void ApplyRuntimeWarningEvidence_RepeatedWeakLines_RaiseConnectionProblem()
    {
        var tracker = RuntimeWarningTracker.Empty;

        tracker = OrchestratorService.ApplyRuntimeWarningEvidence(
            tracker,
            "connection download closed: wsarecv: An existing connection was forcibly closed by the remote host");
        tracker = OrchestratorService.ApplyRuntimeWarningEvidence(
            tracker,
            "packet upload closed: connection reset by peer");

        Assert.Equal(RuntimeWarningEvidence.ConnectionProblem, tracker.Warning);
        Assert.Equal(RuntimeWarningStrength.Weak, tracker.Strength);
        Assert.Equal(OrchestratorService.WeakConnectionProblemThreshold, tracker.WeakConnectionEvidenceCount);
    }

    [Fact]
    public void ApplyRuntimeWarningEvidence_StrongAuthRemainsStrict()
    {
        var tracker = OrchestratorService.ApplyRuntimeWarningEvidence(
            RuntimeWarningTracker.Empty,
            "outbound/vless[vless-out]: status code: 403");

        Assert.Equal(RuntimeWarningEvidence.AuthenticationFailure, tracker.Warning);
        Assert.Equal(RuntimeWarningStrength.Strong, tracker.Strength);
        Assert.Equal(0, tracker.WeakConnectionEvidenceCount);
    }

    [Theory]
    [InlineData(SingBoxStatus.Stopped, true)]
    [InlineData(SingBoxStatus.Restarting, true)]
    [InlineData(SingBoxStatus.Crashed, true)]
    [InlineData(SingBoxStatus.Starting, false)]
    [InlineData(SingBoxStatus.Running, false)]
    public void IsRuntimeWarningResetBoundary_MatchesSafeSessionBoundaries(SingBoxStatus status, bool expected)
    {
        var result = OrchestratorService.IsRuntimeWarningResetBoundary(status);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ApplyRuntimeWarningResetBoundary_ClearsWeakAggregationState()
    {
        var tracker = OrchestratorService.ApplyRuntimeWarningEvidence(
            RuntimeWarningTracker.Empty,
            "connection download closed: wsarecv: An existing connection was forcibly closed by the remote host");

        var reset = OrchestratorService.ApplyRuntimeWarningResetBoundary(tracker, SingBoxStatus.Restarting);

        Assert.Equal(RuntimeWarningTracker.Empty, reset);
    }

    [Fact]
    public async Task StartCaptureAsync_WhenStartAlreadyInProgress_FailsClosedWithoutSecondStart()
    {
        var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var singBox = new FakeSingBoxManager(order) { BlockStart = startGate };
        var tun = new FakeTunOrchestrator(order);
        using var harness = await CreateHarnessAsync(singBox, tun);

        var firstStart = harness.Service.StartCaptureAsync();
        await singBox.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondStart = harness.Service.StartCaptureAsync();
        await secondStart.WaitAsync(TimeSpan.FromSeconds(1));

        var duringStart = await harness.PipeServer.GetStateHandler!();

        Assert.Equal(1, singBox.StartCalls);
        Assert.Equal(TunnelLifecycleState.Starting, duringStart.LifecycleState);
        Assert.False(duringStart.CaptureRunning);

        startGate.SetResult(true);
        await firstStart.WaitAsync(TimeSpan.FromSeconds(1));

        var finalState = await harness.PipeServer.GetStateHandler!();
        Assert.Equal(TunnelLifecycleState.Running, finalState.LifecycleState);
        Assert.True(finalState.CaptureRunning);
    }

    [Fact]
    public async Task StartCaptureAsync_WhenStopInProgress_FailsClosedWithoutQueuingNewStart()
    {
        var stopGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var singBox = new FakeSingBoxManager(order) { BlockStop = stopGate };
        var tun = new FakeTunOrchestrator(order);
        using var harness = await CreateHarnessAsync(singBox, tun);

        await harness.Service.StartCaptureAsync();

        var stopTask = harness.Service.StopCaptureAsync();
        await singBox.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var overlappingStart = harness.Service.StartCaptureAsync();
        await overlappingStart.WaitAsync(TimeSpan.FromSeconds(1));

        var duringStop = await harness.PipeServer.GetStateHandler!();

        Assert.Equal(1, singBox.StartCalls);
        Assert.Equal(1, singBox.StopCalls);
        Assert.Equal(TunnelLifecycleState.Stopping, duringStop.LifecycleState);
        Assert.True(duringStop.CaptureRunning);

        stopGate.SetResult(true);
        await stopTask.WaitAsync(TimeSpan.FromSeconds(1));

        var finalState = await harness.PipeServer.GetStateHandler!();
        Assert.Equal(TunnelLifecycleState.Stopped, finalState.LifecycleState);
        Assert.False(finalState.CaptureRunning);
    }

    [Fact]
    public async Task StopCaptureAsync_StopsSingBoxBeforeTunAndMarksStopped()
    {
        var order = new List<string>();
        var singBox = new FakeSingBoxManager(order);
        var tun = new FakeTunOrchestrator(order);
        using var harness = await CreateHarnessAsync(singBox, tun);

        await harness.Service.StartCaptureAsync();
        await harness.Service.StopCaptureAsync();

        var state = await harness.PipeServer.GetStateHandler!();

        Assert.Equal(
            ["tun:start", "singbox:start", "singbox:stop", "tun:stop"],
            order);
        Assert.Equal(TunnelLifecycleState.Stopped, state.LifecycleState);
        Assert.False(state.CaptureRunning);
        Assert.Equal(SingBoxStatus.Stopped, state.SingBoxStatus);
    }

    [Fact]
    public async Task StartCaptureAsync_WhenSingBoxStartupFails_CleansUpAndLeavesStoppedState()
    {
        var order = new List<string>();
        var singBox = new FakeSingBoxManager(order)
        {
            StartException = new InvalidOperationException("startup-fatal-tun-log-line")
        };
        var tun = new FakeTunOrchestrator(order);
        using var harness = await CreateHarnessAsync(singBox, tun);

        await harness.Service.StartCaptureAsync("owner-1");

        var state = await harness.PipeServer.GetStateHandler!();

        Assert.Equal(
            ["tun:start", "singbox:start", "singbox:stop", "tun:stop"],
            order);
        Assert.Equal(TunnelLifecycleState.Stopped, state.LifecycleState);
        Assert.False(state.CaptureRunning);
        Assert.Equal(SingBoxStatus.Stopped, state.SingBoxStatus);
        Assert.Null(state.ActiveOwnerSessionId);
        Assert.Equal(1, singBox.StartCalls);
        Assert.Equal(1, singBox.StopCalls);
    }

    [Fact]
    public async Task OwnerHeartbeat_KeepsOwnerLeaseAlive()
    {
        var singBox = new FakeSingBoxManager([]);
        var tun = new FakeTunOrchestrator([]);
        using var harness = await CreateHarnessAsync(singBox, tun);

        await harness.Service.StartCaptureAsync("owner-1");
        await harness.PipeServer.OwnerHeartbeatHandler!("owner-1");
        await harness.Service.HandleOwnerLeaseTickAsync(DateTimeOffset.UtcNow + OrchestratorService.OwnerLeaseTimeout - TimeSpan.FromSeconds(1));

        var state = await harness.PipeServer.GetStateHandler!();

        Assert.Equal("owner-1", state.ActiveOwnerSessionId);
        Assert.Equal(TunnelLifecycleState.Running, state.LifecycleState);
        Assert.Equal(0, singBox.StopCalls);
    }

    [Fact]
    public async Task OwnerLeaseExpiry_AutoStopsTunnel()
    {
        var order = new List<string>();
        var singBox = new FakeSingBoxManager(order);
        var tun = new FakeTunOrchestrator(order);
        using var harness = await CreateHarnessAsync(singBox, tun);

        await harness.Service.StartCaptureAsync("owner-1");
        await harness.Service.HandleOwnerLeaseTickAsync(DateTimeOffset.UtcNow + OrchestratorService.OwnerLeaseTimeout + TimeSpan.FromSeconds(1));

        var state = await harness.PipeServer.GetStateHandler!();

        Assert.Equal(
            ["tun:start", "singbox:start", "singbox:stop", "tun:stop"],
            order);
        Assert.Equal(TunnelLifecycleState.Stopped, state.LifecycleState);
        Assert.Null(state.ActiveOwnerSessionId);
    }

    [Fact]
    public async Task OwnerLease_TransientGapShorterThanTimeout_DoesNotStopTunnel()
    {
        var singBox = new FakeSingBoxManager([]);
        var tun = new FakeTunOrchestrator([]);
        using var harness = await CreateHarnessAsync(singBox, tun);

        await harness.Service.StartCaptureAsync("owner-1");
        await harness.Service.HandleOwnerLeaseTickAsync(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10));

        var state = await harness.PipeServer.GetStateHandler!();

        Assert.Equal(TunnelLifecycleState.Running, state.LifecycleState);
        Assert.Equal("owner-1", state.ActiveOwnerSessionId);
        Assert.Equal(0, singBox.StopCalls);
    }

    [Fact]
    public async Task OwnerHeartbeat_RepeatedFromSameOwner_IsSafe()
    {
        var singBox = new FakeSingBoxManager([]);
        var tun = new FakeTunOrchestrator([]);
        using var harness = await CreateHarnessAsync(singBox, tun);

        await harness.Service.StartCaptureAsync("owner-1");
        await harness.PipeServer.OwnerHeartbeatHandler!("owner-1");
        await harness.PipeServer.OwnerHeartbeatHandler!("owner-1");
        await harness.Service.HandleOwnerLeaseTickAsync(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1));

        var state = await harness.PipeServer.GetStateHandler!();

        Assert.Equal(TunnelLifecycleState.Running, state.LifecycleState);
        Assert.Equal("owner-1", state.ActiveOwnerSessionId);
        Assert.Equal(0, singBox.StopCalls);
    }

    private static async Task<ServiceHarness> CreateHarnessAsync(
        FakeSingBoxManager singBoxManager,
        FakeTunOrchestrator tunOrchestrator)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TunnelFlow-OrchestratorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var wintunPath = Path.Combine(tempDir, "wintun.dll");
        await File.WriteAllTextAsync(wintunPath, "test");
        tunOrchestrator.ResolvedWintunPath = wintunPath;

        var profileId = Guid.NewGuid();
        var configStore = new ConfigStore(Path.Combine(tempDir, "config.json"));
        await configStore.SaveAsync(new TunnelFlowConfig
        {
            UseTunMode = true,
            ActiveProfileId = profileId,
            Profiles =
            [
                new VlessProfile
                {
                    Id = profileId,
                    Name = "Primary",
                    ServerAddress = "example.com",
                    ServerPort = 443,
                    UserId = "11111111-1111-1111-1111-111111111111",
                    Network = "tcp",
                    Security = "tls"
                }
            ]
        });

        var pipeServer = new PipeServer(NullLogger<PipeServer>.Instance);
        var service = new OrchestratorService(
            singBoxManager,
            tunOrchestrator,
            configStore,
            pipeServer,
            NullLogger<OrchestratorService>.Instance);

        return new ServiceHarness(tempDir, service, pipeServer);
    }

    private sealed class ServiceHarness : IDisposable
    {
        public ServiceHarness(string tempDir, OrchestratorService service, PipeServer pipeServer)
        {
            TempDir = tempDir;
            Service = service;
            PipeServer = pipeServer;
        }

        public string TempDir { get; }

        public OrchestratorService Service { get; }

        public PipeServer PipeServer { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(TempDir, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup for focused tests.
            }
        }
    }

    private sealed class FakeTunOrchestrator(List<string> order) : ITunOrchestrator
    {
        public string ResolvedWintunPath { get; set; } = string.Empty;

        public bool SupportsActivation => true;

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public Task StartAsync(TunOrchestrationConfig config, CancellationToken cancellationToken)
        {
            StartCalls++;
            order.Add("tun:start");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            order.Add("tun:stop");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSingBoxManager(List<string> order) : TunnelFlow.Core.ISingBoxManager
    {
        private SingBoxStatus _status = SingBoxStatus.Stopped;

        public TaskCompletionSource<bool> StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> StopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool>? BlockStart { get; init; }

        public TaskCompletionSource<bool>? BlockStop { get; init; }

        public Exception? StartException { get; init; }

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public event EventHandler<SingBoxStatus>? StatusChanged;

        public event EventHandler<string>? LogLine
        {
            add { }
            remove { }
        }

        public async Task StartAsync(VlessProfile profile, SingBoxConfig config, CancellationToken ct)
        {
            StartCalls++;
            order.Add("singbox:start");
            SetStatus(SingBoxStatus.Starting);
            StartEntered.TrySetResult(true);

            if (BlockStart is not null)
            {
                await BlockStart.Task.WaitAsync(ct);
            }

            if (StartException is not null)
            {
                throw StartException;
            }

            SetStatus(SingBoxStatus.Running);
        }

        public async Task StopAsync(CancellationToken ct)
        {
            StopCalls++;
            order.Add("singbox:stop");
            StopEntered.TrySetResult(true);

            if (BlockStop is not null)
            {
                await BlockStop.Task.WaitAsync(ct);
            }

            SetStatus(SingBoxStatus.Stopped);
        }

        public Task RestartAsync(CancellationToken ct) => Task.CompletedTask;

        public SingBoxStatus GetStatus() => _status;

        public void Dispose()
        {
        }

        private void SetStatus(SingBoxStatus status)
        {
            _status = status;
            StatusChanged?.Invoke(this, status);
        }
    }
}
