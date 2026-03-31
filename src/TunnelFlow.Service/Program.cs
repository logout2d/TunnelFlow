using TunnelFlow.Capture;
using TunnelFlow.Capture.Interop;
using TunnelFlow.Capture.Policy;
using TunnelFlow.Capture.ProcessResolver;
using TunnelFlow.Capture.SessionRegistry;
using TunnelFlow.Capture.TcpRedirect;
using TunnelFlow.Core;
using TunnelFlow.Service;
using TunnelFlow.Service.Configuration;
using TunnelFlow.Service.Ipc;
using TunnelFlow.Service.SingBox;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
    options.ServiceName = "TunnelFlow");

builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<SingBoxConfigBuilder>();
builder.Services.AddSingleton<ISingBoxManager, SingBoxManager>();
// Fallback: use StubPacketDriver if WinpkFilter driver is not installed.
// builder.Services.AddSingleton<IPacketDriver, StubPacketDriver>();
builder.Services.AddSingleton<IPacketDriver, WinpkFilterPacketDriver>();
builder.Services.AddSingleton<IOriginalDestinationStore, InMemoryOriginalDestinationStore>();
builder.Services.AddSingleton<NoOpTcpRedirectProvider>();
builder.Services.AddSingleton<WfpTcpRedirectProvider>();
builder.Services.AddSingleton<ITcpRedirectProvider, FeatureFlagTcpRedirectProvider>();
builder.Services.AddSingleton<IProcessResolver, WindowsProcessResolver>();
builder.Services.AddSingleton<ISessionRegistry, InMemorySessionRegistry>();
builder.Services.AddSingleton<IPolicyEngine>(sp =>
    new PolicyEngine([], sp.GetRequiredService<ILogger<PolicyEngine>>()));
builder.Services.AddSingleton<ICaptureEngine>(sp =>
    new CaptureEngine(
        sp.GetRequiredService<IPacketDriver>(),
        sp.GetRequiredService<IProcessResolver>(),
        sp.GetRequiredService<ISessionRegistry>(),
        sp.GetRequiredService<IPolicyEngine>(),
        sp.GetRequiredService<ITcpRedirectProvider>(),
        sp.GetRequiredService<ILogger<CaptureEngine>>(),
        sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<PipeServer>();
builder.Services.AddHostedService<OrchestratorService>();

var host = builder.Build();
host.Run();
